// Checks for Engine/Realtime. Three layers:
//   1. the state-machine table;
//   2. the RealtimeClient lifecycle against a scripted fake socket (no network);
//   3. the real ClientWebSocket against a local HttpListener server (real handshake, headers, framing, close).
// None of it contacts OpenAI. RunLiveAsync does, but only when the self-test is started with --live-openai.
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using BlueyDesktop.Core.Settings;
using BlueyDesktop.Engine.Realtime;

namespace BlueyDesktop.Engine.SelfTest;

delegate void CheckFn(string name, bool ok, string detail = "");

static class RealtimeChecks
{
    private const string Key = "sk-test-SECRET-123";
    private const string FakeEndpoint = "wss://example.test/v1/realtime";

    public static async Task RunAsync(CheckFn check, Action<string> section)
    {
        // -----------------------------------------------------------------------------------------------
        section("realtime: state machine table");
        {
            var all = Enum.GetValues<RealtimeState>();
            var legal = new HashSet<(RealtimeState, RealtimeState)>
            {
                (RealtimeState.Disconnected, RealtimeState.Connecting),
                (RealtimeState.Faulted, RealtimeState.Connecting),
                (RealtimeState.Connecting, RealtimeState.Connected),
                (RealtimeState.Connecting, RealtimeState.Disconnected),
                (RealtimeState.Connecting, RealtimeState.Faulted),
                (RealtimeState.Connected, RealtimeState.Disconnecting),
                (RealtimeState.Connected, RealtimeState.Disconnected),
                (RealtimeState.Connected, RealtimeState.Faulted),
                (RealtimeState.Disconnecting, RealtimeState.Disconnected),
            };
            var wrong = all.SelectMany(from => all.Select(to => (from, to)))
                .Where(pair => RealtimeStateMachine.CanMove(pair.from, pair.to) != legal.Contains(pair))
                .ToList();
            check("exactly the nine legal moves are allowed (25 pairs checked)", wrong.Count == 0, string.Join(", ", wrong));
        }

        // -----------------------------------------------------------------------------------------------
        section("realtime: lifecycle with a scripted socket");
        {
            var socket = new FakeSocket();
            var client = FakeClient(socket);
            var seen = new Recorder(client);
            check("starts Disconnected", client.State == RealtimeState.Disconnected);

            await client.ConnectAsync(Key, "gpt-test model");
            check("connect: Connecting then Connected", seen.Path == "Connecting>Connected" && client.State == RealtimeState.Connected, seen.Path);
            check("handshake carries the bearer key and the escaped model",
                socket.Headers.GetValueOrDefault("Authorization") == "Bearer " + Key && socket.Uri?.Query == "?model=gpt-test%20model", $"{socket.Uri}");

            socket.PushText("{\"type\":\"session.created\",\"session\":{}}");
            socket.PushText("{\"type\":\"response.", endOfMessage: false);
            socket.PushText("text.delta\",\"delta\":\"hi\"}");
            socket.PushBinary();
            socket.PushText("not json");
            seen.WaitForMessages(3);
            check("text events arrive; a message split across frames is joined; binary is dropped",
                seen.Messages.Count == 3 && seen.Messages[0].Type == "session.created" && seen.Messages[1].Type == "response.text.delta"
                && seen.Messages[1].Json.Contains("\"delta\":\"hi\"") && seen.Messages[2].Json == "not json" && seen.Messages[2].Type is null,
                string.Join(" | ", seen.Messages.Select(m => m.Type ?? "(none)")));

            await client.SendAsync("{\"type\":\"session.update\"}");
            check("send reaches the socket as text", socket.Sent.SequenceEqual(["{\"type\":\"session.update\"}"]));

            await client.DisconnectAsync();
            check("disconnect: Disconnecting then Disconnected, one close frame, socket freed",
                seen.Path == "Connecting>Connected>Disconnecting>Disconnected" && socket.CloseOutputCalls == 1 && socket.Disposed && client.State == RealtimeState.Disconnected, seen.Path);
            await ExpectThrowsAsync<InvalidOperationException>(() => client.SendAsync("{}"), check, "send after disconnect throws");
            await client.DisconnectAsync();
            check("disconnecting twice does nothing", seen.Path == "Connecting>Connected>Disconnecting>Disconnected");
            check("every change has a reason, and the first/last names are right",
                seen.States.All(s => s.Reason.Length > 0) && seen.States[0].Previous == RealtimeState.Disconnected && seen.States[^1].Current == RealtimeState.Disconnected);

            socket = new FakeSocket();
            client = FakeClient(socket);
            seen = new Recorder(client);
            await client.ConnectAsync(Key, "m");
            await ExpectThrowsAsync<InvalidOperationException>(() => client.ConnectAsync(Key, "m"), check, "a second connect while connected throws");
            check("and leaves the state alone", client.State == RealtimeState.Connected && seen.Path == "Connecting>Connected");
            await client.DisposeAsync();
            check("dispose disconnects", client.State == RealtimeState.Disconnected && socket.Disposed);
        }
        {
            var client = FakeClient(new FakeSocket());
            await ExpectThrowsAsync<ArgumentException>(() => client.ConnectAsync("  ", "m"), check, "an empty key is rejected before anything happens");
            await ExpectThrowsAsync<ArgumentException>(() => client.ConnectAsync(Key, ""), check, "an empty model is rejected");
            check("and the state is untouched", client.State == RealtimeState.Disconnected);
            var wrongScheme = false;
            try { _ = new RealtimeClient(new Uri("https://example.test")); } catch (ArgumentException) { wrongScheme = true; }
            check("an http(s) endpoint is rejected", wrongScheme);
        }

        section("realtime: failures");
        var leaked = new List<string>();
        {
            var socket = new FakeSocket { ConnectBehavior = _ => throw new WebSocketException("The server returned status code '401' when status code '101' was expected.") };
            var client = FakeClient(socket);
            var seen = new Recorder(client);
            await ExpectThrowsAsync<WebSocketException>(() => client.ConnectAsync(Key, "m"), check, "a refused handshake throws to the caller");
            check("and the state is Faulted with the error attached", seen.Path == "Connecting>Faulted" && seen.States[^1].Error is WebSocketException && socket.Disposed, seen.Path);
            leaked.AddRange(seen.States.Select(s => s.Reason + s.Error?.Message));
            check("Faulted allows connecting again (same client)", await ReconnectAfterFault());
        }
        {
            var socket = new FakeSocket { ConnectBehavior = async ct => await Task.Delay(Timeout.Infinite, ct) };
            var client = new RealtimeClient(new Uri(FakeEndpoint), () => socket, TimeSpan.FromMilliseconds(150));
            var seen = new Recorder(client);
            await ExpectThrowsAsync<TimeoutException>(() => client.ConnectAsync(Key, "m"), check, "a connect that hangs times out");
            check("and ends Faulted", seen.Path == "Connecting>Faulted" && socket.Disposed, seen.Path);
            leaked.AddRange(seen.States.Select(s => s.Reason + s.Error?.Message));
        }
        {
            using var cancel = new CancellationTokenSource();
            var socket = new FakeSocket { ConnectBehavior = async ct => await Task.Delay(Timeout.Infinite, ct) };
            var client = FakeClient(socket);
            var seen = new Recorder(client);
            var connecting = client.ConnectAsync(Key, "m", cancel.Token);
            SpinWait.SpinUntil(() => client.State == RealtimeState.Connecting, 1000);
            cancel.Cancel();
            await ExpectThrowsAsync<OperationCanceledException>(() => connecting, check, "cancelling the token cancels the connect");
            check("and returns to Disconnected, not Faulted", seen.Path == "Connecting>Disconnected", seen.Path);
        }
        {
            var socket = new FakeSocket { ConnectBehavior = async ct => await Task.Delay(Timeout.Infinite, ct) };
            var client = FakeClient(socket);
            var seen = new Recorder(client);
            var connecting = client.ConnectAsync(Key, "m");
            SpinWait.SpinUntil(() => client.State == RealtimeState.Connecting, 1000);
            await client.DisconnectAsync();
            await ExpectThrowsAsync<OperationCanceledException>(() => connecting, check, "disconnecting while connecting cancels the connect");
            check("the state goes straight to Disconnected and the socket is freed",
                seen.Path == "Connecting>Disconnected" && socket.Disposed && client.State == RealtimeState.Disconnected, seen.Path);
        }
        {
            var socket = new FakeSocket();
            var client = FakeClient(socket);
            var seen = new Recorder(client);
            await client.ConnectAsync(Key, "m");
            socket.PushClose(WebSocketCloseStatus.NormalClosure, "bye");
            SpinWait.SpinUntil(() => client.State != RealtimeState.Connected, 2000);
            check("a normal close from the server -> Disconnected, and we answered with our own close frame",
                seen.Path == "Connecting>Connected>Disconnected" && socket.CloseOutputCalls == 1 && seen.States[^1].Reason.Contains("server closed"),
                seen.Path + " / " + seen.States[^1].Reason);

            socket = new FakeSocket();
            client = FakeClient(socket);
            seen = new Recorder(client);
            await client.ConnectAsync(Key, "m");
            socket.PushClose(WebSocketCloseStatus.PolicyViolation, "bad request");
            SpinWait.SpinUntil(() => client.State != RealtimeState.Connected, 2000);
            check("an error close from the server -> Faulted with the status in the reason",
                seen.Path == "Connecting>Connected>Faulted" && seen.States[^1].Reason.Contains("PolicyViolation") && seen.States[^1].Error is not null, seen.Path);

            socket = new FakeSocket();
            client = FakeClient(socket);
            seen = new Recorder(client);
            await client.ConnectAsync(Key, "m");
            socket.PushError(new IOException("connection reset"));
            SpinWait.SpinUntil(() => client.State != RealtimeState.Connected, 2000);
            check("a receive error -> Faulted and the socket is freed", seen.Path == "Connecting>Connected>Faulted" && socket.Disposed, seen.Path);
            leaked.AddRange(seen.States.Select(s => s.Reason + s.Error?.Message));
            await ExpectThrowsAsync<InvalidOperationException>(() => client.SendAsync("{}"), check, "sending when Faulted throws");
        }
        {
            var socket = new FakeSocket { SendError = new IOException("broken pipe") };
            var client = FakeClient(socket);
            var seen = new Recorder(client);
            await client.ConnectAsync(Key, "m");
            await ExpectThrowsAsync<IOException>(() => client.SendAsync("{}"), check, "a failed send throws to the caller");
            check("and faults the connection", seen.Path == "Connecting>Connected>Faulted", seen.Path);
        }

        section("realtime: events and concurrency");
        {
            var socket = new FakeSocket();
            var client = FakeClient(socket);
            var delivered = new List<string>();
            client.MessageReceived += (_, e) =>
            {
                lock (delivered) delivered.Add(e.Json);
                if (e.Json == "first") throw new InvalidOperationException("handler bug");
            };
            client.StateChanged += (_, _) => throw new InvalidOperationException("state handler bug");
            await client.ConnectAsync(Key, "m");
            socket.PushText("first");
            socket.PushText("second");
            SpinWait.SpinUntil(() => { lock (delivered) return delivered.Count == 2; }, 2000);
            check("a handler that throws doesn't stop later events or state changes",
                delivered.SequenceEqual(["first", "second"]) && client.State == RealtimeState.Connected, string.Join(",", delivered));
            await client.DisconnectAsync();
        }
        {
            var socket = new FakeSocket();
            var client = FakeClient(socket);
            var seen = new Recorder(client);
            await client.ConnectAsync(Key, "m");
            await Task.WhenAll(client.DisconnectAsync(), client.DisconnectAsync(), client.DisconnectAsync());
            check("three disconnects at once: all return, one close frame, states in order",
                seen.Path == "Connecting>Connected>Disconnecting>Disconnected" && socket.CloseOutputCalls == 1 && client.State == RealtimeState.Disconnected, seen.Path);
        }
        {
            var socket = new FakeSocket { AutoReplyToClose = false };
            var client = FakeClient(socket);
            var seen = new Recorder(client);
            await client.ConnectAsync(Key, "m");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await client.DisconnectAsync();
            watch.Stop();
            check("a server that never answers our close frame doesn't hang us (gives up after a few seconds)",
                seen.Path == "Connecting>Connected>Disconnecting>Disconnected" && watch.Elapsed < TimeSpan.FromSeconds(6) && socket.Disposed, $"{watch.Elapsed.TotalSeconds:0.0} s");
        }

        section("realtime: key comes from the app settings");
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bluey-realtime-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(System.IO.Path.Combine(directory, SettingsLoader.BaseFileName), "{ \"OpenAi\": { \"ApiKey\": \"\", \"RealtimeModel\": \"gpt-from-file\" } }");
                var noKey = SettingsLoader.Load(directory, _ => null);
                var socket = new FakeSocket();
                var client = FakeClient(socket);
                var thrown = await ExpectThrowsAsync<InvalidOperationException>(() => client.ConnectAsync(noKey.OpenAi), check, "no key in settings -> a clear error and nothing is opened");
                check("the error says where to put one",
                    thrown is not null && thrown.Message.Contains(SettingsLoader.LocalFileName) && thrown.Message.Contains(SettingsLoader.ApiKeyEnvironmentVariable) && socket.Uri is null);

                File.WriteAllText(System.IO.Path.Combine(directory, SettingsLoader.LocalFileName), "{ \"OpenAi\": { \"ApiKey\": \"sk-from-local-file\" } }");
                await client.ConnectAsync(SettingsLoader.Load(directory, _ => null).OpenAi);
                check("the local settings file supplies the key and the base file the model",
                    socket.Headers.GetValueOrDefault("Authorization") == "Bearer sk-from-local-file" && socket.Uri?.Query == "?model=gpt-from-file", $"{socket.Uri}");
                await client.DisconnectAsync();

                socket = new FakeSocket();
                client = FakeClient(socket);
                await client.ConnectAsync(SettingsLoader.Load(directory, name => name == SettingsLoader.ApiKeyEnvironmentVariable ? "sk-from-env" : null).OpenAi);
                check("the environment variable wins", socket.Headers.GetValueOrDefault("Authorization") == "Bearer sk-from-env");
                await client.DisconnectAsync();
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        // -----------------------------------------------------------------------------------------------
        section("realtime: the real ClientWebSocket against a local server");
        {
            await using var server = new LocalRealtimeServer(LocalRealtimeServer.Mode.Echo);
            await using var client = new RealtimeClient(server.Endpoint);
            var seen = new Recorder(client);
            await client.ConnectAsync(Key, "gpt-local");
            check("handshake: Connected, and the server saw the path, model and bearer key",
                client.State == RealtimeState.Connected && server.RequestPath == "/v1/realtime" && server.Query == "?model=gpt-local" && server.Authorization == "Bearer " + Key,
                $"{server.RequestPath}{server.Query} auth={(server.Authorization is null ? "(none)" : "(present)")}");
            seen.WaitForMessages(1);
            check("the server's first event arrives", seen.Messages.Count >= 1 && seen.Messages[0].Type == "session.created");

            const string sent = "{\"type\":\"session.update\",\"session\":{\"instructions\":\"héllo 😀\"}}";
            await client.SendAsync(sent);
            seen.WaitForMessages(2);
            check("a sent event round-trips, UTF-8 intact", seen.Messages.Count >= 2 && seen.Messages[1].Type == "echo" && EchoedText(seen.Messages[1]) == sent,
                seen.Messages.Count >= 2 ? EchoedText(seen.Messages[1]) ?? "(null)" : "(none)");

            await client.SendAsync("{\"type\":\"big\"}");
            seen.WaitForMessages(3, 5000);
            check("a 100,000-character message arrives whole (many frames joined)", seen.Messages.Count >= 3 && seen.Messages[2].Json.Length == 100_000,
                seen.Messages.Count >= 3 ? seen.Messages[2].Json.Length.ToString() : "(none)");

            await client.DisconnectAsync();
            var closed = await Task.WhenAny(server.ClientClosed.Task, Task.Delay(3000)) == server.ClientClosed.Task;
            check("clean close: the server got a normal close frame", closed && server.ClientClosed.Task.Result == WebSocketCloseStatus.NormalClosure);
            check("states: Connecting>Connected>Disconnecting>Disconnected", seen.Path == "Connecting>Connected>Disconnecting>Disconnected", seen.Path);
            leaked.AddRange(seen.States.Select(s => s.Reason));
        }
        {
            await using var server = new LocalRealtimeServer(LocalRealtimeServer.Mode.Reject401);
            await using var client = new RealtimeClient(server.Endpoint);
            var seen = new Recorder(client);
            var ex = await ExpectThrowsAsync<WebSocketException>(() => client.ConnectAsync(Key, "m"), check, "a 401 on the handshake throws WebSocketException");
            check("and leaves the client Faulted", seen.Path == "Connecting>Faulted", seen.Path + " / " + seen.States[^1].Reason);
            leaked.AddRange(seen.States.Select(s => s.Reason + s.Error?.Message));
            leaked.Add(ex?.Message ?? "");
        }
        {
            await using var server = new LocalRealtimeServer(LocalRealtimeServer.Mode.CloseWithPolicyViolation);
            await using var client = new RealtimeClient(server.Endpoint);
            var seen = new Recorder(client);
            await client.ConnectAsync(Key, "m");
            SpinWait.SpinUntil(() => client.State != RealtimeState.Connected, 3000);
            check("a server that closes with an error code -> Faulted",
                seen.Path == "Connecting>Connected>Faulted" && seen.States[^1].Reason.Contains("PolicyViolation"), seen.Path + " / " + seen.States[^1].Reason);
        }
        {
            await using var client = new RealtimeClient(new Uri($"ws://localhost:{FreePort()}/v1/realtime"));
            var seen = new Recorder(client);
            await ExpectThrowsAsync<WebSocketException>(() => client.ConnectAsync(Key, "m"), check, "connecting to nothing throws");
            check("and ends Faulted", seen.Path == "Connecting>Faulted", seen.Path);
            leaked.AddRange(seen.States.Select(s => s.Reason + s.Error?.Message));
        }
        check("the key never shows up in a state reason or an error message (fake and real failures)",
            leaked.Count > 0 && leaked.All(text => !text.Contains("SECRET-123")), string.Join(" | ", leaked.Where(text => text.Contains("SECRET-123"))));
    }

    /// <summary>
    /// Opt-in: a real connection to OpenAI with your key (from appsettings*.json in `settingsDirectory`, or the
    /// BLUEY_OPENAI_API_KEY environment variable). It only opens the socket, waits for the server's first event
    /// and closes, so it should cost nothing. It prints event types and states, never the key.
    /// </summary>
    public static async Task RunLiveAsync(string settingsDirectory)
    {
        var settings = SettingsLoader.Load(settingsDirectory).OpenAi;
        Console.WriteLine($"settings: {settings}");
        if (!settings.HasApiKey)
        {
            Console.WriteLine("No key found, so there is nothing to connect with. Set BLUEY_OPENAI_API_KEY or pass the folder that holds appsettings.Local.json.");
            return;
        }
        await using var client = new RealtimeClient();
        var first = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StateChanged += (_, e) => Console.WriteLine($"state: {e.Previous} -> {e.Current} ({e.Reason})");
        client.MessageReceived += (_, e) =>
        {
            Console.WriteLine($"event: {e.Type ?? "(no type)"}");
            first.TrySetResult(e.Type);
        };
        try
        {
            await client.ConnectAsync(settings);
            var winner = await Task.WhenAny(first.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Console.WriteLine(winner == first.Task ? $"first event: {first.Task.Result}" : "no event within 10 s");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"live connection failed: {ex.GetType().Name}: {ex.Message}");
        }
        await client.DisconnectAsync();
        Console.WriteLine($"final state: {client.State}");
    }

    // MARK: helpers

    private static async Task<bool> ReconnectAfterFault()
    {
        var attempts = 0;
        await using var client = new RealtimeClient(new Uri(FakeEndpoint), () =>
        {
            var socket = new FakeSocket();
            if (Interlocked.Increment(ref attempts) == 1) socket.ConnectBehavior = _ => throw new IOException("first attempt fails");
            return socket;
        }, TimeSpan.FromSeconds(2));
        try { await client.ConnectAsync(Key, "m"); } catch (IOException) { }
        if (client.State != RealtimeState.Faulted) return false;
        await client.ConnectAsync(Key, "m");
        return client.State == RealtimeState.Connected;
    }

    private static string? EchoedText(RealtimeMessageEventArgs message)
    {
        using var document = JsonDocument.Parse(message.Json);
        return document.RootElement.TryGetProperty("text", out var text) ? text.GetString() : null;
    }

    private static RealtimeClient FakeClient(FakeSocket socket) =>
        new(new Uri(FakeEndpoint), () => socket, TimeSpan.FromSeconds(2));

    private static async Task<T?> ExpectThrowsAsync<T>(Func<Task> action, CheckFn check, string name)
        where T : Exception
    {
        try
        {
            await action();
            check(name, false, "nothing was thrown");
            return null;
        }
        catch (Exception ex) when (ex is T expected)
        {
            check(name, true);
            return expected;
        }
        catch (Exception ex)
        {
            check(name, false, $"threw {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Collects a client's events so a test can look at them afterwards.</summary>
    private sealed class Recorder
    {
        public List<RealtimeStateChangedEventArgs> States { get; } = [];

        public List<RealtimeMessageEventArgs> Messages { get; } = [];

        public Recorder(RealtimeClient client)
        {
            client.StateChanged += (_, e) => { lock (States) States.Add(e); };
            client.MessageReceived += (_, e) => { lock (Messages) Messages.Add(e); };
        }

        /// <summary>The states entered so far, like "Connecting>Connected".</summary>
        public string Path
        {
            get { lock (States) return string.Join(">", States.Select(s => s.Current)); }
        }

        public void WaitForMessages(int count, int milliseconds = 3000) =>
            SpinWait.SpinUntil(() => { lock (Messages) return Messages.Count >= count; }, milliseconds);
    }

    /// <summary>A socket that does what a test tells it to.</summary>
    private sealed class FakeSocket : IRealtimeSocket
    {
        private sealed record Item(SocketFrame? Frame, byte[]? Bytes, Exception? Error);

        private readonly Channel<Item> _incoming = Channel.CreateUnbounded<Item>();
        private int _closeOutputCalls;

        public Uri? Uri { get; private set; }

        public Dictionary<string, string> Headers { get; } = [];

        public List<string> Sent { get; } = [];

        public Func<CancellationToken, Task>? ConnectBehavior { get; set; }

        public Exception? SendError { get; set; }

        public bool AutoReplyToClose { get; set; } = true;

        public bool Disposed { get; private set; }

        public int CloseOutputCalls => Volatile.Read(ref _closeOutputCalls);

        public async Task ConnectAsync(Uri uri, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            Uri = uri;
            foreach (var (name, value) in headers) Headers[name] = value;
            if (ConnectBehavior is not null) await ConnectBehavior(cancellationToken);
        }

        public Task SendTextAsync(ReadOnlyMemory<byte> utf8, CancellationToken cancellationToken)
        {
            if (SendError is not null) throw SendError;
            lock (Sent) Sent.Add(Encoding.UTF8.GetString(utf8.Span));
            return Task.CompletedTask;
        }

        public async Task<SocketFrame> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var item = await _incoming.Reader.ReadAsync(cancellationToken);
            if (item.Error is not null) throw item.Error;
            item.Bytes?.AsMemory().CopyTo(buffer);
            return item.Frame!.Value;
        }

        public Task CloseOutputAsync(string description, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _closeOutputCalls);
            if (AutoReplyToClose) PushClose(WebSocketCloseStatus.NormalClosure, "bye");
            return Task.CompletedTask;
        }

        public void PushText(string text, bool endOfMessage = true)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            _incoming.Writer.TryWrite(new Item(new SocketFrame(WebSocketMessageType.Text, bytes.Length, endOfMessage), bytes, null));
        }

        public void PushBinary() =>
            _incoming.Writer.TryWrite(new Item(new SocketFrame(WebSocketMessageType.Binary, 3, true), [1, 2, 3], null));

        public void PushClose(WebSocketCloseStatus status, string description) =>
            _incoming.Writer.TryWrite(new Item(new SocketFrame(WebSocketMessageType.Close, 0, true, status, description), null, null));

        public void PushError(Exception error) => _incoming.Writer.TryWrite(new Item(null, null, error));

        public void Dispose()
        {
            Disposed = true;
            _incoming.Writer.TryComplete();
        }
    }

    /// <summary>A WebSocket server on localhost, so the real ClientWebSocket can be exercised end to end.</summary>
    private sealed class LocalRealtimeServer : IAsyncDisposable
    {
        public enum Mode
        {
            Echo,
            Reject401,
            CloseWithPolicyViolation,
        }

        private readonly HttpListener _listener = new();
        private readonly Mode _mode;

        public LocalRealtimeServer(Mode mode)
        {
            _mode = mode;
            var port = FreePort();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Start();
            Endpoint = new Uri($"ws://localhost:{port}/v1/realtime");
            _ = Task.Run(AcceptLoopAsync);
        }

        public Uri Endpoint { get; }

        public string? Authorization { get; private set; }

        public string? Query { get; private set; }

        public string? RequestPath { get; private set; }

        public TaskCompletionSource<WebSocketCloseStatus?> ClientClosed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (_listener.IsListening)
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleAsync(context));
                }
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                // The listener was closed: the test is over.
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                Authorization = context.Request.Headers["Authorization"];
                Query = context.Request.Url?.Query;
                RequestPath = context.Request.Url?.AbsolutePath;
                if (_mode == Mode.Reject401)
                {
                    context.Response.StatusCode = 401;
                    context.Response.Close();
                    return;
                }

                var webSocket = (await context.AcceptWebSocketAsync(subProtocol: null)).WebSocket;
                await SendAsync(webSocket, "{\"type\":\"session.created\"}");
                if (_mode == Mode.CloseWithPolicyViolation)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "no", CancellationToken.None);
                    return;
                }

                var buffer = new byte[8192];
                using var message = new MemoryStream();
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "ok", CancellationToken.None);
                        ClientClosed.TrySetResult(result.CloseStatus);
                        return;
                    }
                    message.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage) continue;
                    var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    message.SetLength(0);
                    await SendAsync(webSocket, text.Contains("\"big\"") ? new string('a', 100_000) : JsonSerializer.Serialize(new { type = "echo", text }));
                }
            }
            catch (Exception ex) when (ex is WebSocketException or HttpListenerException or ObjectDisposedException or IOException)
            {
                ClientClosed.TrySetResult(null); // the client went away without a close frame
            }
        }

        private static Task SendAsync(WebSocket webSocket, string text) =>
            webSocket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

        public ValueTask DisposeAsync()
        {
            _listener.Close();
            return ValueTask.CompletedTask;
        }
    }
}
