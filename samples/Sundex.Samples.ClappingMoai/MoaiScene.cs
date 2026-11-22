using ClappingMoai.Markup;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Common.Input;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Sundex.Components;
using Sundex.Components.Abstractions;
using Sundex.Engine;
using Sundex.Engine.Renderer;
using Sundex.Engine.Scenes;
using Sundex.Engine.Scenes.Arguments;
using Sundex.Markup;
using Sundex.Reactive;

namespace ClappingMoai;

/// <summary>
///     The one scene: the world underneath, the markup overlay on top, and the input that drives
///     both. Space, the up arrow, W, Enter or a click claps.
/// </summary>
public sealed class MoaiScene(Game game) : Scene(game)
{
    private readonly GameState _state = new();
    private World _world = null!;
    private UICamera _camera = null!;
    private UIContext _ui = null!;
    private UIElement _overlay = null!;
    private Vector2 _scale = Vector2.One;
    private IDisposable? _resultsTimer;

    public override void Initialize(InitArguments initArguments)
    {
        _world = new World(AssetProvider)
        {
            Scored = () => _state.Score.Value++,
            Crashed = EndRun
        };
        _state.Again = StartRun;
        BuildOverlay();
        Resize(Game.FramebufferSize.X, Game.FramebufferSize.Y);
    }

    public override void Start()
    {
    }

    public override void TransitionedTo()
    {
    }

    public override void Update(UpdateArguments updateArgs)
    {
        _world.Update((float)updateArgs.Delta, _state.Phase.Peek);
        _overlay.Layout();
        var cursor = _ui.CursorAtPointer == CursorType.Pointer ? MouseCursor.PointingHand : MouseCursor.Default;
        if (Game.Cursor != cursor) Game.Cursor = cursor;
    }

    public override void Render(RenderArguments renderArgs)
    {
        _world.Render(renderArgs.Commands.WithCamera(_world.Camera));
        _ui.Render(renderArgs.Commands);
    }

    public override void Resize(int width, int height)
    {
        // A resize can reach a loaded scene before it initializes.
        if (_ui == null) return;
        if (Game.TryGetScreenScale(out var scaleX, out var scaleY)) _scale = new Vector2(scaleX, scaleY);
        _camera.Viewport = new Vector2i((int)(width / _scale.X), (int)(height / _scale.Y));
        _camera.UpdateMatrix();
        _ui.PixelScale = _scale;
        _overlay.InvalidateMeasure();
        _world.Resize(width, height);
    }

    public override void Mouse(MouseState mouseState, KeyboardState keyboardState)
    {
        _overlay.Test(mouseState, _scale);
        // The results card has a button of its own, so a stray click there doesn't restart.
        if (mouseState.IsButtonPressed(MouseButton.Left) && _state.Phase.Peek != Phase.Over) Clap();
    }

    public override void KeyDown(KeyboardKeyEventArgs args)
    {
        if (args.IsRepeat) return;
        if (args.Key is Keys.Space or Keys.Up or Keys.W or Keys.Enter) Clap();
    }

    public override void Keyboard(KeyboardState state)
    {
    }

    public override void TextInput(TextInputEventArgs args)
    {
    }

    public override void FileDrop(string[] locations)
    {
    }

    /// <summary>Edits to the markup's stylesheet show up while the game runs, in Debug builds.</summary>
    public override void ReloadStyles() => ReloadUI();

    public override void ReloadUI()
    {
        // Into a fresh context first: a half-saved sheet throws here and leaves the old overlay up.
        var previous = (_ui, _overlay);
        try
        {
            BuildOverlay();
        }
        catch
        {
            (_ui, _overlay) = previous;
            throw;
        }

        Resize(Game.FramebufferSize.X, Game.FramebufferSize.Y);
    }

    public override void Shutdown()
    {
        _resultsTimer?.Dispose();
        _world.Dispose();
    }

    private void BuildOverlay()
    {
        _camera ??= new UICamera(Vector3.Zero, Game.ClientSize);
        _ui = new UIContext { Camera = _camera };
        _overlay = new Overlay(new SundexContext<GameState>(_ui, _state)).Create();
        _overlay.DrawTo(_ui);
    }

    private void Clap()
    {
        switch (_state.Phase.Peek)
        {
            case Phase.Title:
                StartRun();
                break;
            case Phase.Playing:
                _world.Clap();
                break;
            case Phase.Over when _state.ShowResults.Peek:
                StartRun();
                break;
        }
    }

    private void StartRun()
    {
        _resultsTimer?.Dispose();
        _state.ShowResults.Value = false;
        _state.NewBest.Value = false;
        _state.Score.Value = 0;
        _state.Phase.Value = Phase.Playing;
        _world.Reset();
        _world.Clap();
    }

    private void EndRun()
    {
        _state.Phase.Value = Phase.Over;
        if (_state.Score.Peek > _state.Best.Peek)
        {
            _state.Best.Value = _state.Score.Peek;
            _state.NewBest.Value = true;
            _state.SaveBest();
        }

        // Let the moai land before the card covers it.
        _resultsTimer = Frame.After(700, () => _state.ShowResults.Value = true);
    }
}
