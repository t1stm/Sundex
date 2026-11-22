using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using Serilog;
using Sundex.Components.Abstractions;
using Sundex.Engine.AssetManagement;
using Sundex.Engine.Audio;
using Sundex.Engine.Renderer;
using Sundex.Engine.Renderer.OpenGL;
using Sundex.Reactive;

namespace FlyingMoai.WASM;

[SupportedOSPlatform("browser")]
internal static partial class Program
{
    private static GLGraphicsDevice _device = null!;
    private static AssetProvider _assets = null!;
    private static MoaiGame _game = null!;
    private static GameState _state = null!;
    private static AudioDevice _audio = null!;

    private static void Main() { }

    [JSExport]
    internal static void Start(int width, int height, int pixelsWide, int pixelsHigh, int best)
    {
        BrowserContext.Open();
        var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
        _device = GLGraphicsDevice.CreateWebGL2(logger);
        _assets = new AssetProvider(logger, [typeof(AssetProvider).Assembly, typeof(MoaiGame).Assembly]);

        Frame.Install();
        Rx.OnError = (error, where) => logger.Error(error, "[Reactive] {Where} threw", where);
        _assets.PreloadGraphics();
        _device.Resize(pixelsWide, pixelsHigh);
        _state = new GameState(best, SaveBest);
        _audio = AudioDevice.Open(logger);
        var clap = _audio.CreateBuffer(MoaiGame.ClapSound(_audio.SampleRate), 1, _audio.SampleRate);
        _game = new MoaiGame(_assets, _state) { Clapped = () => clap.PlayOnce(0.6f) };
        _game.Initialize((width, height), (pixelsWide, pixelsHigh));
    }

    [JSExport]
    internal static void Resize(int width, int height, int pixelsWide, int pixelsHigh)
    {
        _device.Resize(pixelsWide, pixelsHigh);
        _game.Resize((width, height), (pixelsWide, pixelsHigh));
    }

    [JSExport]
    internal static bool Draw(double delta, bool verify)
    {
        _assets.Update();
        _assets.ThreadRunner.Update();
        Frame.Pump();
        _game.Update(delta);

        var commands = _device.BeginFrame();
        commands.BeginPass(new PassDescription(null, Vector4.Zero));
        _game.Render(commands);
        commands.EndPass();
        _device.EndFrame();
        if (verify)
        {
            var error = GL.GetError();
            if (error != ErrorCode.NoError) throw new InvalidOperationException($"WebGL reported {error}.");
        }

        return _game.Cursor == CursorType.Pointer;
    }

    [JSExport]
    internal static void Pointer(float x, float y, bool down, bool pressed, bool released) =>
        _game.Pointer(x, y, down, pressed, released, Vector2.Zero);

    [JSExport]
    internal static void Clap() => _game.Clap();

    [JSExport]
    internal static string Screen() => _state.ShowResults.Peek ? "Results" : _state.Phase.Peek.ToString();

    /// <summary>The browser has no thread for the audio feeder; main.js calls this from a timer.</summary>
    [JSExport]
    internal static void PumpAudio() => _audio.Pump();

    [JSExport]
    internal static bool AudioSuspended() => _audio.Suspended;

    [JSExport]
    internal static void StartToneTest(float volume) => ToneTest.Start(_audio, volume);

    [JSExport]
    internal static string ToneReport() => ToneTest.Report(_audio);

    [JSExport]
    internal static void Stress(int megabytes) => ToneTest.Stress(megabytes);

    [JSImport("saveBest", "moai")]
    private static partial void SaveBest(int best);
}
