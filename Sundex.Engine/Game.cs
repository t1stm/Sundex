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
using Sundex.Engine.Input;
using Sundex.Engine.Renderer;
using Sundex.Engine.Renderer.OpenGL;
using Sundex.Reactive;

using MILogger =  Microsoft.Extensions.Logging.ILogger;

namespace Sundex.Engine;

/// <summary>The desktop window: a GLFW window that hosts a <see cref="GameRuntime" />.</summary>
public class Game : GameWindow, IGameHost
{
    private readonly string _id;
    private readonly GLGraphicsDevice _device;
    private readonly Action _afterRenderPass;

    private static readonly (MouseButton Button, string Name)[] TracedButtons =
        [(MouseButton.Left, "left"), (MouseButton.Middle, "middle"), (MouseButton.Right, "right")];

    // Distinct: MouseButton has aliases (Left is Button1).
    private static readonly MouseButton[] PointerButtons = [.. Enum.GetValues<MouseButton>().Distinct()];

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
        _device = new GLGraphicsDevice(Logger, () => Context.SwapBuffers());
        _inputLogger = new SerilogLoggerFactory(serilogLogger).CreateLogger("Input");

        Runtime = new GameRuntime(Logger, assemblies, _device, this);
        _afterRenderPass = OnAfterRenderPass;

        if (!SundexDevelopment.Enabled) return;
        // The IDE callback and file watcher run on their own threads. Rebuilding the UI
        // touches GL objects, so reloads land on the render thread's frame queue.
        HotReload.Requested = scope => Runtime.Enqueue(runtime => runtime.SceneManager.Reload(scope));
        _sourceWatcher = new SourceWatcher(Logger, Runtime.AssetProvider.SourceRoots);
    }

    public ILogger Logger { get; }

    /// <summary>What runs the scenes in this window.</summary>
    public GameRuntime Runtime { get; }

    /// <summary>
    ///     Title and text shown when the fullscreen shortcut is pressed on Wayland - see
    ///     <see cref="GameRuntime.OnWindowActionUnavailable" />. Says what to do, not why: the GLFW
    ///     reason goes to the log.
    /// </summary>
    public const string WaylandFullscreenTitle = "Fullscreen is up to your desktop";

    /// <inheritdoc cref="WaylandFullscreenTitle" />
    public const string WaylandFullscreenMessage =
        "On Wayland, apps can't switch themselves to fullscreen.\n" +
        "Use your window manager's shortcut instead, often Super+F or F11.";

    /// <inheritdoc />
    public bool IsCursorInWindow { get; private set; }

    /// <summary>What the context supports; filled in once it exists, before any scene initializes.</summary>
    public IGraphicsDevice Device => _device;

    /// <inheritdoc cref="GameRuntime.TryGetScreenScale" />
    public bool TryGetScreenScale(out float horizontalScale, out float verticalScale) =>
        Runtime.TryGetScreenScale(out horizontalScale, out verticalScale);

    protected override void OnLoad()
    {
        base.OnLoad();
        _device.Initialize();
        LogSetup();

        // Enter/leave only report changes, so the state at startup has to be asked for.
        unsafe
        {
            IsCursorInWindow = GLFW.GetWindowAttrib(WindowPtr, WindowAttributeGetBool.Hovered);
        }

        RenderMarker.Debug("Game Window Initialized");
        Runtime.Initialize();

        AppDomain.CurrentDomain.UnhandledException +=
            (_, args) =>
            {
                Logger.Fatal(args.ExceptionObject as Exception,
                    "[Unhandled Exception]: ({GameName}, {Id}) ", nameof(Game), _id);
            };

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

    protected override void OnFramebufferResize(FramebufferResizeEventArgs e)
    {
        base.OnFramebufferResize(e);
        Runtime.Resize((e.Width, e.Height));
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        Runtime.Render(args.Time, _afterRenderPass);
        if (SundexDevelopment.Enabled) LogInput();
    }

    /// <summary>Runs after the frame pass and before presentation, when pixel readback reads the frame.</summary>
    protected virtual void OnAfterRenderPass() { }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);
        MakeCurrent();

        CursorState = CursorState.Normal;
        Runtime.Update(args.Time, ReadPointer(), ReadKeyboard());

        if (QuitShortcut && KeyboardState.IsKeyDown(Keys.LeftControl) && KeyboardState.IsKeyDown(Keys.Q))
            Close();
    }

    private PointerInput ReadPointer()
    {
        var mouse = MouseState;
        uint buttons = 0, previous = 0;
        foreach (var button in PointerButtons)
        {
            if (mouse.IsButtonDown(button)) buttons |= PointerInput.Bit(button);
            if (mouse.WasButtonDown(button)) previous |= PointerInput.Bit(button);
        }

        return new PointerInput(mouse.Position, mouse.Delta, mouse.ScrollDelta, buttons, previous);
    }

    private KeyboardInput ReadKeyboard()
    {
        var keyboard = KeyboardState;
        KeySet held = default, pressed = default;
        if (keyboard.IsAnyKeyDown)
            foreach (var key in TracedKeys)
            {
                if (!keyboard.IsKeyDown(key)) continue;
                held.Add(key);
                if (!keyboard.WasKeyDown(key)) pressed.Add(key);
            }

        return new KeyboardInput(held, pressed);
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
        Runtime.SceneManager.TextInput(e);
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

        Runtime.SceneManager.KeyDown(e);
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
            Runtime.OnWindowActionUnavailable?.Invoke(WaylandFullscreenTitle, WaylandFullscreenMessage);
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

        Runtime.SceneManager.FileDropped(e.FileNames);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        Runtime.SceneManager.Shutdown();
        if (_sourceWatcher == null) return;
        HotReload.Requested = null;
        _sourceWatcher.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _device is { IsDisposed: false })
        {
            MakeCurrent();
            Runtime.Dispose();
        }
        base.Dispose(disposing);
    }
}
