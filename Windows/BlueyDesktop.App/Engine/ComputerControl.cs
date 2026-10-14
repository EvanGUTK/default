using System.Text;

namespace BlueyDesktop.Engine;

/// <summary>What an action did, in words he can pass on. `Succeeded` is false for refusals and failures alike.</summary>
public readonly record struct ControlResult(bool Succeeded, string Message)
{
    public static ControlResult Ok(string message) => new(true, message);

    public static ControlResult Refused(string message) => new(false, message);
}

/// <summary>
/// Hands on the keyboard and mouse, used only when the user asks him to do something. Positions are screen
/// pixels in the process's DPI space (the same space as the cursor position).
/// </summary>
/// <remarks>
/// Safety rules, all enforced here so no caller can skip them:
/// <list type="bullet">
/// <item>Control starts OFF. Nothing happens until <see cref="Enable"/> is called.</item>
/// <item><see cref="Stop"/> (also the Ctrl+Alt+S hotkey) latches control off for good: it cancels the running
/// action and every waiting one, lets go of any held mouse button, and stays off until <see cref="Enable"/>.</item>
/// <item>Chords on the denylist are never pressed (see <see cref="ShortcutPolicy"/>).</item>
/// <item>Typing, and keys that type, are refused unless UI Automation says the focused control is not a
/// password field. If it can't say, they are refused too.</item>
/// </list>
/// Actions run one at a time, in the order they were asked for. The latch is in memory only: persist it in
/// the settings layer if it should survive a relaunch.
/// </remarks>
public sealed class ComputerControl : IDisposable
{
    /// <summary>The stop hotkey: Ctrl+Alt+S.</summary>
    public const KeyModifiers StopHotKeyModifiers = KeyModifiers.Control | KeyModifiers.Alt;

    public const ushort StopHotKeyCode = Vk.S;

    private const string StoppedMessage =
        "The user pressed stop, which switched computer control off. Don't use the computer again until they switch it back on. You can still look and point.";

    private const string OffMessage = "Computer control is switched off. You can still look and point.";

    private const string BlockedMessage =
        "Windows blocked the input. The window may be running as administrator, or a secure prompt may be showing.";

    private const int MaxClickCount = 3;
    private const int MaxScroll = 1200;
    private const int ScrollStep = 40;
    private const int LongTextLength = 160;
    private const int LongTextChunk = 6;
    private static readonly TimeSpan ScrollPause = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan MoveSettle = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan ClickHold = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan MultiClickGap = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan SubmitPause = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan ShortTypePause = TimeSpan.FromMilliseconds(22);
    private static readonly TimeSpan LongTypePause = TimeSpan.FromMilliseconds(12);

    private readonly IInputDriver _driver;
    private readonly Func<CancellationToken, Task<FocusKind>> _focusCheck;
    private readonly Func<ForegroundWindow?> _foreground;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<MouseButton> _held = [];
    private readonly object _lock = new();
    private CancellationTokenSource _epoch = new();
    private bool _enabled;
    private bool _stoppedByUser;
    private GlobalHotKey? _stopHotKey;

    public ComputerControl()
        : this(new Win32InputDriver(), FocusGuard.CheckAsync, ShortcutPolicy.ReadForeground)
    {
    }

    internal ComputerControl(
        IInputDriver driver,
        Func<CancellationToken, Task<FocusKind>> focusCheck,
        Func<ForegroundWindow?> foreground)
    {
        _driver = driver;
        _focusCheck = focusCheck;
        _foreground = foreground;
    }

    /// <summary>Raised (on whatever thread changed it) when control is switched on or off.</summary>
    public event Action? StateChanged;

    public bool IsEnabled
    {
        get { lock (_lock) return _enabled; }
    }

    /// <summary>True when control is off because the user pressed stop, not because it was never switched on.</summary>
    public bool StoppedByUser
    {
        get { lock (_lock) return _stoppedByUser; }
    }

    /// <summary>Switches control on. This is the only way back after <see cref="Stop"/>.</summary>
    public void Enable()
    {
        lock (_lock)
        {
            if (_enabled) return;
            _enabled = true;
            _stoppedByUser = false;
            // A new epoch: actions from before the last stop keep their cancelled token and stay cancelled.
            _epoch = new CancellationTokenSource();
        }
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Latches control off right now: cancels the running action and the waiting ones, releases any held
    /// mouse button, and refuses everything until <see cref="Enable"/>. Safe to call from any thread.
    /// </summary>
    public void Stop()
    {
        CancellationTokenSource epoch;
        lock (_lock)
        {
            _enabled = false;
            _stoppedByUser = true;
            epoch = _epoch;
        }
        epoch.Cancel(); // outside the lock: cancelling runs the waiting actions' continuations
        ReleaseButtons();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Registers Ctrl+Alt+S system-wide to call <see cref="Stop"/>. Returns false, with the Win32 error
    /// code, if another app already owns the combination, so the caller can tell the user.
    /// </summary>
    public bool TryInstallStopHotKey(out int win32Error)
    {
        if (_stopHotKey is not null)
        {
            win32Error = 0;
            return true;
        }
        _stopHotKey = GlobalHotKey.TryCreate(StopHotKeyModifiers, StopHotKeyCode, Stop, out win32Error);
        return _stopHotKey is not null;
    }

    public void Dispose()
    {
        _stopHotKey?.Dispose();
        _stopHotKey = null;
        CancellationTokenSource epoch;
        lock (_lock)
        {
            _enabled = false;
            epoch = _epoch;
        }
        epoch.Cancel();
        ReleaseButtons();
    }

    // MARK: Mouse

    /// <summary>Moves to a screen position and clicks. The real pointer is put back afterwards.</summary>
    public Task<ControlResult> ClickAsync(int x, int y, MouseButton button = MouseButton.Left, int count = 1) =>
        RunAsync(async token =>
        {
            if (!_driver.VirtualScreen.Contains(x, y)) return ControlResult.Refused("That spot is off the screen.");
            var clicks = Math.Clamp(count, 1, MaxClickCount);
            var saved = _driver.GetCursor();
            try
            {
                if (!_driver.MoveMouse(x, y)) return ControlResult.Refused(BlockedMessage);
                await Task.Delay(MoveSettle, token);
                for (var i = 0; i < clicks; i++)
                {
                    token.ThrowIfCancellationRequested();
                    if (!SetButton(button, down: true)) return ControlResult.Refused(BlockedMessage);
                    // Once the button is down it always gets its release, so this wait ignores a stop.
                    await Task.Delay(ClickHold, CancellationToken.None);
                    SetButton(button, down: false);
                    if (i < clicks - 1) await Task.Delay(MultiClickGap, token);
                }
                token.ThrowIfCancellationRequested(); // a stop that landed mid-click still reads as stopped
                return ControlResult.Ok(clicks == 2 ? "Double-clicked." : button == MouseButton.Right ? "Right-clicked." : "Clicked.");
            }
            finally
            {
                ReleaseButtons();
                _driver.SetCursor(saved.X, saved.Y);
            }
        });

    /// <summary>
    /// Scrolls smoothly. Positive `delta` scrolls down (or right when `horizontal`); 120 is one wheel notch.
    /// With `x` and `y` the pointer goes there first (and is put back afterwards).
    /// </summary>
    public Task<ControlResult> ScrollAsync(int delta, bool horizontal = false, int? x = null, int? y = null) =>
        RunAsync(async token =>
        {
            if (delta == 0) return ControlResult.Refused("Nothing to scroll.");
            var moves = x is not null && y is not null;
            if (moves && !_driver.VirtualScreen.Contains(x!.Value, y!.Value))
                return ControlResult.Refused("That spot is off the screen.");
            var saved = _driver.GetCursor();
            try
            {
                if (moves)
                {
                    if (!_driver.MoveMouse(x!.Value, y!.Value)) return ControlResult.Refused(BlockedMessage);
                    await Task.Delay(MoveSettle, token);
                }
                var remaining = Math.Min(Math.Abs(delta), MaxScroll);
                // The wheel reports positive as "away from the user" (up, or right), so down is negative.
                var direction = (delta > 0) == horizontal ? 1 : -1;
                while (remaining > 0)
                {
                    token.ThrowIfCancellationRequested();
                    var step = Math.Min(remaining, ScrollStep);
                    if (!_driver.Wheel(direction * step, horizontal)) return ControlResult.Refused(BlockedMessage);
                    remaining -= step;
                    await Task.Delay(ScrollPause, token);
                }
                return ControlResult.Ok("Scrolled.");
            }
            finally
            {
                if (moves) _driver.SetCursor(saved.X, saved.Y);
            }
        });

    // MARK: Keyboard

    /// <summary>
    /// Types text into whatever is focused, as if on a keyboard. A newline presses Enter and a tab presses
    /// Tab. Refused for a password field, and for anything the focus check can't identify.
    /// </summary>
    public Task<ControlResult> TypeTextAsync(string text, bool pressEnter = false) =>
        RunAsync(async token =>
        {
            if (string.IsNullOrEmpty(text)) return ControlResult.Refused("Nothing to type.");
            if (FocusGuard.Refusal(await _focusCheck(token)) is { } refusal) return ControlResult.Refused(refusal);

            // Short text types at a natural pace; long text goes in faster, a few characters at a time.
            var isLong = text.Length > LongTextLength;
            var chunkSize = isLong ? LongTextChunk : 1;
            var pause = isLong ? LongTypePause : ShortTypePause;
            var runes = text.EnumerateRunes().ToList();
            var chunk = new StringBuilder();
            var chunkRunes = 0;

            async Task<bool> FlushAsync()
            {
                if (chunk.Length == 0) return true;
                var sent = _driver.TypeUnits(chunk.ToString());
                chunk.Clear();
                chunkRunes = 0;
                if (sent) await Task.Delay(pause, token);
                return sent;
            }

            for (var i = 0; i < runes.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var rune = runes[i];
                if (rune.IsAscii && (char)rune.Value is '\r' or '\n' or '\t')
                {
                    if (!await FlushAsync()) return ControlResult.Refused(BlockedMessage);
                    var isCrLf = rune.Value == '\r' && i + 1 < runes.Count && runes[i + 1].Value == '\n';
                    if (isCrLf) continue; // the "\n" that follows presses Enter
                    var key = rune.Value == '\t' ? Vk.Tab : Vk.Return;
                    if (!_driver.Chord(new Shortcut(KeyModifiers.None, key, ""))) return ControlResult.Refused(BlockedMessage);
                    await Task.Delay(pause, token);
                    continue;
                }
                chunk.Append(rune.ToString());
                chunkRunes++;
                if (chunkRunes >= chunkSize && !await FlushAsync()) return ControlResult.Refused(BlockedMessage);
            }
            if (!await FlushAsync()) return ControlResult.Refused(BlockedMessage);

            if (pressEnter)
            {
                await Task.Delay(SubmitPause, token);
                if (!_driver.Chord(new Shortcut(KeyModifiers.None, Vk.Return, ""))) return ControlResult.Refused(BlockedMessage);
                return ControlResult.Ok("Typed it and pressed Enter.");
            }
            return ControlResult.Ok("Typed it.");
        });

    /// <summary>Presses a key or shortcut like "ctrl+t", "enter", "ctrl+shift+n" or "down".</summary>
    public Task<ControlResult> PressKeysAsync(string combo) =>
        RunAsync(async token =>
        {
            if (!Shortcut.TryParse(combo, out var shortcut, out var error)) return ControlResult.Refused(error);
            if (ShortcutPolicy.Refusal(shortcut, _foreground) is { } denied) return ControlResult.Refused(denied);
            // Keys that type are held to the same password-field rule as TypeTextAsync.
            if (shortcut.InsertsText && FocusGuard.Refusal(await _focusCheck(token)) is { } refusal)
                return ControlResult.Refused(refusal);
            token.ThrowIfCancellationRequested();
            return _driver.Chord(shortcut) ? ControlResult.Ok($"Pressed {shortcut.Label}.") : ControlResult.Refused(BlockedMessage);
        });

    // MARK: Running actions

    /// <summary>
    /// Runs one action when it's its turn. It is refused up front if control is off, and it ends as "stopped"
    /// if a stop lands while it waits or runs.
    /// </summary>
    private async Task<ControlResult> RunAsync(Func<CancellationToken, Task<ControlResult>> action)
    {
        CancellationToken token;
        lock (_lock)
        {
            if (!_enabled) return ControlResult.Refused(_stoppedByUser ? StoppedMessage : OffMessage);
            token = _epoch.Token;
        }

        try
        {
            await _gate.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            return ControlResult.Refused(StoppedMessage);
        }

        try
        {
            token.ThrowIfCancellationRequested();
            return await action(token);
        }
        catch (OperationCanceledException)
        {
            return ControlResult.Refused(StoppedMessage);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool SetButton(MouseButton button, bool down)
    {
        // Note a press as held before sending it, so a stop that lands in between can't miss it.
        if (down) lock (_lock) _held.Add(button);
        if (!_driver.SetMouseButton(button, down))
        {
            if (down) lock (_lock) _held.Remove(button);
            return false;
        }
        if (!down) lock (_lock) _held.Remove(button);
        return true;
    }

    /// <summary>Lets go of every mouse button we are holding. Safe to call twice, or from another thread.</summary>
    private void ReleaseButtons()
    {
        MouseButton[] held;
        lock (_lock)
        {
            held = [.. _held];
            _held.Clear();
        }
        foreach (var button in held) _driver.SetMouseButton(button, down: false);
    }
}
