using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Sundex.Engine.Input;

/// <summary>Keys held or pressed during one update.</summary>
/// <param name="held">Keys down at this update.</param>
/// <param name="pressed">Keys pressed since the last update, including ones already let go.</param>
public readonly struct KeyboardInput(KeySet held, KeySet pressed)
{
    private readonly KeySet _held = held;
    private readonly KeySet _pressed = pressed;

    public bool IsAnyKeyDown => !_held.IsEmpty;
    public bool IsAnyKeyPressed => !_pressed.IsEmpty;
    public bool IsKeyDown(Keys key) => _held.Contains(key);
    public bool IsKeyPressed(Keys key) => _pressed.Contains(key);
}
