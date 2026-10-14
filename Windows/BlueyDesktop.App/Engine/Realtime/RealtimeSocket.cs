using System.Net.WebSockets;

namespace BlueyDesktop.Engine.Realtime;

/// <summary>One frame read from the socket. For a close frame, `CloseStatus` and `CloseDescription` say why.</summary>
internal readonly record struct SocketFrame(
    WebSocketMessageType Type,
    int Count,
    bool EndOfMessage,
    WebSocketCloseStatus? CloseStatus = null,
    string? CloseDescription = null);

/// <summary>
/// The few WebSocket operations the client needs. The real one wraps <see cref="ClientWebSocket"/>; tests
/// swap in a scripted one so the connection lifecycle can be checked without a network.
/// </summary>
internal interface IRealtimeSocket : IDisposable
{
    Task ConnectAsync(Uri uri, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken);

    Task SendTextAsync(ReadOnlyMemory<byte> utf8, CancellationToken cancellationToken);

    Task<SocketFrame> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>Sends our close frame without waiting for the other side's (the receive loop sees that one).</summary>
    Task CloseOutputAsync(string description, CancellationToken cancellationToken);
}

/// <summary>The real socket.</summary>
internal sealed class ClientWebSocketAdapter : IRealtimeSocket
{
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(20);

    private readonly ClientWebSocket _socket = new();

    public Task ConnectAsync(Uri uri, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
    {
        _socket.Options.KeepAliveInterval = KeepAlive;
        foreach (var (name, value) in headers) _socket.Options.SetRequestHeader(name, value);
        return _socket.ConnectAsync(uri, cancellationToken);
    }

    public Task SendTextAsync(ReadOnlyMemory<byte> utf8, CancellationToken cancellationToken) =>
        _socket.SendAsync(utf8, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).AsTask();

    public async Task<SocketFrame> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
        return result.MessageType == WebSocketMessageType.Close
            ? new SocketFrame(result.MessageType, result.Count, result.EndOfMessage, _socket.CloseStatus, _socket.CloseStatusDescription)
            : new SocketFrame(result.MessageType, result.Count, result.EndOfMessage);
    }

    public Task CloseOutputAsync(string description, CancellationToken cancellationToken) =>
        _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, description, cancellationToken);

    public void Dispose() => _socket.Dispose();
}
