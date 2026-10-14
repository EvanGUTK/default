namespace BlueyDesktop.Engine;

/// <summary>Modifier keys of a chord. The values match the Win32 MOD_* flags used by RegisterHotKey.</summary>
[Flags]
public enum KeyModifiers : uint
{
    None = 0,
    Alt = NativeMethods.ModAlt,
    Control = NativeMethods.ModControl,
    Shift = NativeMethods.ModShift,
    Win = NativeMethods.ModWin,
}

/// <summary>Windows virtual-key codes the engine refers to by name.</summary>
internal static class Vk
{
    internal const ushort Back = 0x08;
    internal const ushort Tab = 0x09;
    internal const ushort Return = 0x0D;
    internal const ushort Shift = 0x10;
    internal const ushort Control = 0x11;
    internal const ushort Menu = 0x12; // the Alt key
    internal const ushort Escape = 0x1B;
    internal const ushort Space = 0x20;
    internal const ushort PageUp = 0x21;
    internal const ushort PageDown = 0x22;
    internal const ushort End = 0x23;
    internal const ushort Home = 0x24;
    internal const ushort Left = 0x25;
    internal const ushort Up = 0x26;
    internal const ushort Right = 0x27;
    internal const ushort Down = 0x28;
    internal const ushort Insert = 0x2D;
    internal const ushort Delete = 0x2E;
    internal const ushort L = 0x4C;
    internal const ushort S = 0x53;
    internal const ushort V = 0x56;
    internal const ushort LeftWin = 0x5B;
    internal const ushort F1 = 0x70;
    internal const ushort F4 = 0x73;
    internal const ushort F12 = 0x7B;
}

/// <summary>
/// A parsed key chord: modifier flags plus one key, whatever aliases or order it was written in.
/// The safety checks compare these parsed values, never the text.
/// </summary>
public readonly record struct Shortcut(KeyModifiers Modifiers, ushort VirtualKey, string Label)
{
    private static readonly HashSet<ushort> NavigationKeys = BuildNavigationKeys();
    private static readonly Dictionary<string, (ushort VirtualKey, string Label)> KeysByName = BuildKeysByName();

    /// <summary>
    /// True when the chord would put characters into the focused field, or submit it: a paste, an AltGr
    /// character (Ctrl+Alt on many layouts), or any key that isn't a Ctrl/Alt/Win shortcut or a navigation key.
    /// </summary>
    public bool InsertsText
    {
        get
        {
            var ctrl = Modifiers.HasFlag(KeyModifiers.Control);
            var alt = Modifiers.HasFlag(KeyModifiers.Alt);
            var win = Modifiers.HasFlag(KeyModifiers.Win);
            var shift = Modifiers.HasFlag(KeyModifiers.Shift);
            if (ctrl && VirtualKey == Vk.V) return true; // Ctrl+V and Ctrl+Shift+V
            if (shift && VirtualKey == Vk.Insert) return true; // Shift+Insert pastes
            if (ctrl && alt && !win) return true; // AltGr
            if (ctrl || alt || win) return false;
            return !NavigationKeys.Contains(VirtualKey);
        }
    }

    /// <summary>
    /// Reads a chord like "ctrl+t", "enter", "ctrl+shift+n" or "down". Exactly one non-modifier key is allowed.
    /// There is no Cmd key on Windows, so "cmd" is rejected with a hint rather than guessed at.
    /// </summary>
    public static bool TryParse(string? combo, out Shortcut shortcut, out string error)
    {
        shortcut = default;
        error = "";
        var parts = (combo ?? "").ToLowerInvariant().Replace(" ", "").Split('+', StringSplitOptions.RemoveEmptyEntries);
        var modifiers = KeyModifiers.None;
        ushort? key = null;
        var keyLabel = "";
        foreach (var part in parts)
        {
            switch (part)
            {
                case "ctrl" or "control" or "⌃":
                    modifiers |= KeyModifiers.Control;
                    break;
                case "alt" or "option" or "opt" or "⌥":
                    modifiers |= KeyModifiers.Alt;
                    break;
                case "shift" or "⇧":
                    modifiers |= KeyModifiers.Shift;
                    break;
                case "win" or "windows" or "meta" or "super":
                    modifiers |= KeyModifiers.Win;
                    break;
                case "cmd" or "command" or "⌘":
                    error = "Windows has no Cmd key. Use ctrl for shortcuts like copy and paste.";
                    return false;
                default:
                    if (!KeysByName.TryGetValue(part, out var known))
                    {
                        error = $"I don't know the key \"{part}\".";
                        return false;
                    }
                    if (key is not null)
                    {
                        error = "Press one key at a time, with its modifiers, like ctrl+shift+n.";
                        return false;
                    }
                    key = known.VirtualKey;
                    keyLabel = known.Label;
                    break;
            }
        }
        if (key is null)
        {
            error = $"I don't know the key \"{combo}\".";
            return false;
        }
        shortcut = new Shortcut(modifiers, key.Value, LabelFor(modifiers) + keyLabel);
        return true;
    }

    /// <summary>True for keys that need the extended-key flag so Windows doesn't read them as numpad keys.</summary>
    internal static bool IsExtended(ushort virtualKey) =>
        virtualKey is Vk.Insert or Vk.Delete or Vk.Home or Vk.End or Vk.PageUp or Vk.PageDown
            or Vk.Left or Vk.Up or Vk.Right or Vk.Down;

    private static string LabelFor(KeyModifiers modifiers)
    {
        var label = "";
        if (modifiers.HasFlag(KeyModifiers.Control)) label += "Ctrl+";
        if (modifiers.HasFlag(KeyModifiers.Alt)) label += "Alt+";
        if (modifiers.HasFlag(KeyModifiers.Shift)) label += "Shift+";
        if (modifiers.HasFlag(KeyModifiers.Win)) label += "Win+";
        return label;
    }

    /// <summary>Keys that move around or dismiss things without typing anything.</summary>
    private static HashSet<ushort> BuildNavigationKeys()
    {
        var keys = new HashSet<ushort>
        {
            Vk.Escape, Vk.Tab, Vk.Left, Vk.Right, Vk.Up, Vk.Down, Vk.Home, Vk.End, Vk.PageUp, Vk.PageDown,
        };
        for (var code = Vk.F1; code <= Vk.F12; code++) keys.Add(code);
        return keys;
    }

    private static Dictionary<string, (ushort VirtualKey, string Label)> BuildKeysByName()
    {
        var map = new Dictionary<string, (ushort VirtualKey, string Label)>(StringComparer.Ordinal);
        for (var letter = 'a'; letter <= 'z'; letter++)
            map[letter.ToString()] = ((ushort)(0x41 + (letter - 'a')), char.ToUpperInvariant(letter).ToString());
        for (var digit = '0'; digit <= '9'; digit++)
            map[digit.ToString()] = ((ushort)(0x30 + (digit - '0')), digit.ToString());
        for (var number = 1; number <= 12; number++)
            map[$"f{number}"] = ((ushort)(Vk.F1 + number - 1), $"F{number}");

        void Add(ushort code, string label, params string[] names)
        {
            foreach (var name in names) map[name] = (code, label);
        }

        Add(Vk.Return, "Enter", "return", "enter");
        Add(Vk.Tab, "Tab", "tab");
        Add(Vk.Space, "Space", "space", "spacebar");
        Add(Vk.Back, "Backspace", "backspace", "back");
        Add(Vk.Delete, "Delete", "delete", "del", "forwarddelete");
        Add(Vk.Insert, "Insert", "insert", "ins");
        Add(Vk.Escape, "Esc", "escape", "esc");
        Add(Vk.Left, "Left", "left");
        Add(Vk.Right, "Right", "right");
        Add(Vk.Up, "Up", "up");
        Add(Vk.Down, "Down", "down");
        Add(Vk.Home, "Home", "home");
        Add(Vk.End, "End", "end");
        Add(Vk.PageUp, "PageUp", "pageup", "pgup");
        Add(Vk.PageDown, "PageDown", "pagedown", "pgdn");
        Add(0xBD, "-", "-", "minus");
        Add(0xBB, "=", "=", "equal", "plus");
        Add(0xDB, "[", "[");
        Add(0xDD, "]", "]");
        Add(0xBA, ";", ";");
        Add(0xDE, "'", "'");
        Add(0xBC, ",", ",", "comma");
        Add(0xBE, ".", ".", "period");
        Add(0xBF, "/", "/", "slash");
        Add(0xDC, "\\", "\\");
        Add(0xC0, "`", "`");
        return map;
    }
}
