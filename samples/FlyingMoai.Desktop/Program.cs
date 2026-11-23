using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Serilog;
using FlyingMoai;
using FlyingMoai.Desktop;
using Sundex.Engine;

using var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
using var game = new Game(logger, [typeof(MoaiGame).Assembly], new GameWindowSettings(), new NativeWindowSettings
{
    ClientSize = (480, 720),
    APIVersion = new Version(3, 3),
    Flags = ContextFlags.ForwardCompatible,
    Title = "Flying Moai",
    Vsync = VSyncMode.On
}, "FlyingMoai");

// Scenes touch the graphics device when they build, so they load once the window is up.
game.Runtime.Enqueue(runtime => runtime.SceneManager.LoadScene("moai", _ => new MoaiScene(runtime)));
game.Runtime.Enqueue(runtime => runtime.SceneManager.TransitionTo("moai"));
game.Run();
