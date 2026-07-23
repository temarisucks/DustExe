using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Dust;

internal sealed record OnlineMessage(
    string Type,
    string? RequestId,
    JsonElement Data);

/// <summary>
/// One ordered WebSocket transport for account, lobby, input, and snapshot
/// traffic. WebSockets are deliberate here: Dust's grid commands are small and
/// reliable ordering is more useful than a second lossy transport.
/// </summary>
internal sealed class OnlineClient : IDisposable
{
    private const int MaximumMessageBytes = 80 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _lifetime;
    private Task? _receiveLoop;
    private int _disposed;

    public event Action<OnlineMessage>? MessageReceived;
    public event Action<string>? ConnectionClosed;

    public bool IsConnected => _socket?.State == WebSocketState.Open;
    public DateTimeOffset LastMessageUtc { get; private set; } = DateTimeOffset.MinValue;
    public Uri? Endpoint { get; private set; }

    public async Task ConnectAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("ws" or "wss"))
            throw new InvalidOperationException("The online server must use a ws:// or wss:// address.");

        await DisconnectAsync(notify: false);
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(12);
        socket.Options.SetRequestHeader("User-Agent", "Dust/1.0");
        var lifetime = new CancellationTokenSource();
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, lifetime.Token);
            await socket.ConnectAsync(uri, linked.Token);
        }
        catch
        {
            socket.Dispose();
            lifetime.Dispose();
            throw;
        }

        _socket = socket;
        _lifetime = lifetime;
        Endpoint = uri;
        LastMessageUtc = DateTimeOffset.UtcNow;
        _receiveLoop = ReceiveLoopAsync(socket, lifetime.Token);
    }

    public async Task SendAsync(
        string type,
        object? payload = null,
        string? requestId = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var socket = _socket;
        if (socket?.State != WebSocketState.Open)
            throw new InvalidOperationException("The online connection is not open.");

        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type,
            requestId,
            payload = payload ?? new { }
        }, JsonOptions);
        if (bytes.Length > MaximumMessageBytes)
            throw new InvalidOperationException("The online message is too large.");

        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            if (socket.State != WebSocketState.Open)
                throw new InvalidOperationException("The online connection closed before the message was sent.");
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public async Task DisconnectAsync(bool notify = false)
    {
        var lifetime = Interlocked.Exchange(ref _lifetime, null);
        var socket = Interlocked.Exchange(ref _socket, null);
        lifetime?.Cancel();
        if (socket is not null)
        {
            try
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
                    await socket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure, "client leaving", timeout.Token);
                }
            }
            catch
            {
                // Disposal below is the final transport shutdown path.
            }
            socket.Dispose();
        }
        lifetime?.Dispose();
        Endpoint = null;
        if (notify) ConnectionClosed?.Invoke("CONNECTION CLOSED");
    }

    public static bool IsCredentialTransportSecure(Uri endpoint)
    {
        if (endpoint.Scheme == "wss") return true;
        if (endpoint.Scheme != "ws") return false;
        return endpoint.IsLoopback;
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        string closeReason = "SERVER CONNECTION LOST";
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   socket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                message.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        closeReason = string.IsNullOrWhiteSpace(result.CloseStatusDescription)
                            ? "SERVER CLOSED CONNECTION"
                            : result.CloseStatusDescription!;
                        return;
                    }
                    if (result.MessageType != WebSocketMessageType.Text)
                        throw new InvalidDataException("The server sent a non-text protocol message.");
                    if (message.Length + result.Count > MaximumMessageBytes)
                        throw new InvalidDataException("The server message exceeded the protocol limit.");
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                using var document = JsonDocument.Parse(message.GetBuffer().AsMemory(0, (int)message.Length));
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("type", out var typeNode) ||
                    typeNode.ValueKind != JsonValueKind.String)
                    continue;
                var type = typeNode.GetString();
                if (string.IsNullOrWhiteSpace(type)) continue;
                var requestId = root.TryGetProperty("requestId", out var requestNode) &&
                                requestNode.ValueKind == JsonValueKind.String
                    ? requestNode.GetString()
                    : null;
                var data = root.TryGetProperty("data", out var dataNode)
                    ? dataNode.Clone()
                    : root.TryGetProperty("payload", out var payloadNode)
                        ? payloadNode.Clone()
                        : EmptyObject();
                LastMessageUtc = DateTimeOffset.UtcNow;
                MessageReceived?.Invoke(new OnlineMessage(type, requestId, data));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            closeReason = exception is InvalidDataException
                ? exception.Message
                : "SERVER CONNECTION LOST";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                ConnectionClosed?.Invoke(closeReason);
        }
    }

    private static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var lifetime = Interlocked.Exchange(ref _lifetime, null);
        lifetime?.Cancel();
        lifetime?.Dispose();
        var socket = Interlocked.Exchange(ref _socket, null);
        socket?.Dispose();
        _sendGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
