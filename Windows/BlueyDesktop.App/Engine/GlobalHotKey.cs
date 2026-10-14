using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using static BlueyDesktop.Engine.NativeMethods;

namespace BlueyDesktop.Engine;

/// <summary>
/// A system-wide hotkey through RegisterHotKey. It owns a small message-only window on its own thread, so it
/// works without any app window and keeps working while the UI thread is busy. `onPressed` runs on that
/// thread, so it must be thread-safe and quick.
/// </summary>
public sealed class GlobalHotKey : IDisposable
{
    private const int ErrorGenFailure = 31;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);
    private static int _nextId = 0x4259;

    private readonly Action _onPressed;
    private readonly int _id = Interlocked.Increment(ref _nextId);
    private Dispatcher? _dispatcher;
    private HwndSource? _window;
    private Thread? _thread;
    private volatile bool _loopRunning;
    private int _disposed;

    private GlobalHotKey(Action onPressed) => _onPressed = onPressed;

    /// <summary>
    /// Registers the hotkey. Returns null, with the Win32 error code, when it can't: usually error 1409
    /// (ERROR_HOTKEY_ALREADY_REGISTERED), because another app owns that combination.
    /// </summary>
    public static GlobalHotKey? TryCreate(KeyModifiers modifiers, ushort virtualKey, Action onPressed, out int win32Error)
    {
        var hotKey = new GlobalHotKey(onPressed);
        using var ready = new ManualResetEventSlim();
        var error = 0;

        var thread = new Thread(() =>
        {
            try
            {
                error = hotKey.Open(modifiers, virtualKey);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Bluey: couldn't start the hotkey window: {ex}");
                error = ErrorGenFailure;
            }
            hotKey._loopRunning = error == 0;
            ready.Set();
            if (error == 0) Dispatcher.Run();
        })
        {
            Name = "Bluey hotkey",
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        hotKey._thread = thread;
        thread.Start();
        ready.Wait();

        win32Error = error;
        if (error == 0) return hotKey;
        hotKey.Dispose(); // the thread has already ended; this just joins it
        return null;
    }

    /// <summary>Runs on the hotkey thread: makes the message-only window and registers the key.</summary>
    private int Open(KeyModifiers modifiers, ushort virtualKey)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        var parameters = new HwndSourceParameters("BlueyHotKeyWindow")
        {
            ParentWindow = new IntPtr(HwndMessage),
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        };
        _window = new HwndSource(parameters);
        _window.AddHook(WindowProc);
        if (RegisterHotKey(_window.Handle, _id, (uint)modifiers | ModNoRepeat, virtualKey)) return 0;

        var error = Marshal.GetLastWin32Error();
        _window.RemoveHook(WindowProc);
        _window.Dispose();
        _window = null;
        return error == 0 ? ErrorGenFailure : error;
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotKey && wParam.ToInt32() == _id)
        {
            handled = true;
            try
            {
                _onPressed();
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Bluey: the hotkey handler threw: {ex}");
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>Unregisters the hotkey and stops its thread. Don't call it from inside `onPressed`.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        if (_loopRunning && _dispatcher is { HasShutdownStarted: false } dispatcher)
        {
            dispatcher.Invoke(() =>
            {
                if (_window is null) return;
                UnregisterHotKey(_window.Handle, _id);
                _window.RemoveHook(WindowProc);
                _window.Dispose();
                _window = null;
            });
            dispatcher.InvokeShutdown();
        }
        _thread?.Join(StopTimeout);
    }
}
