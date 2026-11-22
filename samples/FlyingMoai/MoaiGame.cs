using FlyingMoai.Markup;
using OpenTK.Mathematics;
using Sundex.Components;
using Sundex.Components.Abstractions;
using Sundex.Engine.AssetManagement;
using Sundex.Engine.Renderer;
using Sundex.Markup;
using Sundex.Reactive;

namespace FlyingMoai;

/// <summary>
///     The one scene: the world underneath, the markup overlay on top, and the input that drives
///     both. Space, the up arrow, W, Enter or a click claps.
/// </summary>
public sealed class MoaiGame(AssetProvider assets, GameState state) : IDisposable
{
    private World _world = null!;
    private UICamera _camera = null!;
    private UIContext _ui = null!;
    private UIElement _overlay = null!;
    private Vector2 _scale = Vector2.One;
    private IDisposable? _resultsTimer;

    public void Initialize(Vector2i clientSize, Vector2i framebufferSize)
    {
        _world = new World(assets)
        {
            Scored = () => state.Score.Value++,
            Crashed = EndRun
        };
        state.Again = StartRun;
        _camera = new UICamera(Vector3.Zero, clientSize);
        BuildOverlay();
        Resize(clientSize, framebufferSize);
    }

    public CursorType Cursor => _ui.CursorAtPointer;

    public void Update(double delta)
    {
        _world.Update((float)delta, state.Phase.Peek);
        _overlay.Layout();
    }

    public void Render(ICommandList commands)
    {
        _world.Render(commands.WithCamera(_world.Camera));
        _ui.Render(commands);
    }

    public void Resize(Vector2i clientSize, Vector2i framebufferSize)
    {
        if (clientSize.X > 0 && clientSize.Y > 0)
            _scale = new Vector2((float)framebufferSize.X / clientSize.X, (float)framebufferSize.Y / clientSize.Y);
        _camera.Viewport = clientSize;
        _camera.UpdateMatrix();
        _ui.PixelScale = _scale;
        _overlay.InvalidateMeasure();
        _world.Resize(framebufferSize.X, framebufferSize.Y);
    }

    public void Pointer(float x, float y, bool down, bool pressed, bool released, Vector2 scroll, bool rightDown = false)
    {
        if (_overlay.Visible)
            _ui.UpdatePointer(_overlay, x, y, down, pressed, released, scroll, rightDown);
        // The results card has a button of its own, so a stray click there doesn't restart.
        if (pressed && state.Phase.Peek != Phase.Over) Clap();
    }

    public void ReloadUI(Vector2i clientSize, Vector2i framebufferSize)
    {
        // A half-saved sheet throws here and leaves the old overlay up.
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

        Resize(clientSize, framebufferSize);
    }

    public void Dispose()
    {
        _resultsTimer?.Dispose();
        _world.Dispose();
    }

    private void BuildOverlay()
    {
        _ui = new UIContext { Camera = _camera };
        _overlay = new Overlay(new SundexContext<GameState>(_ui, state)).Create();
        _overlay.DrawTo(_ui);
    }

    public void Clap()
    {
        switch (state.Phase.Peek)
        {
            case Phase.Title:
                StartRun();
                break;
            case Phase.Playing:
                _world.Clap();
                break;
            case Phase.Over when state.ShowResults.Peek:
                StartRun();
                break;
        }
    }

    private void StartRun()
    {
        _resultsTimer?.Dispose();
        state.ShowResults.Value = false;
        state.NewBest.Value = false;
        state.Score.Value = 0;
        state.Phase.Value = Phase.Playing;
        _world.Reset();
        _world.Clap();
    }

    private void EndRun()
    {
        state.Phase.Value = Phase.Over;
        if (state.Score.Peek > state.Best.Peek)
        {
            state.Best.Value = state.Score.Peek;
            state.NewBest.Value = true;
            state.SaveBest();
        }

        // Let the moai land before the card covers it.
        _resultsTimer = Frame.After(700, () => state.ShowResults.Value = true);
    }
}
