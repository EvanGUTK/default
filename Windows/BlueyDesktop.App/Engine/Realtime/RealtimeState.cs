using System.Text.Json;

namespace BlueyDesktop.Engine.Realtime;

/// <summary>Where the Realtime connection is.</summary>
public enum RealtimeState
{
    /// <summary>No connection, and none was lost to an error.</summary>
    Disconnected,

    /// <summary>The socket is being opened.</summary>
    Connecting,

    /// <summary>The socket is open and messages flow both ways.</summary>
    Connected,

    /// <summary>The client asked to close and is waiting for the server's close frame.</summary>
    Disconnecting,

    /// <summary>The connection failed (couldn't connect, dropped, or the server closed with an error).
    /// A new <c>ConnectAsync</c> is allowed from here.</summary>
    Faulted,
}

/// <summary>The legal moves between <see cref="RealtimeState"/> values. Everything else is a bug.</summary>
public static class RealtimeStateMachine
{
    public static bool CanMove(RealtimeState from, RealtimeState to) => (from, to) switch
    {
        (RealtimeState.Disconnected or RealtimeState.Faulted, RealtimeState.Connecting) => true,
        (RealtimeState.Connecting, RealtimeState.Connected or RealtimeState.Disconnected or RealtimeState.Faulted) => true,
        (RealtimeState.Connected, RealtimeState.Disconnecting or RealtimeState.Disconnected or RealtimeState.Faulted) => true,
        (RealtimeState.Disconnecting, RealtimeState.Disconnected) => true,
        _ => false,
    };
}

/// <summary>A state change. `Error` is set when the change was caused by an exception.</summary>
public sealed class RealtimeStateChangedEventArgs(RealtimeState previous, RealtimeState current, string reason, Exception? error)
    : EventArgs
{
    public RealtimeState Previous { get; } = previous;

    public RealtimeState Current { get; } = current;

    /// <summary>A short human-readable cause. It never contains the API key.</summary>
    public string Reason { get; } = reason;

    public Exception? Error { get; } = error;
}

/// <summary>One text event from the server, as raw JSON. Nothing is interpreted beyond reading its `type`.</summary>
public sealed class RealtimeMessageEventArgs(string json) : EventArgs
{
    private string? _type;
    private bool _typeRead;

    public string Json { get; } = json;

    /// <summary>The event's `type` field (like "session.created"), or null if the JSON has none.</summary>
    public string? Type
    {
        get
        {
            if (_typeRead) return _type;
            _typeRead = true;
            try
            {
                using var document = JsonDocument.Parse(Json);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("type", out var type)
                    && type.ValueKind == JsonValueKind.String)
                {
                    _type = type.GetString();
                }
            }
            catch (JsonException)
            {
                // Not JSON: leave the type unknown and let the caller look at the raw text.
            }
            return _type;
        }
    }
}
