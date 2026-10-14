using System.Diagnostics;
using System.Windows.Automation;

namespace BlueyDesktop.Engine;

/// <summary>What the focused control is, as far as typing into it is concerned.</summary>
public enum FocusKind
{
    /// <summary>A control we could read, and it is not a password field.</summary>
    Ordinary,

    /// <summary>A password field. Nothing is typed into it.</summary>
    Secure,

    /// <summary>We couldn't tell: no element, an error, or the lookup ran out of time. Treated like a password field.</summary>
    Unknown,
}

/// <summary>
/// Asks UI Automation what has keyboard focus, and fails closed: anything short of a clear "this is an
/// ordinary control" means don't type.
/// </summary>
internal static class FocusGuard
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(750);

    private static readonly string[] PasswordNameHints = ["password", "passcode", "passwd"];

    internal static string? Refusal(FocusKind kind) => kind switch
    {
        FocusKind.Ordinary => null,
        FocusKind.Secure => "That's a password field. Ask the user to type it themselves.",
        _ => "I can't tell what's focused, and it could be a password field, so I won't type or press text keys. Click a text field first, or ask the user.",
    };

    internal static Task<FocusKind> CheckAsync(CancellationToken cancellationToken) =>
        CheckAsync(ReadFocusedElement, DefaultTimeout, cancellationToken);

    /// <summary>
    /// Runs the probe off the calling thread (UI Automation can block, and must not run on a UI thread) and
    /// gives up after `timeout`. A probe that throws, or doesn't answer in time, reads as Unknown. A probe
    /// that is still running when we give up is left to finish on its own.
    /// </summary>
    internal static async Task<FocusKind> CheckAsync(Func<FocusKind> probe, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var work = Task.Run(() =>
        {
            try
            {
                return probe();
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Bluey: focus check failed, treating as unknown: {ex.Message}");
                return FocusKind.Unknown;
            }
        }, CancellationToken.None);

        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var deadline = Task.Delay(timeout, timer.Token);
        var finished = await Task.WhenAny(work, deadline).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        timer.Cancel();
        if (finished == work) return await work.ConfigureAwait(false);
        Trace.TraceWarning($"Bluey: focus check took longer than {timeout.TotalMilliseconds:0} ms, treating as unknown");
        return FocusKind.Unknown;
    }

    private static FocusKind ReadFocusedElement() => Classify(AutomationElement.FocusedElement);

    internal static FocusKind Classify(AutomationElement? element)
    {
        if (element is null) return FocusKind.Unknown;

        // IsPassword is true for WPF PasswordBox, Win32 ES_PASSWORD edits and browser password inputs. Any other
        // answer (including "not supported") means we can't be sure, so it is not treated as ordinary.
        if (element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty) is not bool isPassword)
            return FocusKind.Unknown;
        if (isPassword) return FocusKind.Secure;

        // A field that calls itself a password field but doesn't report IsPassword (custom controls) is still off limits.
        var current = element.Current;
        return LooksLikePassword(current.Name) || LooksLikePassword(current.AutomationId)
            ? FocusKind.Secure
            : FocusKind.Ordinary;
    }

    private static bool LooksLikePassword(string? text) =>
        !string.IsNullOrEmpty(text)
        && PasswordNameHints.Any(hint => text.Contains(hint, StringComparison.OrdinalIgnoreCase));
}
