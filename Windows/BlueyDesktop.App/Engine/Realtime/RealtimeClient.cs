using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using BlueyDesktop.Core.Settings;

namespace BlueyDesktop.Engine.Realtime;

/// <summary>
/// A WebSocket connection to the OpenAI Realtime API: connect, disconnect, send text events, and get the
/// server's text events and the connection's state changes back as .NET events. Skeleton only: no audio,
/// no tool calls, no session setup yet. Those build on <see cref="SendAsync"/> and
/// <see cref="MessageReceived"/>.
/// </summary>
/// <remarks>
/// <para>The key goes only into the Authorization header of the handshake. It is never logged, put in a
/// state-change reason, or included in an exception message written by this class.</para>
/// <para>Unlike the Mac, where the phone held the socket and the Mac handed it a short-lived secret, this
/// process is both ends, so it connects with the key directly (the documented server-side way).</para>
/// <para>Events are raised on whatever thread caused them (usually the receive loop) and never while an
/// internal lock is held. A handler that throws is logged and ignored. Each state change carries both the
/// previous and the new state, so a handler can tell if it missed an earlier one.</para>
/// </remarks>
public sealed class RealtimeClient : IAsyncDisposable
{
    public static readonly Uri DefaultEndpoint = new("wss://api.openai.com/v1/realtime");

    private const int ReceiveBufferBytes = 16 * 1024;
    private const int MaxMessageBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(3);

    private readonly Uri _endpoint;
    private readonly Func<IRealtimeSocket> _socketFactory;
    private readonly TimeSpan _connectTimeout;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private RealtimeState _state = RealtimeState.Disconnected;
    private Session? _session;

    public RealtimeClient()
        : this(DefaultEndpoint)
    {
    }

    /// <summary>Connects to a different endpoint (a ws:// or wss:// address), for a proxy or a local test server.</summary>
    public RealtimeClient(Uri endpoint)
        : this(endpoint, () => new ClientWebSocketAdapter(), DefaultConnectTimeout)
    {
    }

    internal RealtimeClient(Uri endpoint, Func<IRealtimeSocket> socketFactory, TimeSpan connectTimeout)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.Scheme is not ("ws" or "wss"))
            throw new ArgumentException("The endpoint must be a ws:// or wss:// address.", nameof(endpoint));
        _endpoint = endpoint;
        _socketFactory = socketFactory;
        _connectTimeout = connectTimeout;
    }

    /// <summary>Raised after every state change. The state is already updated when handlers run.</summary>
    public event EventHandler<RealtimeStateChangedEventArgs>? StateChanged;

    /// <summary>Raised for each complete text event from the server.</summary>
    public event EventHandler<RealtimeMessageEventArgs>? MessageReceived;

    public RealtimeState State
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>
    /// Connects using the key and model from the app's settings (see <see cref="SettingsLoader"/>).
    /// Throws if no key is set.
    /// </summary>
    public Task ConnectAsync(OpenAiSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.HasApiKey)
        {
            throw new InvalidOperationException(
                $"No OpenAI key is set. Put one in {SettingsLoader.LocalFileName} or the {SettingsLoader.ApiKeyEnvironmentVariable} environment variable.");
        }
        return ConnectAsync(settings.ApiKey.Trim(), settings.RealtimeModel, cancellationToken);
    }

    /// <summary>
    /// Opens the socket. Completes when the connection is up. If it can't be opened the state becomes
    /// <see cref="RealtimeState.Faulted"/> and this throws. A <see cref="DisconnectAsync"/> that lands
    /// while connecting makes this throw <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task ConnectAsync(string apiKey, string model, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("The API key is empty.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("The model is empty.", nameof(model));

        var uri = new UriBuilder(_endpoint) { Query = "model=" + Uri.EscapeDataString(model.Trim()) }.Uri;
        var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + apiKey.Trim() };

        Session session;
        RealtimeStateChangedEventArgs? connecting;
        lock (_gate)
        {
            if (_state is not (RealtimeState.Disconnected or RealtimeState.Faulted))
                throw new InvalidOperationException($"Can't connect while the client is {_state}.");
            session = new Session(_socketFactory());
            _session = session;
            connecting = MoveLocked(session, RealtimeState.Connecting, "Connecting.");
        }
        Raise(connecting);

        try
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cts.Token);
            attempt.CancelAfter(_connectTimeout);
            await session.Socket.ConnectAsync(uri, headers, attempt.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (session.Cts.IsCancellationRequested)
            {
                // DisconnectAsync got here first and has already moved the state.
                throw new OperationCanceledException("Disconnected while connecting.", ex);
            }
            if (cancellationToken.IsCancellationRequested)
            {
                Terminate(session, RealtimeState.Disconnected, "Connecting was cancelled.", null);
                throw;
            }
            Exception error = ex is OperationCanceledException
                ? new TimeoutException($"No connection within {_connectTimeout.TotalSeconds:0.#} s.")
                : ex;
            Terminate(session, RealtimeState.Faulted, "Couldn't connect: " + error.Message, error);
            if (ReferenceEquals(error, ex)) throw;
            throw error;
        }

        RealtimeStateChangedEventArgs? connected;
        lock (_gate)
        {
            connected = MoveLocked(session, RealtimeState.Connected, "Connected.");
            if (connected is not null) session.Loop = Task.Run(() => ReceiveLoopAsync(session));
        }
        if (connected is null)
        {
            // A disconnect won the race between the socket opening and this point.
            Release(session);
            throw new OperationCanceledException("Disconnected while connecting.");
        }
        Raise(connected);
    }

    /// <summary>
    /// Closes the connection cleanly: sends the close frame and waits a few seconds for the server's. Safe to
    /// call in any state, and from several places at once; every caller returns once the client is
    /// <see cref="RealtimeState.Disconnected"/>.
    /// </summary>
    public async Task DisconnectAsync()
    {
        Session session;
        RealtimeStateChangedEventArgs? change = null;
        var alreadyClosing = false;
        var wasConnecting = false;
        lock (_gate)
        {
            if (_session is null) return; // already Disconnected or Faulted
            session = _session;
            switch (_state)
            {
                case RealtimeState.Disconnecting:
                    alreadyClosing = true;
                    break;
                case RealtimeState.Connecting:
                    session.ClientClosing = true;
                    wasConnecting = true;
                    change = MoveLocked(session, RealtimeState.Disconnected, "Disconnected while connecting.");
                    break;
                default:
                    session.ClientClosing = true;
                    change = MoveLocked(session, RealtimeState.Disconnecting, "Disconnecting.");
                    break;
            }
        }

        if (alreadyClosing)
        {
            await session.Done.Task.ConfigureAwait(false);
            return;
        }
        Raise(change);
        if (wasConnecting)
        {
            Release(session);
            return;
        }

        try
        {
            using var closeTimeout = new CancellationTokenSource(CloseTimeout);
            await session.Socket.CloseOutputAsync("Client disconnect", closeTimeout.Token).ConfigureAwait(false);
            // The server's close frame ends the receive loop; if it never comes, give up after the timeout.
            if (session.Loop is { } loop) await Task.WhenAny(loop, Task.Delay(CloseTimeout)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Bluey: the Realtime close handshake didn't finish cleanly: {ex.Message}");
        }
        Terminate(session, RealtimeState.Disconnected, "Disconnected by the client.", null);
    }

    /// <summary>Sends one text event (a JSON string). Throws if the client isn't <see cref="RealtimeState.Connected"/>.</summary>
    public async Task SendAsync(string json, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(json);
        Session session;
        lock (_gate)
        {
            if (_state != RealtimeState.Connected || _session is null)
                throw new InvalidOperationException($"Can't send while the client is {_state}.");
            session = _session;
        }

        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false); // one send at a time on a WebSocket
        try
        {
            await session.Socket.SendTextAsync(Encoding.UTF8.GetBytes(json), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Terminate(session, RealtimeState.Faulted, "Sending failed: " + ex.Message, ex);
            throw;
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Bluey: disconnecting during dispose failed: {ex.Message}");
        }
        _sendGate.Dispose();
    }

    // MARK: Receiving

    private async Task ReceiveLoopAsync(Session session)
    {
        var buffer = new byte[ReceiveBufferBytes];
        using var message = new MemoryStream();
        try
        {
            while (true)
            {
                var frame = await session.Socket.ReceiveAsync(buffer, session.Cts.Token).ConfigureAwait(false);
                if (frame.Type == WebSocketMessageType.Close)
                {
                    await HandleServerCloseAsync(session, frame).ConfigureAwait(false);
                    return;
                }

                message.Write(buffer, 0, frame.Count);
                if (message.Length > MaxMessageBytes)
                    throw new InvalidDataException($"A message from the server was larger than {MaxMessageBytes / (1024 * 1024)} MB.");
                if (!frame.EndOfMessage) continue;

                if (frame.Type == WebSocketMessageType.Text)
                    RaiseMessage(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                else
                    Trace.TraceWarning($"Bluey: ignoring a binary Realtime message ({message.Length} bytes).");
                message.SetLength(0);
            }
        }
        catch (OperationCanceledException) when (session.Cts.IsCancellationRequested)
        {
            // The session was released (disconnect or fault): nothing more to do.
        }
        catch (Exception ex)
        {
            if (!IsClientClosing(session))
                Terminate(session, RealtimeState.Faulted, "The connection failed: " + ex.Message, ex);
        }
    }

    private async Task HandleServerCloseAsync(Session session, SocketFrame frame)
    {
        // If we asked to close, DisconnectAsync owns the final state change.
        if (IsClientClosing(session)) return;

        try
        {
            using var timeout = new CancellationTokenSource(CloseTimeout);
            await session.Socket.CloseOutputAsync("Closing in reply", timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Bluey: couldn't answer the server's close frame: {ex.Message}");
        }

        var said = $"{frame.CloseStatus?.ToString() ?? "no status"}{(string.IsNullOrEmpty(frame.CloseDescription) ? "" : ": " + frame.CloseDescription)}";
        var normal = frame.CloseStatus is null or WebSocketCloseStatus.NormalClosure or WebSocketCloseStatus.EndpointUnavailable;
        if (normal)
            Terminate(session, RealtimeState.Disconnected, $"The server closed the connection ({said}).", null);
        else
            Terminate(session, RealtimeState.Faulted, $"The server closed the connection with an error ({said}).", new WebSocketException(said));
    }

    // MARK: State

    /// <summary>One connection attempt and everything that belongs to it, so a late callback from an old attempt can't touch a new one.</summary>
    private sealed class Session(IRealtimeSocket socket)
    {
        public IRealtimeSocket Socket { get; } = socket;

        public CancellationTokenSource Cts { get; } = new();

        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task? Loop { get; set; }

        /// <summary>True once the client itself asked to close. Guarded by the client's lock.</summary>
        public bool ClientClosing { get; set; }
    }

    private bool IsClientClosing(Session session)
    {
        lock (_gate) return session.ClientClosing;
    }

    /// <summary>
    /// Moves the state if `session` is the current attempt and the move is legal; returns the event to raise,
    /// or null if nothing changed. A move to Disconnected or Faulted ends the attempt. Caller holds the lock.
    /// </summary>
    private RealtimeStateChangedEventArgs? MoveLocked(Session session, RealtimeState to, string reason, Exception? error = null)
    {
        if (!ReferenceEquals(_session, session) || !RealtimeStateMachine.CanMove(_state, to)) return null;
        var previous = _state;
        _state = to;
        if (to is RealtimeState.Disconnected or RealtimeState.Faulted) _session = null;
        return new RealtimeStateChangedEventArgs(previous, to, reason, error);
    }

    /// <summary>
    /// Ends an attempt in a terminal state: moves the state, frees the socket, then tells listeners. If the
    /// move isn't allowed (the attempt already ended, or a clean disconnect is under way) nothing is touched.
    /// </summary>
    private void Terminate(Session session, RealtimeState to, string reason, Exception? error)
    {
        RealtimeStateChangedEventArgs? change;
        lock (_gate) change = MoveLocked(session, to, reason, error);
        if (change is null) return;
        Release(session);
        Raise(change);
    }

    private static void Release(Session session)
    {
        try
        {
            session.Cts.Cancel();
            session.Socket.Dispose();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Bluey: releasing the Realtime socket failed: {ex.Message}");
        }
        session.Done.TrySetResult();
    }

    private void Raise(RealtimeStateChangedEventArgs? change)
    {
        if (change is null) return;
        try
        {
            StateChanged?.Invoke(this, change);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Bluey: a Realtime state handler threw: {ex}");
        }
    }

    private void RaiseMessage(string json)
    {
        try
        {
            MessageReceived?.Invoke(this, new RealtimeMessageEventArgs(json));
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Bluey: a Realtime message handler threw: {ex}");
        }
    }
}
