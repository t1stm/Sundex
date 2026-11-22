using ClappingMoai;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Serilog;
using Sundex.Engine;
using Sundex.Engine.AssetManagement;

[assembly: SundexAssets]

using var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
using var game = new Game(logger, [], new GameWindowSettings(), new NativeWindowSettings
{
    ClientSize = (480, 720),
    APIVersion = new Version(3, 3),
    Flags = ContextFlags.ForwardCompatible,
    Title = "Clapping Moai",
    Vsync = VSyncMode.On
}, "ClappingMoai");

// Scenes touch the graphics device when they build, so they load once the window is up.
game.Enqueue(instance => instance.SceneManager.LoadScene("moai", _ => new MoaiScene(instance)));
game.Enqueue(instance => instance.SceneManager.TransitionTo("moai"));
game.Run();
