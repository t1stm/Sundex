using OpenTK.Windowing.Common;
using OpenTK.Windowing.Common.Input;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Sundex.Engine.Input;
using Sundex.Components.Abstractions;
using Sundex.Engine;
using Sundex.Engine.Audio;
using Sundex.Engine.Scenes;
using Sundex.Engine.Scenes.Arguments;

namespace FlyingMoai.Desktop;

internal sealed class MoaiScene(GameRuntime runtime) : Scene(runtime)
{
    private AudioDevice? _audio;
    private MoaiGame? _moai;

    public override void Initialize(InitArguments initArguments)
    {
        _audio = AudioDevice.Open(Logger);
        var clap = _audio.CreateBuffer(MoaiGame.ClapSound(_audio.SampleRate), 1, _audio.SampleRate);
        _moai = new MoaiGame(AssetProvider, new GameState(BestScoreFile.Load(), BestScoreFile.Save))
        {
            Clapped = () => clap.PlayOnce(0.6f)
        };
        _moai.Initialize(Runtime.ClientSize, Runtime.FramebufferSize);
    }


    public override void Update(UpdateArguments updateArgs)
    {
        _moai!.Update(updateArgs.Delta);
        var cursor = _moai.Cursor == CursorType.Pointer ? MouseCursor.PointingHand : MouseCursor.Default;
        if (Runtime.Host.Cursor != cursor) Runtime.Host.Cursor = cursor;
    }

    public override void Render(RenderArguments renderArgs) => _moai!.Render(renderArgs.Commands);

    public override void Resize(int width, int height) => _moai?.Resize(Runtime.ClientSize, (width, height));

    public override void Mouse(PointerInput mouse, KeyboardInput keyboardState) =>
        _moai!.Pointer(mouse.X, mouse.Y, mouse.IsButtonDown(MouseButton.Left),
            mouse.IsButtonPressed(MouseButton.Left), mouse.IsButtonReleased(MouseButton.Left),
            mouse.ScrollDelta, mouse.IsButtonDown(MouseButton.Right));

    public override void KeyDown(KeyboardKeyEventArgs args)
    {
        if (!args.IsRepeat && args.Key is Keys.Space or Keys.Up or Keys.W or Keys.Enter) _moai!.Clap();
    }

    public override void ReloadStyles() => ReloadUI();
    public override void ReloadUI() => _moai!.ReloadUI(Runtime.ClientSize, Runtime.FramebufferSize);
    public override void Shutdown()
    {
        _moai?.Dispose();
        _audio?.Dispose();
    }
    public override void Keyboard(KeyboardInput state) { }
    public override void Start() { }
    public override void TransitionedTo() { }
    public override void FileDrop(string[] locations) { }
}
