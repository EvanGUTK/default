using System.Runtime.InteropServices;
using static BlueyDesktop.Engine.NativeMethods;

namespace BlueyDesktop.Engine;

public enum MouseButton
{
    Left,
    Right,
}

/// <summary>The virtual screen (all monitors together), in the same pixel space as the cursor position.</summary>
public readonly record struct ScreenBounds(int Left, int Top, int Width, int Height)
{
    public bool Contains(int x, int y) => x >= Left && x < Left + Width && y >= Top && y < Top + Height;
}

/// <summary>The hands: raw keyboard and mouse input. Each call returns false if Windows refused the input.</summary>
internal interface IInputDriver
{
    ScreenBounds VirtualScreen { get; }

    (int X, int Y) GetCursor();

    void SetCursor(int x, int y);

    bool MoveMouse(int x, int y);

    bool SetMouseButton(MouseButton button, bool down);

    /// <summary>One wheel step; positive `amount` is away from the user (up, or right when horizontal).</summary>
    bool Wheel(int amount, bool horizontal);

    /// <summary>Modifiers down, key down and up, modifiers up, as a single batch.</summary>
    bool Chord(Shortcut shortcut);

    /// <summary>Types the text as Unicode key events, which works on any keyboard layout.</summary>
    bool TypeUnits(string text);
}

/// <summary>Sends real input with SendInput. It cannot reach elevated or secure-desktop windows (UIPI).</summary>
internal sealed class Win32InputDriver : IInputDriver
{
    private const int AbsoluteRange = 65535;

    public ScreenBounds VirtualScreen => new(
        GetSystemMetrics(SmXVirtualScreen), GetSystemMetrics(SmYVirtualScreen),
        GetSystemMetrics(SmCxVirtualScreen), GetSystemMetrics(SmCyVirtualScreen));

    public (int X, int Y) GetCursor() => GetCursorPos(out var point) ? (point.X, point.Y) : (0, 0);

    public void SetCursor(int x, int y) => SetCursorPos(x, y);

    public bool MoveMouse(int x, int y)
    {
        var screen = VirtualScreen;
        var input = MouseEvent(MouseEventMove | MouseEventAbsolute | MouseEventVirtualDesk, 0);
        input.u.mi.dx = Normalize(x - screen.Left, screen.Width);
        input.u.mi.dy = Normalize(y - screen.Top, screen.Height);
        return Send([input]);
    }

    public bool SetMouseButton(MouseButton button, bool down)
    {
        var flags = (button, down) switch
        {
            (MouseButton.Left, true) => MouseEventLeftDown,
            (MouseButton.Left, false) => MouseEventLeftUp,
            (_, true) => MouseEventRightDown,
            _ => MouseEventRightUp,
        };
        return Send([MouseEvent(flags, 0)]);
    }

    public bool Wheel(int amount, bool horizontal) =>
        Send([MouseEvent(horizontal ? MouseEventHWheel : MouseEventWheel, unchecked((uint)amount))]);

    public bool Chord(Shortcut shortcut)
    {
        var modifiers = ModifierCodes(shortcut.Modifiers);
        var inputs = new List<Input>();
        foreach (var code in modifiers) inputs.Add(Key(code, up: false));
        inputs.Add(Key(shortcut.VirtualKey, up: false));
        inputs.Add(Key(shortcut.VirtualKey, up: true));
        for (var i = modifiers.Count - 1; i >= 0; i--) inputs.Add(Key(modifiers[i], up: true));
        return Send(inputs);
    }

    public bool TypeUnits(string text)
    {
        var inputs = new List<Input>(text.Length * 2);
        foreach (var unit in text)
        {
            foreach (var flags in new[] { KeyEventUnicode, KeyEventUnicode | KeyEventKeyUp })
            {
                var input = new Input { type = InputKeyboard };
                input.u.ki.wScan = unit;
                input.u.ki.dwFlags = flags;
                inputs.Add(input);
            }
        }
        return Send(inputs);
    }

    private static List<ushort> ModifierCodes(KeyModifiers modifiers)
    {
        var codes = new List<ushort>();
        if (modifiers.HasFlag(KeyModifiers.Control)) codes.Add(Vk.Control);
        if (modifiers.HasFlag(KeyModifiers.Alt)) codes.Add(Vk.Menu);
        if (modifiers.HasFlag(KeyModifiers.Shift)) codes.Add(Vk.Shift);
        if (modifiers.HasFlag(KeyModifiers.Win)) codes.Add(Vk.LeftWin);
        return codes;
    }

    private static Input Key(ushort virtualKey, bool up)
    {
        var input = new Input { type = InputKeyboard };
        input.u.ki.wVk = virtualKey;
        input.u.ki.wScan = (ushort)MapVirtualKeyW(virtualKey, MapVkToVsc);
        input.u.ki.dwFlags = (up ? KeyEventKeyUp : 0) | (Shortcut.IsExtended(virtualKey) ? KeyEventExtendedKey : 0);
        return input;
    }

    private static Input MouseEvent(uint flags, uint data)
    {
        var input = new Input { type = InputMouse };
        input.u.mi.dwFlags = flags;
        input.u.mi.mouseData = data;
        return input;
    }

    /// <summary>Absolute mouse coordinates run 0..65535 across the virtual screen.</summary>
    private static int Normalize(int offset, int extent) =>
        (int)Math.Round(offset * (double)AbsoluteRange / Math.Max(1, extent - 1));

    private static bool Send(IReadOnlyList<Input> inputs)
    {
        var batch = inputs.ToArray();
        return SendInput((uint)batch.Length, batch, Marshal.SizeOf<Input>()) == batch.Length;
    }
}
