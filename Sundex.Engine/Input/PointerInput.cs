using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Sundex.Engine.Input;

/// <summary>Pointer position in logical pixels and button state for one update.</summary>
/// <param name="position">Position in logical pixels.</param>
/// <param name="delta">Movement since the last update.</param>
/// <param name="scrollDelta">Wheel movement since the last update.</param>
/// <param name="buttons">Held buttons as bits indexed by <see cref="MouseButton" />.</param>
/// <param name="previousButtons">Buttons held at the previous update.</param>
/// <param name="pressed">Press events since the last update, including completed clicks.</param>
/// <param name="released">Release events since the last update, including completed clicks.</param>
public readonly struct PointerInput(Vector2 position, Vector2 delta, Vector2 scrollDelta, uint buttons,
    uint previousButtons, uint pressed = 0, uint released = 0)
{
    private readonly uint _buttons = buttons;
    private readonly uint _previousButtons = previousButtons;
    private readonly uint _pressed = pressed | (buttons & ~previousButtons);
    private readonly uint _released = released | (previousButtons & ~buttons);

    public Vector2 Position { get; } = position;
    public Vector2 Delta { get; } = delta;
    public Vector2 ScrollDelta { get; } = scrollDelta;
    public float X => Position.X;
    public float Y => Position.Y;
    public bool IsAnyButtonDown => _buttons != 0;

    public bool IsButtonDown(MouseButton button) => HasButton(_buttons, button);
    public bool WasButtonDown(MouseButton button) => HasButton(_previousButtons, button);
    public bool IsButtonPressed(MouseButton button) => HasButton(_pressed, button);
    public bool IsButtonReleased(MouseButton button) => HasButton(_released, button);

    /// <summary>The bit <paramref name="button" /> takes in the button masks.</summary>
    public static uint Bit(MouseButton button) => (uint)button < 32 ? 1u << (int)button : 0;

    private static bool HasButton(uint buttons, MouseButton button) => (buttons & Bit(button)) != 0;
}
