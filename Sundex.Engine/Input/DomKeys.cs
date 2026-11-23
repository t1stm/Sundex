using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Sundex.Engine.Input;

/// <summary>Reads browser keyboard events as <see cref="Keys" /> and <see cref="KeyModifiers" />.</summary>
public static class DomKeys
{
    /// <summary>The key a <c>KeyboardEvent.code</c> names, or <see cref="Keys.Unknown" />.</summary>
    public static Keys FromCode(string code)
    {
        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal) && char.IsAsciiLetterUpper(code[3]))
            return (Keys)code[3];
        if (code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal) && char.IsAsciiDigit(code[5]))
            return (Keys)code[5];
        if (code.Length == 7 && code.StartsWith("Numpad", StringComparison.Ordinal) && char.IsAsciiDigit(code[6]))
            return Keys.KeyPad0 + (code[6] - '0');
        if (code.Length is 2 or 3 && code[0] == 'F' && int.TryParse(code.AsSpan(1), out var number) &&
            number is >= 1 and <= 25)
            return Keys.F1 + (number - 1);

        return code switch
        {
            "Space" => Keys.Space,
            "Quote" => Keys.Apostrophe,
            "Comma" => Keys.Comma,
            "Minus" => Keys.Minus,
            "Period" => Keys.Period,
            "Slash" => Keys.Slash,
            "Semicolon" => Keys.Semicolon,
            "Equal" => Keys.Equal,
            "BracketLeft" => Keys.LeftBracket,
            "Backslash" => Keys.Backslash,
            "BracketRight" => Keys.RightBracket,
            "Backquote" => Keys.GraveAccent,
            "Escape" => Keys.Escape,
            "Enter" => Keys.Enter,
            "Tab" => Keys.Tab,
            "Backspace" => Keys.Backspace,
            "Insert" => Keys.Insert,
            "Delete" => Keys.Delete,
            "ArrowRight" => Keys.Right,
            "ArrowLeft" => Keys.Left,
            "ArrowDown" => Keys.Down,
            "ArrowUp" => Keys.Up,
            "PageUp" => Keys.PageUp,
            "PageDown" => Keys.PageDown,
            "Home" => Keys.Home,
            "End" => Keys.End,
            "CapsLock" => Keys.CapsLock,
            "ScrollLock" => Keys.ScrollLock,
            "NumLock" => Keys.NumLock,
            "PrintScreen" => Keys.PrintScreen,
            "Pause" => Keys.Pause,
            "NumpadDecimal" => Keys.KeyPadDecimal,
            "NumpadDivide" => Keys.KeyPadDivide,
            "NumpadMultiply" => Keys.KeyPadMultiply,
            "NumpadSubtract" => Keys.KeyPadSubtract,
            "NumpadAdd" => Keys.KeyPadAdd,
            "NumpadEnter" => Keys.KeyPadEnter,
            "NumpadEqual" => Keys.KeyPadEqual,
            "ShiftLeft" => Keys.LeftShift,
            "ControlLeft" => Keys.LeftControl,
            "AltLeft" => Keys.LeftAlt,
            "MetaLeft" => Keys.LeftSuper,
            "ShiftRight" => Keys.RightShift,
            "ControlRight" => Keys.RightControl,
            "AltRight" => Keys.RightAlt,
            "MetaRight" => Keys.RightSuper,
            "ContextMenu" => Keys.Menu,
            _ => Keys.Unknown
        };
    }

    /// <summary>The modifiers a keyboard event reports, from its flags and lock states.</summary>
    public static KeyModifiers Modifiers(bool shift, bool control, bool alt, bool meta, bool capsLock, bool numLock) =>
        (shift ? KeyModifiers.Shift : 0) | (control ? KeyModifiers.Control : 0) | (alt ? KeyModifiers.Alt : 0) |
        (meta ? KeyModifiers.Super : 0) | (capsLock ? KeyModifiers.CapsLock : 0) |
        (numLock ? KeyModifiers.NumLock : 0);
}
