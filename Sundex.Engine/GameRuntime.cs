using System.Reflection;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using Serilog;
using Sundex.Engine.AssetManagement;
using Sundex.Engine.Input;
using Sundex.Engine.Renderer;
using Sundex.Engine.Renderer.OpenGL;
using Sundex.Engine.Renderer.Textures;
using Sundex.Engine.Scenes;
using Sundex.Engine.Scenes.Arguments;
using Sundex.Engine.Threading;
using Sundex.Reactive;

namespace Sundex.Engine;

/// <summary>
///     Runs scenes: assets, globals, the scene manager, queued work and each frame. The host owns
///     the graphics context and calls <see cref="Update" /> and <see cref="Render" /> once a frame,
///     on the thread that context is current on.
/// </summary>
public sealed class GameRuntime(ILogger logger, Assembly[] assemblies, GLGraphicsDevice device, IGameHost host)
    : IDisposable
{
    private readonly Queue<Action<GameRuntime>> _enqueuedEvents = new();
    private GPUTexture? _frameTarget;
    private bool _offscreen;

    public ILogger Logger { get; } = logger;
    public IGameHost Host { get; } = host;
    public AssetProvider AssetProvider { get; } = new(logger, [typeof(GameRuntime).Assembly, .. assemblies]);
    public Assembly[] AssetAssemblies => AssetProvider.AssetAssemblies;
    public SceneManager SceneManager { get; } = new(logger);
    public ThreadRunner ThreadRunner => AssetProvider.ThreadRunner;
    public GameGlobals Globals { get; } = new();

    /// <summary>What the context supports; filled in once it exists, before any scene initializes.</summary>
    public IGraphicsDevice Device => device;

    /// <inheritdoc cref="IGameHost.ClientSize" />
    public Vector2i ClientSize => Host.ClientSize;

    /// <inheritdoc cref="IGameHost.FramebufferSize" />
    public Vector2i FramebufferSize => Host.FramebufferSize;

    /// <summary>The pointer as the last update saw it.</summary>
    public PointerInput Pointer { get; private set; }

    /// <summary>Raised at the start of every update, before queued work and the scenes.</summary>
    public event Action<FrameEventArgs>? UpdateFrame;

    /// <summary>
    ///     Raised with a title and a message when a window action the platform refuses is
    ///     attempted; today only fullscreen on Wayland. The engine has no UI of its own, so
    ///     the active scene wires this to whatever it shows dialogs with - unwired, the
    ///     refusal only reaches the log.
    /// </summary>
    public Action<string, string>? OnWindowActionUnavailable { get; set; }

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

    /// <summary>Call once, with the initialized graphics context current.</summary>
    public void Initialize()
    {
        // This thread is the UI thread from here on: awaits come back to it, and reactive state
        // written from anywhere else is caught in Debug builds.
        Frame.Install();
        Rx.OnError = (exception, where) => Logger.Error(exception, "[Reactive] {Where} threw", where);
        Clipboard.Read = () => Host.ClipboardString;
        Clipboard.Write = text => Host.ClipboardString = text;

        AssetProvider.PreloadGraphics();

        // Some windowing backends don't reliably deliver a framebuffer-resize event on the
        // very first frame, so the viewport and the scenes are sized from the current
        // framebuffer here as well as from the event.
        Resize(FramebufferSize);
    }

    /// <summary>Sizes the viewport, the scenes and the offscreen frame to a new framebuffer.</summary>
    public void Resize(Vector2i framebufferSize)
    {
        var (width, height) = framebufferSize;
        SceneManager.Resize(width, height);
        Device.Resize(width, height);
        _frameTarget?.Dispose();
        _frameTarget = new GPUTexture
        {
            Width = Math.Max(1, width), Height = Math.Max(1, height),
            Usage = TextureUsage.RenderTarget, MipmapMode = MipmapMode.Disabled
        };
    }

    /// <summary>Runs <paramref name="action" /> on the frame thread at the start of a later update.</summary>
    public void Enqueue(Action<GameRuntime> action)
    {
        lock (_enqueuedEvents)
        {
            _enqueuedEvents.Enqueue(action);
        }
    }

    public void Update(double delta, PointerInput pointer, KeyboardInput keyboard)
    {
        Pointer = pointer;
        UpdateFrame?.Invoke(new FrameEventArgs(delta));
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
                // initialize scenes enqueued using Enqueue() before anything else runs.
                SceneManager.Initialize(initArguments);
            }
        }

        SceneManager.Initialize(initArguments);
        SceneManager.Input(pointer, keyboard);
        SceneManager.Update(new UpdateArguments { Delta = delta });
    }

    /// <param name="delta">Seconds since the last frame.</param>
    /// <param name="afterPass">Runs after the frame pass and before presentation, when pixel readback reads the frame.</param>
    public void Render(double delta, Action? afterPass = null)
    {
        var commands = Device.BeginFrame();
        // Offscreen while frames copy what they have drawn, as backdrop blur does, so a backend never
        // copies from the window. Otherwise straight to the window, which saves a full-screen copy.
        // ponytail: the first copying frame still reads the window, which OpenGL allows; a backend
        // that can't must draw offscreen every frame.
        _offscreen = device.CopiedFromTargetLastFrame;
        commands.BeginPass(new PassDescription(_offscreen ? _frameTarget : null, Vector4.Zero));
        SceneManager.Render(new RenderArguments { Commands = commands, Delta = delta });
        commands.EndPass();
        afterPass?.Invoke();
        if (_offscreen)
        {
            commands.BeginPass(new PassDescription());
            commands.CopyToTarget(_frameTarget!);
            commands.EndPass();
        }

        Device.EndFrame();
    }

    /// <summary>Releases the frame target and the device. Shut the scenes down first.</summary>
    public void Dispose()
    {
        _frameTarget?.Dispose();
        device.Dispose();
    }
}
