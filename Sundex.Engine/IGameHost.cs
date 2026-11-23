using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Common.Input;

namespace Sundex.Engine;

/// <summary>
///     The window or page a <see cref="GameRuntime" /> draws into: the desktop <see cref="Game" />, or a
///     browser host. Scenes reach it through <see cref="GameRuntime.Host" />.
/// </summary>
public interface IGameHost
{
    /// <summary>The drawing area's size in logical pixels.</summary>
    Vector2i ClientSize { get; }

    /// <summary>The drawing area's size in physical pixels.</summary>
    Vector2i FramebufferSize { get; }

    /// <summary>
    ///     Whether the pointer is over the drawing area. The last pointer position stays wherever
    ///     it was last seen inside, so UI that reacts to where the pointer is can't tell a pointer
    ///     resting at an edge from one that left across it without this.
    /// </summary>
    bool IsCursorInWindow { get; }

    /// <summary>Whether frames wait for the display.</summary>
    VSyncMode VSync { get; }

    string Title { get; set; }
    MouseCursor Cursor { get; set; }
    string ClipboardString { get; set; }

    void Close();
}
