using Sundex.Reactive;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Serilog;
using Serilog.Extensions.Logging;
using Sundex.Engine.AssetManagement;
using Sundex.Engine.Renderer;
using Sundex.Engine.Renderer.Abstract;
using Sundex.Engine.Renderer.OpenGL;
using Sundex.Engine.Renderer.Attributes;
using Sundex.Engine.Renderer.Textures;
using Sundex.Engine.Scenes;
using Sundex.Engine.Scenes.Arguments;
using Sundex.Engine.Threading;

using MILogger =  Microsoft.Extensions.Logging.ILogger;

namespace Sundex.Engine;

public class Game : GameWindow
{
    private readonly Queue<Action<Game>> _enqueuedEvents = new();
    private readonly string _id;
    private readonly GLGraphicsDevice _device;
    private GPUTexture? _frameTarget;
    private bool _offscreen;

    private static readonly (MouseButton Button, string Name)[] TracedButtons =
        [(MouseButton.Left, "left"), (MouseButton.Middle, "middle"), (MouseButton.Right, "right")];

    // Distinct: Keys has aliases, which would log one press twice.
    private static readonly Keys[] TracedKeys = [.. Enum.GetValues<Keys>().Where(key => key != Keys.Unknown).Distinct()];

    /// <summary>The input trace's logger and the pointer position it last wrote; see <see cref="LogInput" />.</summary>
    private readonly MILogger _inputLogger;

    private Vector2 _loggedPointer = new(float.NaN);

    /// <summary>Text events since the last drawn frame, held for <see cref="LogInput" />.</summary>
    private readonly List<string> _pendingText = [];
    private WindowState _preFullscreenState = WindowState.Normal;

    private readonly SourceWatcher? _sourceWatcher;

    public Game(ILogger serilogLogger, Assembly[] assemblies, GameWindowSettings gameSettings,
        NativeWindowSettings nativeWindowSettings, string id) :
        base(gameSettings, nativeWindowSettings)
    {
        _id = id;

        Logger = serilogLogger.ForContext<Game>();
        _device = new GLGraphicsDevice(this, Logger);
        _inputLogger = new SerilogLoggerFactory(serilogLogger).CreateLogger("Input");

        AssetProvider = new AssetProvider(Logger, [typeof(Game).Assembly, .. assemblies]);
        SceneManager = new SceneManager(Logger);

        if (SundexDevelopment.Enabled)
        {
            // The IDE callback and file watcher run on their own threads. Rebuilding the UI
            // touches GL objects, so reloads land on the render thread's frame queue.
            HotReload.Requested = scope => Enqueue(game => game.SceneManager.Reload(scope));
            _sourceWatcher = new SourceWatcher(Logger, AssetProvider.SourceRoots);
        }
    }

    public ILogger Logger { get; }
    public Assembly[] AssetAssemblies => AssetProvider.AssetAssemblies;
    public AssetProvider AssetProvider { get; }
    public SceneManager SceneManager { get; }
    public ThreadRunner ThreadRunner => AssetProvider.ThreadRunner;

    public GameGlobals Globals { get; } = new();

    /// <summary>
    ///     Title and text shown when the fullscreen shortcut is pressed on Wayland - see
    ///     <see cref="OnWindowActionUnavailable" />. Says what to do, not why: the GLFW
    ///     reason goes to the log.
    /// </summary>
    public const string WaylandFullscreenTitle = "Fullscreen is up to your desktop";

    /// <inheritdoc cref="WaylandFullscreenTitle" />
    public const string WaylandFullscreenMessage =
        "On Wayland, apps can't switch themselves to fullscreen.\n" +
        "Use your window manager's shortcut instead, often Super+F or F11.";

    /// <summary>
    ///     Raised with a title and a message when a window action the platform refuses is
    ///     attempted; today only fullscreen on Wayland. The engine has no UI of its own, so
    ///     the active scene wires this to whatever it shows dialogs with - unwired, the
    ///     refusal only reaches the log.
    /// </summary>
    public Action<string, string>? OnWindowActionUnavailable { get; set; }

    /// <summary>
    ///     Whether the pointer is over the window. <see cref="NativeWindow.MouseState" /> keeps
    ///     the last position it had inside, so UI that reacts to where the pointer is can't
    ///     tell a pointer resting at an edge from one that left across it without this.
    /// </summary>
    public bool IsCursorInWindow { get; private set; }

    /// <summary>What the context supports; filled in once it exists, before any scene initializes.</summary>
    public IGraphicsDevice Device => _device;

    /// <summary>
    ///     Ratio between physical framebuffer pixels and logical window (client) size, measured
    ///     as FramebufferSize / ClientSize rather than read from the platform's reported DPI or
    ///     content scale, which can disagree with the real framebuffer (on X11 a desktop scale
    ///     may only change Xft.dpi while the framebuffer stays 1:1 with the window). Correct on
    ///     every platform by construction, so no per-platform special-casing. False when the
    ///     client size is not yet known, in which case both scales come back as 1.
    /// </summary>
    public bool TryGetScreenScale(out float horizontalScale, out float verticalScale)
    {
        var client = ClientSize;
        var framebuffer = FramebufferSize;

        if (client.X <= 0 || client.Y <= 0)
        {
            horizontalScale = 1f;
            verticalScale = 1f;
            return false;
        }

        horizontalScale = (float)framebuffer.X / client.X;
        verticalScale = (float)framebuffer.Y / client.Y;
        return true;
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        _device.Initialize();
        LogSetup();

        // This thread is the UI thread from here on: awaits come back to it, and reactive state
        // written from anywhere else is caught in Debug builds.
        Frame.Install();
        Rx.OnError = (exception, where) => Logger.Error(exception, "[Reactive] {Where} threw", where);
        Clipboard.Read = () => ClipboardString ?? "";
        Clipboard.Write = text => ClipboardString = text;

        // Enter/leave only report changes, so the state at startup has to be asked for.
        unsafe
        {
            IsCursorInWindow = GLFW.GetWindowAttrib(WindowPtr, WindowAttributeGetBool.Hovered);
        }

        RenderMarker.Debug("Game Window Initialized");
        foreach (var assembly in AssetAssemblies) ReflectionPreloadObjects(assembly);

        AppDomain.CurrentDomain.UnhandledException +=
            (_, args) =>
            {
                Logger.Fatal(args.ExceptionObject as Exception,
                    "[Unhandled Exception]: ({GameName}, {Id}) ", nameof(Game), _id);
            };

        // Some windowing backends don't reliably deliver a framebuffer-resize event on the
        // very first frame, so the viewport and the scenes are sized from the current
        // framebuffer here as well as from the event.
        var framebufferSize = FramebufferSize;
        ApplyFramebufferSize(framebufferSize.X, framebufferSize.Y);

        RenderMarker.Debug("Finished OnLoad() Procedure");
    }

    /// <summary>
    ///     Writes the user's setup to the log once the window is up: every display with its
    ///     resolution, refresh rate and scale, the window's own size and pixel scale, the OS,
    ///     runtime and culture, and the GL driver with its extensions. Enough to tell a layout or
    ///     scaling fault in a bug report's log from a setup that is only unusual.
    /// </summary>
    private void LogSetup()
    {
        var logger = Logger.ForContext("SourceContext", "Setup");
        var monitors = Monitors.GetMonitors();

        foreach (var monitor in monitors)
        {
            var mode = monitor.CurrentVideoMode;
            logger.Information("Display {Name}: {Width}x{Height} @ {RefreshRate} Hz, scale {ScaleX}x{ScaleY}",
                monitor.Name, mode.Width, mode.Height, mode.RefreshRate, monitor.HorizontalScale,
                monitor.VerticalScale);
        }

        // CurrentMonitor goes by window position, which Wayland doesn't report: GLFW raises
        // FeatureUnavailable and OpenTK's default error callback throws, so it is never asked there.
        var platform = GLFW.GetPlatform();
        var display = platform == Platform.Wayland ? "an unknown display" : CurrentMonitor.Name;
        TryGetScreenScale(out var scaleX, out var scaleY);
        logger.Information(
            "Window on {Display}: {ClientWidth}x{ClientHeight}, framebuffer {FramebufferWidth}x{FramebufferHeight} " +
            "(scale {ScaleX}x{ScaleY}), {WindowState}, {Platform}",
            display, ClientSize.X, ClientSize.Y, FramebufferSize.X, FramebufferSize.Y,
            scaleX, scaleY, WindowState, platform);

        logger.Information("OS: {OS} ({OSArchitecture}), {Runtime} ({ProcessArchitecture}), culture {Culture}",
            RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture,
            RuntimeInformation.FrameworkDescription, RuntimeInformation.ProcessArchitecture,
            CultureInfo.CurrentCulture.Name);

    }

    private void ReflectionPreloadObjects(Assembly targetAssembly)
    {
        const string preloadMethodName = "Preload";

        // who doesn't love reflection in a small game engine?
        var types = targetAssembly
            .GetTypes()
            .Where(type => type.GetCustomAttribute<PreloadGraphicsContextAttribute>() != null);

        foreach (var type in types)
        {
            if (!typeof(IGamePreloadable).IsAssignableFrom(type))
                continue;

            var method = type.GetMethod(preloadMethodName);
            if (method is null)
                throw new Exception("Method not found");

            method.Invoke(null, [AssetProvider]);
        }
    }

    protected override void OnFramebufferResize(FramebufferResizeEventArgs e)
    {
        base.OnFramebufferResize(e);
        ApplyFramebufferSize(e.Width, e.Height);
    }

    private void ApplyFramebufferSize(int width, int height)
    {
        SceneManager.Resize(width, height);
        Device.Resize(width, height);
        _frameTarget?.Dispose();
        _frameTarget = new GPUTexture
        {
            Width = Math.Max(1, width), Height = Math.Max(1, height),
            Usage = TextureUsage.RenderTarget, MipmapMode = MipmapMode.Disabled
        };
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        var commands = BeginRenderFrame();
        SceneManager.Render(new RenderArguments { Commands = commands, Delta = args.Time });
        EndRenderFrame(commands);
    }

    protected ICommandList BeginRenderFrame()
    {
        var commands = Device.BeginFrame();
        // Offscreen while frames copy what they have drawn, as backdrop blur does, so a backend never
        // copies from the window. Otherwise straight to the window, which saves a full-screen copy.
        // ponytail: the first copying frame still reads the window, which OpenGL allows; a backend
        // that can't must draw offscreen every frame.
        _offscreen = _device.CopiedFromTargetLastFrame;
        commands.BeginPass(new PassDescription(_offscreen ? _frameTarget : null, Vector4.Zero));
        return commands;
    }

    protected void EndRenderFrame(ICommandList commands)
    {
        commands.EndPass();
        OnAfterRenderPass();
        if (_offscreen)
        {
            commands.BeginPass(new PassDescription());
            commands.CopyToTarget(_frameTarget!);
            commands.EndPass();
        }
        Device.EndFrame();
        if (SundexDevelopment.Enabled) LogInput();
    }

    /// <summary>Runs after the frame pass and before presentation, when pixel readback reads the frame.</summary>
    protected virtual void OnAfterRenderPass() { }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);
        MakeCurrent();

        CursorState = CursorState.Normal;
        AssetProvider.Update();
        ThreadRunner.Update();
        // Posted work, timers and tickers, then every effect whose state changed, before the
        // scenes lay out: ten writes in one handler cost one update.
        Frame.Pump();

        var initArguments = new InitArguments
        {
            StartingResolution = ClientSize,
            Capabilities = Device.Capabilities
        };

        lock (_enqueuedEvents)
        {
            // One per frame, not the whole queue: an enqueued event is typically a scene
            // being built, so spreading them keeps a single frame from stalling on all of
            // them at once.
            if (_enqueuedEvents.TryDequeue(out var action))
            {
                action(this);
                // initialize scenes enqueued using game.Enqueue() before anything else runs.
                SceneManager.Initialize(initArguments);
            }
        }

        SceneManager.Initialize(initArguments);

        if (KeyboardState.IsAnyKeyDown)
            SceneManager.Keyboard(KeyboardState);

        SceneManager.Mouse(MouseState, KeyboardState);
        SceneManager.Update(new UpdateArguments
        {
            Delta = args.Time
        });

        if (QuitShortcut && KeyboardState.IsKeyDown(Keys.LeftControl) && KeyboardState.IsKeyDown(Keys.Q))
            Close();
    }

    protected override void OnMouseEnter()
    {
        base.OnMouseEnter();
        IsCursorInWindow = true;
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        IsCursorInWindow = false;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        SceneManager.TextInput(e);
        LogTextInput(e);
    }

    /// <summary>
    ///     Writes the frame's input once the frame that handled it is on screen: pointer moves,
    ///     button and key presses and releases, scroll, and the text typed since the last frame.
    ///     Debug builds only, since a moving mouse writes a line a frame. A line means the polled
    ///     MouseState/KeyboardState saw the input and its result has been drawn - what a headless
    ///     driver needs before letting go of a button or taking a screenshot (viz.sh in the
    ///     visualizer-headless skill). Runs after SwapBuffers: OpenTK 5 renders once per update,
    ///     so the edges read here are the ones that update handled.
    /// </summary>
    private void LogInput()
    {
        foreach (var text in _pendingText) _inputLogger.TextEntered(text);
        _pendingText.Clear();

        var mouse = MouseState;
        var (x, y) = ((int)mouse.X, (int)mouse.Y);
        if (mouse.Position != _loggedPointer)
        {
            _loggedPointer = mouse.Position;
            _inputLogger.PointerMoved(x, y);
        }

        foreach (var (button, name) in TracedButtons)
        {
            if (mouse.IsButtonPressed(button)) _inputLogger.PointerPressed(name, x, y);
            if (mouse.IsButtonReleased(button)) _inputLogger.PointerReleased(name, x, y);
        }

        if (mouse.ScrollDelta != Vector2.Zero) _inputLogger.Scrolled(mouse.ScrollDelta.X, mouse.ScrollDelta.Y);

        foreach (var key in TracedKeys)
            if (KeyboardState.IsKeyPressed(key)) _inputLogger.KeyPressed(key);
            else if (KeyboardState.IsKeyReleased(key)) _inputLogger.KeyReleased(key);
    }

    /// <summary>Text arrives as events, not polled state, so it is held for the next drawn frame's <see cref="LogInput" />.</summary>
    private void LogTextInput(TextInputEventArgs args)
    {
        if (SundexDevelopment.Enabled) _pendingText.Add(args.AsString);
    }

    /// <summary>Whether Alt+Enter (Ctrl+Cmd+F on macOS) toggles fullscreen before the scene sees the key.</summary>
    public bool FullscreenShortcut { get; set; } = true;

    /// <summary>Whether Ctrl+Q closes the game before the scene sees it.</summary>
    public bool QuitShortcut { get; set; } = true;

    protected override void OnKeyDown(KeyboardKeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (FullscreenShortcut && !e.IsRepeat && IsFullscreenShortcut(e))
        {
            ToggleFullscreen();
            return;
        }

        SceneManager.KeyDown(e);
    }

    /// <summary>
    ///     Alt+Enter on Windows/Linux, Ctrl+Cmd+F on macOS - the platform-native
    ///     "toggle fullscreen" chord in both cases.
    /// </summary>
    private static bool IsFullscreenShortcut(KeyboardKeyEventArgs args)
    {
        if (OperatingSystem.IsMacOS())
            return args.Key == Keys.F &&
                   (args.Modifiers & (KeyModifiers.Super | KeyModifiers.Control)) ==
                   (KeyModifiers.Super | KeyModifiers.Control);

        return (args.Key is Keys.Enter or Keys.KeyPadEnter) && args.Modifiers.HasFlag(KeyModifiers.Alt);
    }

    private void ToggleFullscreen()
    {
        // GLFW's Wayland backend has no way to fullscreen a window on the application's
        // own initiative - the compositor owns window state there - so the assignment
        // below would silently do nothing. Say so instead of swallowing the key.
        if (GLFW.GetPlatform() == Platform.Wayland)
        {
            Logger.Information("[Fullscreen] Refused: GLFW leaves window state to the compositor on Wayland");
            OnWindowActionUnavailable?.Invoke(WaylandFullscreenTitle, WaylandFullscreenMessage);
            return;
        }

        if (WindowState == WindowState.Fullscreen)
        {
            // Restore whatever the window was before, so leaving fullscreen doesn't
            // silently un-maximize a maximized window.
            WindowState = _preFullscreenState;
            return;
        }

        _preFullscreenState = WindowState;
        WindowState = WindowState.Fullscreen;
    }

    protected override void OnFileDrop(FileDropEventArgs e)
    {
        base.OnFileDrop(e);
        if (e.FileNames.Length < 1) return;

        SceneManager.FileDropped(e.FileNames);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        SceneManager.Shutdown();
        if (_sourceWatcher == null) return;
        HotReload.Requested = null;
        _sourceWatcher.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _device is { IsDisposed: false })
        {
            MakeCurrent();
            _device.Dispose();
        }
        base.Dispose(disposing);
    }

    public void Enqueue(Action<Game> action)
    {
        lock (_enqueuedEvents)
        {
            _enqueuedEvents.Enqueue(action);
        }
    }
}
