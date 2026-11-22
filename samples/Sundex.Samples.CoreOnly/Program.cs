using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Serilog;
using SixLabors.ImageSharp.PixelFormats;
using Sundex.Core;
using Sundex.Core.Animations;
using Sundex.Core.Renderer.Planes;
using Sundex.Core.Renderer.Planes.Uniforms;
using Sundex.Engine;
using Sundex.Engine.AssetManagement;
using Sundex.Engine.AssetManagement.Extensions;
using Sundex.Engine.AssetManagement.Types.Asset;
using Sundex.Engine.Renderer;
using Sundex.Engine.Renderer.Cameras;
using Sundex.Engine.Renderer.Textures;
using Sundex.Engine.Text;
using Sundex.Engine.Text.Fonts;

[assembly: SundexAssets]

using var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
using var game = new SampleGame(logger);
game.Run();

if (game.Frames != SampleGame.FrameLimit) return 1;
Console.WriteLine($"Drew {game.Frames} frames and passed pixel probes with Sundex.Core.");
return 0;

sealed class SampleGame(ILogger logger) : Game(logger, [typeof(ColoredPlane).Assembly], new GameWindowSettings(), new NativeWindowSettings
{
    ClientSize = (256, 256),
    APIVersion = new Version(3, 3),
    Title = "Sundex core-only sample",
    Vsync = VSyncMode.Off
}, "CoreOnly")
{
    public const int FrameLimit = 120;
    public int Frames { get; private set; }

    private readonly Camera _camera = new SampleCamera();
    private Renderable[] _planes = [];
    private ColoredPlane _clipped = null!;
    private TextBuffer _text = null!;
    private GPUTexture _texture = null!;
    private SixLabors.ImageSharp.Image<Rgba32> _pixels = null!;

    protected override void OnLoad()
    {
        base.OnLoad();
        var message = AssetProvider.LoadString("Assets/message.txt", StorageLocation.Assembly);
        if (message.Trim() != "Sundex core-only sample") throw new InvalidOperationException("Embedded asset not loaded.");
        _camera.UpdateMatrix();

        _pixels = new(2, 2, new Rgba32(32, 192, 240));
        _texture = new GPUTexture { Width = 2, Height = 2 };
        _texture.QueueUploadToGPU(_pixels.Frames.RootFrame);

        _planes =
        [
            new ColoredPlane { Color = (1, 0, 0, 1), Position = (16, 16, 0), Scale = (48, 48, 1) },
            new GradientPlane
            {
                GradientType = GradientType.Linear, GradientColors = [(0, 1, 0, 1), (0, 0.6f, 0, 1)],
                Position = (80, 16, 0), Scale = (48, 48, 1)
            },
            new TexturedPlane { Texture = _texture, Position = (144, 16, 0), Scale = (48, 48, 1) },
            new ColoredPlane { Color = (0.8f, 0, 0.8f, 1), Position = (16, 80, 0), Scale = (48, 48, 1) },
            new BoxPlane { BackgroundBlur = new BackgroundBlur(1), Position = (16, 80, 0), Scale = (48, 48, 1), BorderWidths = (4, 4, 4, 4), BorderColor = Vector4.One },
            new AnimatedPlane<ColoredPlane>(
                new ColoredPlane { Color = (0, 0, 1, 1), Position = (80, 80, 0), Scale = (48, 48, 1) },
                new KeyframedAnimation([]))
        ];

        _clipped = new ColoredPlane { Color = (1, 1, 0, 1), Position = (144, 80, 0), Scale = (48, 48, 1) };
        _text = new TextBuffer(new TextProvider(AssetProvider, new FontProvider(AssetProvider), "Lato Regular"));
        var line = _text.GetTextSlice("Sundex");
        line.FontSize = 32;
        line.Position = (16, 160, 0);
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        if (Frames == FrameLimit / 2) _planes[0].Shader.ReloadShader();
        var commands = BeginRenderFrame();
        var context = commands.WithCamera(_camera);
        foreach (var plane in _planes) plane.Render(context);
        commands.SetScissor(new Box2i(144, 96, 168, 128));
        _clipped.Render(context);
        commands.SetScissor(null);
        _text.Render(context);
        EndRenderFrame(commands);
        if (++Frames == FrameLimit) Close();
    }

    protected override void OnAfterRenderPass()
    {
        if (Frames is not (0 or FrameLimit - 1)) return;

        Probe("colored plane", 32, 32, (255, 0, 0, 255));
        Probe("textured plane", 160, 32, (32, 192, 240, 255));
        Probe("box border", 17, 104, (255, 255, 255, 255));
        Probe("box backdrop", 40, 104, (204, 0, 204, 255));
        Probe("box backdrop near its bottom", 40, 120, (204, 0, 204, 255));
        Probe("animated plane", 96, 104, (0, 0, 255, 255));
        Probe("scissor interior", 152, 104, (255, 255, 0, 255));
        Probe("same row outside scissor", 184, 104, (0, 0, 0, 0));
        Probe("same column outside scissor", 152, 88, (0, 0, 0, 0));

        Span<byte> pixel = stackalloc byte[4];
        Device.ReadPixels(new Box2i(100, 40, 101, 41), pixel);
        if (pixel[0] > 2 || pixel[1] < 150 || pixel[2] > 2 || pixel[3] < 253)
            throw new InvalidOperationException($"The gradient plane did not reach the framebuffer ({string.Join(", ", pixel.ToArray())}).");

        var textPixels = new byte[128 * 48 * 4];
        Device.ReadPixels(new Box2i(12, 145, 140, 193), textPixels);
        for (var index = 0; index < textPixels.Length; index += 4)
            if (textPixels[index] >= 240 && textPixels[index + 3] >= 240) return;
        throw new InvalidOperationException("The text did not reach the framebuffer.");
    }

    private void Probe(string label, int x, int y, (byte R, byte G, byte B, byte A) expected)
    {
        Span<byte> pixel = stackalloc byte[4];
        Device.ReadPixels(new Box2i(x, y, x + 1, y + 1), pixel);
        ReadOnlySpan<byte> channels = [expected.R, expected.G, expected.B, expected.A];
        for (var index = 0; index < pixel.Length; index++)
            if (Math.Abs(pixel[index] - channels[index]) > 2)
                throw new InvalidOperationException($"Unexpected {label} pixel at {x}, {y}: {string.Join(", ", pixel.ToArray())}.");
    }

    protected override void OnUnload()
    {
        _text.Dispose();
        _texture.Dispose();
        _pixels.Dispose();
        base.OnUnload();
    }

    private sealed class SampleCamera() : Camera(Vector3.Zero, new Vector2i(256, 256));
}
