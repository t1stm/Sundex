using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Serilog;
using Sundex.Core.Renderer.Planes;
using Sundex.Engine;
using Sundex.Engine.AssetManagement;
using Sundex.Engine.AssetManagement.Extensions;
using Sundex.Engine.AssetManagement.Types.Asset;
using Sundex.Engine.Renderer.Cameras;

[assembly: SundexAssets]

using var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
using var game = new SampleGame(logger);
game.Run();

if (game.Frames != SampleGame.FrameLimit) return 1;
Console.WriteLine($"Drew {game.Frames} frames with Sundex.Core.");
return 0;

sealed class SampleGame(ILogger logger) : Game(logger, [], new GameWindowSettings(), new NativeWindowSettings
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
    private ColoredPlane _plane = null!;

    protected override void OnLoad()
    {
        base.OnLoad();
        var message = AssetProvider.LoadString("Assets/message.txt", StorageLocation.Assembly);
        if (message.Trim() != "Sundex core-only sample") throw new InvalidOperationException("Embedded asset not loaded.");
        _camera.UpdateMatrix();
        _plane = new ColoredPlane { Color = (0.2f, 0.6f, 0.9f, 1f), Position = (64, 64, 0), Scale = (128, 128, 1) };
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        GL.ClearColor(0, 0, 0, 1);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        _plane.Render(_camera);
        if (Frames is 0 or FrameLimit - 1)
        {
            Span<byte> pixel = stackalloc byte[4];
            GL.ReadPixels(128, 128, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
            if (pixel[2] < 150) throw new InvalidOperationException("The plane did not reach the framebuffer.");
        }

        Context.SwapBuffers();
        if (++Frames == FrameLimit) Close();
    }

    private sealed class SampleCamera() : Camera(Vector3.Zero, new Vector2i(256, 256));
}
