using OpenTK.Windowing.GraphicsLibraryFramework;
using UnityEngine.InputSystem;

namespace NowUI.Desktop
{
    internal static class DesktopKeys
    {
        // GLFW and Unity name physical positions using the US reference layout.
        internal static Key Map(Keys key)
        {
            if (key >= Keys.A && key <= Keys.Z) return Key.A + (key - Keys.A);
            if (key >= Keys.D1 && key <= Keys.D9) return Key.Digit1 + (key - Keys.D1);
            if (key >= Keys.KeyPad0 && key <= Keys.KeyPad9) return Key.Numpad0 + (key - Keys.KeyPad0);
            if (key >= Keys.F1 && key <= Keys.F12) return Key.F1 + (key - Keys.F1);
            if (key >= Keys.F13 && key <= Keys.F24) return Key.F13 + (key - Keys.F13);
            return key switch
            {
                Keys.D0 => Key.Digit0, Keys.Space => Key.Space, Keys.Enter => Key.Enter, Keys.Tab => Key.Tab,
                Keys.GraveAccent => Key.Backquote, Keys.Apostrophe => Key.Quote, Keys.Semicolon => Key.Semicolon,
                Keys.Comma => Key.Comma, Keys.Period => Key.Period, Keys.Slash => Key.Slash, Keys.Backslash => Key.Backslash,
                Keys.LeftBracket => Key.LeftBracket, Keys.RightBracket => Key.RightBracket, Keys.Minus => Key.Minus, Keys.Equal => Key.Equals,
                Keys.LeftShift => Key.LeftShift, Keys.RightShift => Key.RightShift, Keys.LeftAlt => Key.LeftAlt, Keys.RightAlt => Key.RightAlt,
                Keys.LeftControl => Key.LeftCtrl, Keys.RightControl => Key.RightCtrl, Keys.LeftSuper => Key.LeftMeta, Keys.RightSuper => Key.RightMeta,
                Keys.Menu => Key.ContextMenu, Keys.Escape => Key.Escape, Keys.Left => Key.LeftArrow, Keys.Right => Key.RightArrow,
                Keys.Up => Key.UpArrow, Keys.Down => Key.DownArrow, Keys.Backspace => Key.Backspace, Keys.PageDown => Key.PageDown,
                Keys.PageUp => Key.PageUp, Keys.Home => Key.Home, Keys.End => Key.End, Keys.Insert => Key.Insert, Keys.Delete => Key.Delete,
                Keys.CapsLock => Key.CapsLock, Keys.NumLock => Key.NumLock, Keys.PrintScreen => Key.PrintScreen,
                Keys.ScrollLock => Key.ScrollLock, Keys.Pause => Key.Pause, Keys.KeyPadEnter => Key.NumpadEnter,
                Keys.KeyPadDivide => Key.NumpadDivide, Keys.KeyPadMultiply => Key.NumpadMultiply, Keys.KeyPadAdd => Key.NumpadPlus,
                Keys.KeyPadSubtract => Key.NumpadMinus, Keys.KeyPadDecimal => Key.NumpadPeriod, Keys.KeyPadEqual => Key.NumpadEquals,
                (Keys)161 => Key.OEM1, (Keys)162 => Key.OEM2, // GLFW_KEY_WORLD_1/2 (unnamed in OpenTK's enum).
                _ => Key.None
            };
        }
    }
}
