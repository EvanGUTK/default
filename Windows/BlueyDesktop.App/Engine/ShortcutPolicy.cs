using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace BlueyDesktop.Engine;

/// <summary>The window that has keyboard focus, as far as the denylist cares.</summary>
public sealed record ForegroundWindow(string ClassName, string ProcessName, int ProcessId);

/// <summary>
/// Chords he never presses. Rules compare parsed modifier flags and a virtual-key code, and a chord is
/// refused when it holds the key plus at least the listed modifiers, so extra modifiers can't dodge a rule.
/// </summary>
/// <remarks>
/// Ctrl+Alt+Delete is not on the list on purpose: it is the Windows secure attention sequence, which the OS
/// reserves for the real keyboard driver, so SendInput cannot produce it and there is nothing to intercept.
/// Quit/log-out/shutdown through menu clicks is not covered by a keyboard denylist either.
/// </remarks>
public static class ShortcutPolicy
{
    private sealed record Rule(KeyModifiers Required, ushort Key, string Reason);

    private static readonly Rule[] AlwaysRefused =
    [
        new(KeyModifiers.Win, Vk.L, "it locks the PC"),
        new(KeyModifiers.Shift, Vk.Delete, "it deletes files for good, skipping the Recycle Bin"),
    ];

    /// <summary>
    /// Alt+F4 closes a window, which is fine for an ordinary app. On the desktop it opens the Shut Down
    /// dialog, and on system or shell windows (or on Bluey itself) it can end the session or the app.
    /// </summary>
    private static readonly HashSet<string> ProtectedWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "winlogon", "csrss", "lsass", "wininit", "services", "smss", "dwm", "logonui", "fontdrvhost",
        "sihost", "searchhost", "shellexperiencehost", "startmenuexperiencehost",
    };

    /// <summary>
    /// Why the chord must not be pressed, in words he can pass on, or null if it's allowed. `foreground` is
    /// only asked for Alt+F4, and if it can't say what has focus the chord is refused (fail closed).
    /// </summary>
    public static string? Refusal(Shortcut shortcut, Func<ForegroundWindow?> foreground)
    {
        foreach (var rule in AlwaysRefused)
        {
            if (shortcut.VirtualKey == rule.Key && shortcut.Modifiers.HasFlag(rule.Required))
                return Refuse(shortcut, rule.Reason);
        }
        if (shortcut.VirtualKey == Vk.F4 && shortcut.Modifiers.HasFlag(KeyModifiers.Alt))
        {
            var window = foreground();
            if (window is null) return Refuse(shortcut, "I can't tell which window has focus, and it could be a system window");
            if (IsProtected(window)) return Refuse(shortcut, "it would close a system window, the desktop or Bluey itself");
        }
        return null;
    }

    private static bool IsProtected(ForegroundWindow window) =>
        window.ProcessId == Environment.ProcessId
        || ProtectedWindowClasses.Contains(window.ClassName)
        || ProtectedProcesses.Contains(window.ProcessName);

    private static string Refuse(Shortcut shortcut, string reason) =>
        $"I won't press {shortcut.Label} ({reason}). Ask the user to do it.";

    /// <summary>The real foreground window, or null if any part of the lookup fails.</summary>
    internal static ForegroundWindow? ReadForeground()
    {
        try
        {
            var window = NativeMethods.GetForegroundWindow();
            if (window == IntPtr.Zero) return null;
            var className = new StringBuilder(256);
            if (NativeMethods.GetClassName(window, className, className.Capacity) == 0) return null;
            if (NativeMethods.GetWindowThreadProcessId(window, out var processId) == 0) return null;
            using var process = Process.GetProcessById((int)processId);
            return new ForegroundWindow(className.ToString(), process.ProcessName, (int)processId);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            Trace.TraceWarning($"Bluey: couldn't read the foreground window: {ex.Message}");
            return null;
        }
    }
}
