using System.Runtime.InteropServices;

namespace Uuvr;

// Even though Unity has its own Input stuff, there are multiple input systems that can be used,
// and different Unity versions have slightly different APIs for the same input system.
// So instead of relying on those systems, we just make our own using native system calls.
public class KeyboardKey
{
    // Windows virtual-key codes.
    public enum KeyCode
    {
        None = 0x00,
        Backspace = 0x08,
        Tab = 0x09,
        Enter = 0x0D,
        Pause = 0x13,
        Escape = 0x1B,
        Space = 0x20,
        PageUp = 0x21,
        PageDown = 0x22,
        End = 0x23,
        Home = 0x24,
        LeftArrow = 0x25,
        UpArrow = 0x26,
        RightArrow = 0x27,
        DownArrow = 0x28,
        Insert = 0x2D,
        Delete = 0x2E,
        F1 = 0x70,
        F2 = 0x71,
        F3 = 0x72,
        F4 = 0x73,
        F5 = 0x74,
        F6 = 0x75,
        F7 = 0x76,
        F8 = 0x77,
        F9 = 0x78,
        F10 = 0x79,
        F11 = 0x7A,
        F12 = 0x7B,
        Numpad0 = 0x60,
        Numpad1 = 0x61,
        Numpad2 = 0x62,
        Numpad3 = 0x63,
        Numpad4 = 0x64,
        Numpad5 = 0x65,
        Numpad6 = 0x66,
        Numpad7 = 0x67,
        Numpad8 = 0x68,
        Numpad9 = 0x69,
    }

    private bool _previousIsKeyPressed;
    private bool _isKeyPressed;

    // Mutable so hotkeys can be rebound at runtime from the config / in-game menu.
    public KeyCode Key { get; set; }

    public KeyboardKey(KeyCode keyCode)
    {
        Key = keyCode;
    }

    public bool UpdateIsDown()
    {
        if (Key == KeyCode.None) return false;

        _previousIsKeyPressed = _isKeyPressed;
        _isKeyPressed = ((ushort)GetKeyState((int)Key) & 0x8000) != 0;

        return !_previousIsKeyPressed && _isKeyPressed;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern short GetKeyState(int keyCode);
}
