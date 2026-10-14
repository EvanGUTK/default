// Console checks for Windows/BlueyDesktop.App/Engine. Run: dotnet run --project Windows/BlueyDesktop.Engine.SelfTest
// The logic checks use a fake input driver, so they never touch your mouse or keyboard. The "real" checks at the
// end read the foreground window and focused control, register and press the Ctrl+Alt+S hotkey (the system eats
// that key press), and briefly show a small window with a password box to prove the UI Automation check.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using BlueyDesktop.Engine;

var passes = 0;
var failures = 0;

void Check(string name, bool ok, string detail = "")
{
    if (ok) passes++; else failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok || detail.Length == 0 ? "" : "   -> " + detail)}");
}

void Section(string title) => Console.WriteLine($"\n== {title}");

Shortcut P(string combo) =>
    Shortcut.TryParse(combo, out var shortcut, out var error) ? shortcut : throw new InvalidOperationException($"{combo}: {error}");

ForegroundWindow Notepad() => new("Notepad", "notepad", 4242);

string? PolicyFor(string combo, ForegroundWindow? foreground = null) =>
    ShortcutPolicy.Refusal(P(combo), () => foreground);

ComputerControl Make(FakeDriver driver, FocusKind focus = FocusKind.Ordinary, ForegroundWindow? foreground = null, int[]? focusCalls = null) =>
    new(driver,
        _ =>
        {
            if (focusCalls is not null) focusCalls[0]++;
            return Task.FromResult(focus);
        },
        () => foreground ?? Notepad());

// ---------------------------------------------------------------------------------------------------------
Section("parser: aliases and modifier order");
Check("ctrl+alt+s written four ways is one chord",
    P("ctrl+alt+s") == P("alt+ctrl+S") && P("control+option+s") == P("ctrl+alt+s") && P("⌃+⌥+s") == P("ctrl+alt+s"));
Check("label is normalised", P("shift+alt+ctrl+n").Label == "Ctrl+Alt+Shift+N", P("shift+alt+ctrl+n").Label);
Check("win aliases", P("win+l") == P("meta+l") && P("super+l") == P("windows+l"));
Check("'cmd' is rejected with a hint, not guessed", !Shortcut.TryParse("cmd+q", out _, out var cmdError) && cmdError.Contains("Cmd"), cmdError);
Check("two keys are rejected", !Shortcut.TryParse("ctrl+a+b", out _, out _));
Check("unknown key is rejected", !Shortcut.TryParse("banana", out _, out _));
Check("empty and null are rejected", !Shortcut.TryParse("", out _, out _) && !Shortcut.TryParse(null, out _, out _));

Section("denylist");
Check("win+l refused in any spelling", new[] { "win+l", "meta+l", "super+L", "ctrl+win+l", "Win + L" }.All(c => PolicyFor(c) is not null));
Check("shift+delete refused in any spelling", new[] { "shift+delete", "shift+del", "ctrl+shift+delete" }.All(c => PolicyFor(c) is not null));
Check("plain delete and ctrl+delete allowed", PolicyFor("delete") is null && PolicyFor("ctrl+delete") is null);
Check("alt+f4 refused when foreground is unknown (fail closed)", PolicyFor("alt+f4", null) is not null);
Check("alt+f4 refused on the desktop", PolicyFor("alt+f4", new ForegroundWindow("Progman", "explorer", 10)) is not null);
Check("alt+f4 refused on the taskbar", PolicyFor("option+f4", new ForegroundWindow("Shell_TrayWnd", "explorer", 10)) is not null);
Check("alt+f4 refused on a system process", PolicyFor("alt+f4", new ForegroundWindow("Whatever", "winlogon", 10)) is not null);
Check("alt+f4 refused on Bluey itself", PolicyFor("alt+f4", new ForegroundWindow("HwndWrapper", "BlueyDesktop.App", Environment.ProcessId)) is not null);
Check("alt+f4 with extra modifiers is still refused on the desktop", PolicyFor("ctrl+alt+f4", new ForegroundWindow("Progman", "explorer", 10)) is not null);
Check("alt+f4 allowed on an ordinary app", PolicyFor("alt+f4", Notepad()) is null);
Check("ctrl+alt+delete is not intercepted (OS-reserved; SendInput can't send it)", PolicyFor("ctrl+alt+delete") is null);
var foregroundCalls = 0;
ShortcutPolicy.Refusal(P("ctrl+c"), () => { foregroundCalls++; return null; });
Check("foreground is only looked up for alt+f4", foregroundCalls == 0);

Section("which chords insert text");
var inserts = new[] { "a", "enter", "space", "backspace", "ctrl+v", "ctrl+shift+v", "shift+insert", "ctrl+alt+q", "shift+a", "delete" };
var doesNot = new[] { "ctrl+c", "ctrl+a", "alt+tab", "win+d", "tab", "escape", "f5", "down", "home", "ctrl+t" };
Check("typing keys insert text", inserts.All(c => P(c).InsertsText), string.Join(",", inserts.Where(c => !P(c).InsertsText)));
Check("shortcuts and navigation do not", doesNot.All(c => !P(c).InsertsText), string.Join(",", doesNot.Where(c => P(c).InsertsText)));

Section("focus guard fails closed");
Check("ordinary probe -> Ordinary", await FocusGuard.CheckAsync(() => FocusKind.Ordinary, TimeSpan.FromSeconds(1), default) == FocusKind.Ordinary);
Check("secure probe -> Secure", await FocusGuard.CheckAsync(() => FocusKind.Secure, TimeSpan.FromSeconds(1), default) == FocusKind.Secure);
Check("probe that throws -> Unknown",
    await FocusGuard.CheckAsync(() => throw new InvalidOperationException("no element"), TimeSpan.FromSeconds(1), default) == FocusKind.Unknown);
var stopwatch = Stopwatch.StartNew();
var timedOut = await FocusGuard.CheckAsync(() => { Thread.Sleep(2500); return FocusKind.Ordinary; }, TimeSpan.FromMilliseconds(100), default);
stopwatch.Stop();
Check("probe that hangs -> Unknown, without waiting for it", timedOut == FocusKind.Unknown && stopwatch.ElapsedMilliseconds < 1500, $"{stopwatch.ElapsedMilliseconds} ms");
try
{
    using var cancel = new CancellationTokenSource(50);
    await FocusGuard.CheckAsync(() => { Thread.Sleep(1500); return FocusKind.Ordinary; }, TimeSpan.FromSeconds(5), cancel.Token);
    Check("cancelling the wait throws", false);
}
catch (OperationCanceledException)
{
    Check("cancelling the wait throws", true);
}
Check("only Ordinary lets typing through",
    FocusGuard.Refusal(FocusKind.Ordinary) is null && FocusGuard.Refusal(FocusKind.Secure) is not null && FocusGuard.Refusal(FocusKind.Unknown) is not null);

// ---------------------------------------------------------------------------------------------------------
Section("ComputerControl: off by default");
{
    var driver = new FakeDriver();
    var control = Make(driver);
    Check("starts disabled", !control.IsEnabled && !control.StoppedByUser);
    var click = await control.ClickAsync(10, 10);
    var type = await control.TypeTextAsync("hello");
    var keys = await control.PressKeysAsync("ctrl+t");
    var scroll = await control.ScrollAsync(120);
    Check("every action is refused and nothing is sent",
        !click.Succeeded && !type.Succeeded && !keys.Succeeded && !scroll.Succeeded && driver.Snapshot().Length == 0);
}

Section("ComputerControl: click and scroll");
{
    var driver = new FakeDriver();
    var control = Make(driver);
    control.Enable();
    var result = await control.ClickAsync(300, 200);
    Check("click = move, down, up, pointer restored",
        result.Succeeded && driver.Snapshot().SequenceEqual(["move 300,200", "Left down", "Left up", "cursor 100,100"]), string.Join(" | ", driver.Snapshot()));
    driver.Clear();
    var off = await control.ClickAsync(5000, 5000);
    Check("off-screen click is refused and nothing is sent", !off.Succeeded && driver.Snapshot().Length == 0);
    driver.Clear();
    await control.ClickAsync(10, 10, MouseButton.Right, 2);
    Check("double right-click sends two right presses", driver.Snapshot().Count(e => e == "Right down") == 2 && driver.Snapshot().Count(e => e == "Right up") == 2);

    driver.Clear();
    await control.ScrollAsync(240);
    Check("scroll down 240 = negative wheel steps summing to -240", driver.Wheels.Sum() == -240 && driver.Wheels.All(w => w < 0 && w >= -40), string.Join(",", driver.Wheels));
    driver.Clear();
    await control.ScrollAsync(-80);
    Check("scroll up goes the other way", driver.Wheels.Sum() == 80);
    driver.Clear();
    await control.ScrollAsync(80, horizontal: true);
    Check("scroll right is positive on the horizontal wheel", driver.Wheels.Sum() == 80 && driver.Snapshot().All(e => !e.StartsWith("wheel") || e.EndsWith("h")));
    driver.Clear();
    await control.ScrollAsync(99999);
    Check("scroll distance is capped", driver.Wheels.Sum() == -1200, driver.Wheels.Sum().ToString());
}

Section("ComputerControl: typing and the password guard");
{
    var driver = new FakeDriver();
    var control = Make(driver, FocusKind.Ordinary);
    control.Enable();
    var typed = await control.TypeTextAsync("hi\nthere\ttab");
    Check("types text; newline and tab become Enter and Tab",
        typed.Succeeded && driver.Typed.ToString() == "hithere" + "tab" && driver.Snapshot().Count(e => e.StartsWith("chord |0D")) == 1 && driver.Snapshot().Count(e => e.StartsWith("chord |09")) == 1,
        $"{driver.Typed} / {string.Join(" | ", driver.Snapshot())}");
    driver.Clear();
    await control.TypeTextAsync("a\r\nb");
    Check("CRLF is a single Enter", driver.Snapshot().Count(e => e.StartsWith("chord |0D")) == 1);
    driver.Clear();
    await control.TypeTextAsync("go", pressEnter: true);
    Check("pressEnter ends with Enter", driver.Snapshot().Last().StartsWith("chord |0D"));
    driver.Clear();
    var emoji = await control.TypeTextAsync("a😀b");
    Check("emoji (a surrogate pair) is sent in one piece", emoji.Succeeded && driver.Typed.ToString() == "a😀b");

    foreach (var focus in new[] { FocusKind.Secure, FocusKind.Unknown })
    {
        var blocked = new FakeDriver();
        var guarded = Make(blocked, focus);
        guarded.Enable();
        var r = await guarded.TypeTextAsync("hunter2");
        Check($"typing is refused when focus is {focus}", !r.Succeeded && blocked.Snapshot().Length == 0 && blocked.Typed.Length == 0, r.Message);
    }
    var secure = await Make(new FakeDriver(), FocusKind.Secure).TypeTextAsync("x");
    Check("a never-enabled control still refuses", !secure.Succeeded);
}

Section("ComputerControl: press keys");
{
    async Task<(ControlResult Result, FakeDriver Driver, int FocusLookups)> Press(string combo, FocusKind focus, ForegroundWindow? foreground = null)
    {
        var driver = new FakeDriver();
        var lookups = new int[1];
        var control = Make(driver, focus, foreground, lookups);
        control.Enable();
        return (await control.PressKeysAsync(combo), driver, lookups[0]);
    }

    var t = await Press("ctrl+t", FocusKind.Unknown);
    Check("ctrl+t works even when focus is unknown, and doesn't look it up", t.Result.Succeeded && t.FocusLookups == 0 && t.Driver.Snapshot().Length == 1);
    Check("escape and tab work in a password field", (await Press("escape", FocusKind.Secure)).Result.Succeeded && (await Press("tab", FocusKind.Secure)).Result.Succeeded);
    Check("a letter is refused in a password field", !(await Press("a", FocusKind.Secure)).Result.Succeeded);
    Check("a letter is refused when focus is unknown", !(await Press("a", FocusKind.Unknown)).Result.Succeeded);
    Check("enter is refused in a password field (it would submit)", !(await Press("enter", FocusKind.Secure)).Result.Succeeded);
    Check("ctrl+v (paste) is refused when focus is unknown", !(await Press("ctrl+v", FocusKind.Unknown)).Result.Succeeded);
    Check("ctrl+v works in an ordinary field", (await Press("ctrl+v", FocusKind.Ordinary)).Result.Succeeded);
    var win = await Press("super+L", FocusKind.Ordinary);
    Check("win+l is refused and not sent", !win.Result.Succeeded && win.Driver.Snapshot().Length == 0, win.Result.Message);
    var cmd = await Press("cmd+q", FocusKind.Ordinary);
    Check("cmd+q is refused with a hint", !cmd.Result.Succeeded && cmd.Result.Message.Contains("Cmd"), cmd.Result.Message);
    var f4 = await Press("alt+f4", FocusKind.Ordinary, new ForegroundWindow("Progman", "explorer", 1));
    Check("alt+f4 on the desktop is refused and not sent", !f4.Result.Succeeded && f4.Driver.Snapshot().Length == 0);
    var f4ok = await Press("alt+f4", FocusKind.Ordinary, Notepad());
    Check("alt+f4 on an ordinary app is sent", f4ok.Result.Succeeded && f4ok.Driver.Snapshot().Length == 1);
}

Section("ComputerControl: stop latches");
{
    var driver = new FakeDriver();
    var control = Make(driver);
    var changes = 0;
    control.StateChanged += () => Interlocked.Increment(ref changes);
    control.Enable();
    control.Stop();
    Check("stop switches control off and remembers why", !control.IsEnabled && control.StoppedByUser);
    var refused = await control.ClickAsync(10, 10);
    Check("actions are refused with the stop message", !refused.Succeeded && refused.Message.Contains("pressed stop"), refused.Message);
    Console.WriteLine("      (waiting 7 s: the Mac stop used to lapse after 6 s)");
    await Task.Delay(TimeSpan.FromSeconds(7));
    var later = await control.TypeTextAsync("still off?");
    Check("still off after 7 s", !later.Succeeded && !control.IsEnabled && driver.Snapshot().Length == 0);
    control.Enable();
    var back = await control.ClickAsync(10, 10);
    Check("Enable() is the only way back", back.Succeeded && control.IsEnabled && !control.StoppedByUser);
    Check("StateChanged fired for enable, stop, enable", changes == 3, changes.ToString());
}

Section("ComputerControl: stop cancels running and queued work");
{
    var driver = new FakeDriver();
    var control = Make(driver);
    control.Enable();
    var typing = control.TypeTextAsync(new string('x', 300));
    var queued = control.ClickAsync(50, 50);
    await Task.Delay(120);
    control.Stop();
    var typingResult = await typing;
    var queuedResult = await queued;
    var typedAtStop = driver.Typed.Length;
    await Task.Delay(250);
    Check("the running type stopped partway", !typingResult.Succeeded && typedAtStop > 0 && typedAtStop < 300, $"{typedAtStop} of 300");
    Check("the queued click never ran", !queuedResult.Succeeded && !driver.Snapshot().Any(e => e.StartsWith("move") || e.Contains("down")));
    Check("nothing more was typed after the stop", driver.Typed.Length == typedAtStop);
}
{
    var driver = new FakeDriver();
    var control = Make(driver);
    control.Enable();
    var scrolling = control.ScrollAsync(1200);
    await Task.Delay(60);
    control.Stop();
    var result = await scrolling;
    var wheelsAtStop = driver.Wheels.Count;
    await Task.Delay(120);
    Check("a running scroll stops", !result.Succeeded && wheelsAtStop > 0 && wheelsAtStop < 30 && driver.Wheels.Count == wheelsAtStop, $"{wheelsAtStop} steps");
}
{
    var driver = new FakeDriver();
    var control = Make(driver);
    control.Enable();
    var click = control.ClickAsync(10, 10);
    SpinWait.SpinUntil(() => driver.DownCount > 0, 2000);
    control.Stop();
    var releasedBy = driver.DownCount; // Stop() itself lets go, without waiting for the click's own 35 ms hold
    var result = await click;
    Check("stop releases a held mouse button right away", releasedBy == 0, $"{releasedBy} still down");
    Check("the interrupted click reads as stopped and the button ends up", !result.Succeeded && driver.DownCount == 0);
}

// ---------------------------------------------------------------------------------------------------------
Section("real: Win32 plumbing (harmless)");
{
    var expected = IntPtr.Size == 8 ? 40 : 28;
    Check("INPUT struct size matches Windows", Marshal.SizeOf<NativeMethods.Input>() == expected, $"{Marshal.SizeOf<NativeMethods.Input>()} vs {expected}");
    var real = new Win32InputDriver();
    var screen = real.VirtualScreen;
    Check("virtual screen has a size", screen.Width > 0 && screen.Height > 0, screen.ToString());
    var before = real.GetCursor();
    real.SetCursor(before.X, before.Y);
    Check("cursor read/write round-trips", real.GetCursor() == before);
    var foreground = ShortcutPolicy.ReadForeground();
    Console.WriteLine($"      foreground window: {(foreground is null ? "(unreadable)" : $"{foreground.ClassName} / {foreground.ProcessName} / pid {foreground.ProcessId}")}");
    Check("foreground lookup works or reports null without throwing", true);
    var focus = await FocusGuard.CheckAsync(CancellationToken.None);
    Console.WriteLine($"      UI Automation says the focused control is: {focus}");
    Check("UI Automation focus lookup answers within its timeout", Enum.IsDefined(focus));
}

Section("real: UI Automation sees a password box");
{
    var (passwordKind, textKind, landed, edits) = await ShowWindowAndProbeAsync();
    Check("UI Automation reads the PasswordBox as Secure and the TextBox as Ordinary",
        edits.Length == 2 && edits.Count(k => k == FocusKind.Secure) == 1 && edits.Count(k => k == FocusKind.Ordinary) == 1, string.Join(",", edits));
    if (!landed)
        Console.WriteLine("      SKIP: Windows would not give the test window keyboard focus (foreground lock), so the focus-driven variant is inconclusive");
    else
    {
        Check("a focused PasswordBox reads as Secure", passwordKind == FocusKind.Secure, passwordKind.ToString());
        Check("a focused TextBox reads as Ordinary", textKind == FocusKind.Ordinary, textKind.ToString());
    }
}

Section("real: Ctrl+Alt+S stop hotkey end to end");
{
    using var control = new ComputerControl();
    control.Enable();
    if (!control.TryInstallStopHotKey(out var error))
        Console.WriteLine($"      SKIP: couldn't register Ctrl+Alt+S (Win32 error {error}), probably another app owns it");
    else
    {
        Check("hotkey registered", true);
        var duplicate = GlobalHotKey.TryCreate(ComputerControl.StopHotKeyModifiers, ComputerControl.StopHotKeyCode, () => { }, out var duplicateError);
        Check("the same combination can't be registered twice (error 1409)", duplicate is null && duplicateError == 1409, $"error {duplicateError}");
        duplicate?.Dispose();

        new Win32InputDriver().Chord(P("ctrl+alt+s")); // the system consumes this press for our hotkey
        SpinWait.SpinUntil(() => !control.IsEnabled, 3000);
        Check("pressing Ctrl+Alt+S latches control off", !control.IsEnabled && control.StoppedByUser);
        var after = await control.PressKeysAsync("ctrl+c");
        Check("and actions are refused afterwards", !after.Succeeded);
    }
}
{
    var again = GlobalHotKey.TryCreate(ComputerControl.StopHotKeyModifiers, ComputerControl.StopHotKeyCode, () => { }, out var againError);
    Check("the combination is free again after Dispose", again is not null, $"error {againError}");
    again?.Dispose();
}

Console.WriteLine($"\n{passes} passed, {failures} failed");
return failures == 0 ? 0 : 1;

// ---------------------------------------------------------------------------------------------------------
static Task<(FocusKind Password, FocusKind Text, bool Landed, FocusKind[] Edits)> ShowWindowAndProbeAsync()
{
    var done = new TaskCompletionSource<(FocusKind, FocusKind, bool, FocusKind[])>();
    var thread = new Thread(() =>
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var password = new PasswordBox { Margin = new Thickness(8) };
        var text = new TextBox { Margin = new Thickness(8) };
        var panel = new StackPanel();
        panel.Children.Add(password);
        panel.Children.Add(text);
        var window = new Window
        {
            Title = "Bluey self-test (closes by itself)",
            Width = 320,
            Height = 130,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = panel,
        };
        window.Show();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400);
                // Needs no keyboard focus: classify every edit control the window exposes through UI Automation.
                var handle = await dispatcher.InvokeAsync(() => new WindowInteropHelper(window).Handle);
                var edits = AutomationElement.FromHandle(handle)
                    .FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit))
                    .Cast<AutomationElement>()
                    .Select(FocusGuard.Classify)
                    .ToArray();
                await dispatcher.InvokeAsync(() =>
                {
                    window.Activate();
                    password.Focus();
                });
                await Task.Delay(400);
                var landed = ShortcutPolicy.ReadForeground()?.ProcessId == Environment.ProcessId;
                var passwordKind = await FocusGuard.CheckAsync(CancellationToken.None);
                await dispatcher.InvokeAsync(() => text.Focus());
                await Task.Delay(400);
                var textKind = await FocusGuard.CheckAsync(CancellationToken.None);
                done.SetResult((passwordKind, textKind, landed, edits));
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
            finally
            {
                dispatcher.InvokeShutdown();
            }
        });
        Dispatcher.Run();
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    return done.Task;
}

/// <summary>Records what the engine asks of the input layer, without sending any of it.</summary>
sealed class FakeDriver : IInputDriver
{
    private readonly object _gate = new();
    private readonly List<string> _log = [];
    private readonly HashSet<MouseButton> _down = [];

    public StringBuilder Typed { get; } = new();

    public List<int> Wheels { get; } = [];

    public ScreenBounds VirtualScreen { get; } = new(0, 0, 1920, 1080);

    public int DownCount
    {
        get { lock (_gate) return _down.Count; }
    }

    public string[] Snapshot()
    {
        lock (_gate) return [.. _log];
    }

    public void Clear()
    {
        lock (_gate)
        {
            _log.Clear();
            Typed.Clear();
            Wheels.Clear();
        }
    }

    private void Add(string entry)
    {
        lock (_gate) _log.Add(entry);
    }

    public (int X, int Y) GetCursor() => (100, 100);

    public void SetCursor(int x, int y) => Add($"cursor {x},{y}");

    public bool MoveMouse(int x, int y)
    {
        Add($"move {x},{y}");
        return true;
    }

    public bool SetMouseButton(MouseButton button, bool down)
    {
        lock (_gate)
        {
            if (down) _down.Add(button);
            else _down.Remove(button);
            _log.Add($"{button} {(down ? "down" : "up")}");
        }
        return true;
    }

    public bool Wheel(int amount, bool horizontal)
    {
        lock (_gate)
        {
            Wheels.Add(amount);
            _log.Add($"wheel {amount}{(horizontal ? "h" : "")}");
        }
        return true;
    }

    public bool Chord(Shortcut shortcut)
    {
        Add($"chord {shortcut.Label}|{shortcut.VirtualKey:X2}");
        return true;
    }

    public bool TypeUnits(string text)
    {
        lock (_gate)
        {
            Typed.Append(text);
            _log.Add("type");
        }
        return true;
    }
}
