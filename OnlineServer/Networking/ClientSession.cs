using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Dust.OnlineServer.Configuration;
using Dust.OnlineServer.Lobbies;
using Dust.OnlineServer.Protocol;
using Dust.OnlineServer.Security;
using Microsoft.Extensions.Options;

namespace Dust.OnlineServer.Networking;

internal sealed class ClientSession
{
    private const int MaximumQueuedFrames = 256;
    private const int MaximumQueuedBytes = 2 * 1024 * 1024;

    private readonly WebSocket _socket;
    private readonly ConnectionHub _connections;
    private readonly AccountStore _accounts;
    private readonly LobbyManager _lobbies;
    private readonly ILogger<ClientSession> _logger;
    private readonly object _outboundGate = new();
    private readonly LinkedList<OutboundFrame> _outboundFrames = [];
    private readonly CancellationTokenSource _outboundLifetime = new();
    private readonly TokenBucket _inputLimiter;
    private readonly TokenBucket _snapshotLimiter;
    private readonly int _maxMessageBytes;
    private readonly TimeSpan _sendTimeout;
    private readonly Queue<long> _authAttempts = [];
    private LinkedListNode<OutboundFrame>? _pendingSnapshotFrame;
    private Task? _outboundPump;
    private int _queuedBytes;
    private bool _outboundPumpActive;
    private bool _outboundStopped;

    private sealed record OutboundFrame(
        ReadOnlyMemory<byte> Bytes,
        bool ReplaceableSnapshot);

    public ClientSession(
        WebSocket socket,
        ConnectionHub connections,
        AccountStore accounts,
        LobbyManager lobbies,
        IOptions<OnlineServerOptions> options,
        ILogger<ClientSession> logger)
    {
        _socket = socket;
        _connections = connections;
        _accounts = accounts;
        _lobbies = lobbies;
        _logger = logger;
        _maxMessageBytes = Math.Clamp(
            options.Value.MaxWebSocketMessageBytes,
            70 * 1024,
            256 * 1024);
        _sendTimeout = TimeSpan.FromSeconds(
            Math.Clamp(options.Value.PeerSendTimeoutSeconds, 1, 15));
        _inputLimiter = new TokenBucket(
            Math.Clamp(options.Value.InputMessagesPerSecond, 10, 60));
        _snapshotLimiter = new TokenBucket(
            Math.Clamp(options.Value.SnapshotMessagesPerSecond, 5, 20));
    }

    public Guid ConnectionId { get; } = Guid.NewGuid();
    public AccountIdentity? Identity { get; private set; }

    internal void SetIdentity(AccountIdentity identity) => Identity = identity;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _connections.Register(this);
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   _socket.State == WebSocketState.Open)
            {
                var message = await ReceiveTextMessageAsync(cancellationToken);
                if (message is not { } utf8)
                    break;

                await ProcessMessageAsync(utf8, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // HTTP request/server shutdown cancellation.
        }
        catch (WebSocketException exception)
        {
            _logger.LogDebug(
                exception,
                "WebSocket {ConnectionId} ended unexpectedly.",
                ConnectionId);
        }
        finally
        {
            _connections.Unregister(this);
            if (Identity is not null)
                await _lobbies.OnConnectionLostAsync(Identity.PlayerId, this);

            var outboundPump = StopOutboundQueue();
            if (outboundPump is not null)
            {
                try
                {
                    await outboundPump;
                }
                catch
                {
                    // The receive loop still owns final socket cleanup.
                }
            }
            await CloseSocketQuietlyAsync();
            _outboundLifetime.Dispose();
        }
    }

    public Task SendAsync(
        string type,
        string? requestId,
        object? data,
        CancellationToken cancellationToken,
        bool replacePendingSnapshot = false)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.CompletedTask;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new { type, requestId, data },
            ProtocolJson.Options);
        EnqueueOutbound(bytes, replacePendingSnapshot);
        return Task.CompletedTask;
    }

    private void EnqueueOutbound(
        ReadOnlyMemory<byte> bytes,
        bool replacePendingSnapshot)
    {
        var abortSlowPeer = false;
        lock (_outboundGate)
        {
            if (_outboundStopped) return;

            var pendingSnapshot = _pendingSnapshotFrame;
            var replacesExisting = replacePendingSnapshot &&
                                   pendingSnapshot is not null;
            var replacedBytes = replacesExisting
                ? pendingSnapshot!.Value.Bytes.Length
                : 0;
            var resultingCount = _outboundFrames.Count +
                                 (replacesExisting ? 0 : 1);
            var resultingBytes = _queuedBytes - replacedBytes + bytes.Length;
            if (resultingCount > MaximumQueuedFrames ||
                resultingBytes > MaximumQueuedBytes)
            {
                abortSlowPeer = true;
                StopOutboundQueueLocked();
            }
            else
            {
                if (_pendingSnapshotFrame is not null &&
                    replacePendingSnapshot)
                {
                    _queuedBytes -=
                        _pendingSnapshotFrame.Value.Bytes.Length;
                    _outboundFrames.Remove(_pendingSnapshotFrame);
                    _pendingSnapshotFrame = null;
                }

                var frame = new OutboundFrame(bytes, replacePendingSnapshot);
                var node = _outboundFrames.AddLast(frame);
                _queuedBytes += bytes.Length;
                if (replacePendingSnapshot)
                    _pendingSnapshotFrame = node;
                if (!_outboundPumpActive)
                {
                    _outboundPumpActive = true;
                    _outboundPump = FlushOutboundAsync();
                }
            }
        }

        if (!abortSlowPeer) return;
        _logger.LogWarning(
            "Disconnecting slow WebSocket {ConnectionId}: its bounded outbound queue filled.",
            ConnectionId);
        AbortSocket();
    }

    private async Task FlushOutboundAsync()
    {
        // EnqueueOutbound starts the pump while holding _outboundGate. Yield once
        // so the task can be stored before it attempts to take the same lock.
        await Task.Yield();
        while (true)
        {
            OutboundFrame frame;
            lock (_outboundGate)
            {
                if (_outboundStopped || _outboundFrames.First is null)
                {
                    _outboundPumpActive = false;
                    _outboundPump = null;
                    return;
                }

                var node = _outboundFrames.First;
                frame = node.Value;
                _outboundFrames.RemoveFirst();
                _queuedBytes -= frame.Bytes.Length;
                if (ReferenceEquals(node, _pendingSnapshotFrame))
                    _pendingSnapshotFrame = null;
            }

            try
            {
                using var sendCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        _outboundLifetime.Token);
                sendCancellation.CancelAfter(_sendTimeout);
                if (_socket.State != WebSocketState.Open)
                {
                    _ = StopOutboundQueue();
                    return;
                }
                await _socket.SendAsync(
                    frame.Bytes,
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    sendCancellation.Token);
            }
            catch (OperationCanceledException)
                when (!_outboundLifetime.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Disconnecting slow WebSocket {ConnectionId} after a {TimeoutSeconds}s send timeout.",
                    ConnectionId,
                    _sendTimeout.TotalSeconds);
                _ = StopOutboundQueue();
                AbortSocket();
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (WebSocketException)
            {
                _ = StopOutboundQueue();
                return;
            }
            catch (ObjectDisposedException)
            {
                _ = StopOutboundQueue();
                return;
            }
        }
    }

    private Task? StopOutboundQueue()
    {
        Task? pump;
        lock (_outboundGate)
        {
            pump = _outboundPump;
            StopOutboundQueueLocked();
        }
        return pump;
    }

    private void StopOutboundQueueLocked()
    {
        if (_outboundStopped) return;
        _outboundStopped = true;
        _pendingSnapshotFrame = null;
        _outboundFrames.Clear();
        _queuedBytes = 0;
        _outboundLifetime.Cancel();
    }

    private void AbortSocket()
    {
        try
        {
            _socket.Abort();
        }
        catch (ObjectDisposedException)
        {
            // The receive loop already completed cleanup.
        }
    }

    private async Task ProcessMessageAsync(
        ReadOnlyMemory<byte> utf8,
        CancellationToken cancellationToken)
    {
        string? requestId = null;
        try
        {
            using var document = JsonDocument.Parse(
                utf8,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32
                });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ProtocolException("INVALID_JSON", "The message must be an object.");

            if (!root.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                throw new ProtocolException(
                    "INVALID_REQUEST",
                    "Every message requires a string 'type'.");
            }

            var type = typeElement.GetString() ?? string.Empty;
            if (type.Length is 0 or > 64)
                throw new ProtocolException("INVALID_REQUEST", "Invalid message type.");

            if (root.TryGetProperty("requestId", out var requestElement) &&
                requestElement.ValueKind != JsonValueKind.Null)
            {
                if (requestElement.ValueKind != JsonValueKind.String)
                {
                    throw new ProtocolException(
                        "INVALID_REQUEST",
                        "'requestId' must be a string.");
                }

                requestId = requestElement.GetString();
                if (requestId?.Length > 64)
                {
                    throw new ProtocolException(
                        "INVALID_REQUEST",
                        "'requestId' may contain at most 64 characters.");
                }
            }

            var payload = root.TryGetProperty("payload", out var payloadElement)
                ? payloadElement
                : default;
            await DispatchAsync(type, requestId, payload, cancellationToken);
        }
        catch (JsonException)
        {
            await SendErrorAsync(
                requestId,
                "INVALID_JSON",
                "The message is not valid JSON.",
                cancellationToken);
        }
        catch (ProtocolException exception)
        {
            await SendErrorAsync(
                requestId,
                exception.Code,
                exception.Message,
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unhandled protocol error on connection {ConnectionId}.",
                ConnectionId);
            await SendErrorAsync(
                requestId,
                "SERVER_ERROR",
                "The server could not complete that request.",
                cancellationToken);
        }
    }

    private async Task DispatchAsync(
        string type,
        string? requestId,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        switch (type)
        {
            case "ping":
                await SendAsync(
                    "pong",
                    requestId,
                    new
                    {
                        serverTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        echo = payload
                    },
                    cancellationToken);
                return;

            case "signup":
                EnsureUnauthenticated();
                EnsureAuthAttemptAllowed();
                await AuthenticateAsync(
                    await _accounts.SignupAsync(
                        ProtocolJson.RequiredString(payload, "username", 20),
                        ProtocolJson.RequiredString(payload, "password", 128),
                        cancellationToken),
                    requestId,
                    cancellationToken);
                return;

            case "login":
                EnsureUnauthenticated();
                EnsureAuthAttemptAllowed();
                await AuthenticateAsync(
                    await _accounts.LoginAsync(
                        ProtocolJson.RequiredString(payload, "username", 20),
                        ProtocolJson.RequiredString(payload, "password", 128),
                        cancellationToken),
                    requestId,
                    cancellationToken);
                return;

            case "resume":
                EnsureUnauthenticated();
                EnsureAuthAttemptAllowed();
                var resume = _connections.Resume(
                    ProtocolJson.RequiredString(payload, "token", 128),
                    this);
                await AuthenticationSucceededAsync(
                    resume,
                    requestId,
                    cancellationToken);
                return;
        }

        if (Identity is null)
        {
            throw new ProtocolException(
                "AUTH_REQUIRED",
                "Sign up, log in, or resume a session first.");
        }

        switch (type)
        {
            case "lobby.list":
            case "lobby.search":
                await _lobbies.ListAsync(
                    this,
                    requestId,
                    ProtocolJson.OptionalString(payload, "search", 40),
                    cancellationToken);
                break;

            case "lobby.create":
                await _lobbies.CreateAsync(this, requestId, payload, cancellationToken);
                break;

            case "lobby.join":
                await _lobbies.JoinAsync(
                    this,
                    requestId,
                    ProtocolJson.RequiredString(payload, "lobbyId", 16),
                    cancellationToken);
                break;

            case "lobby.leave":
                await _lobbies.LeaveAsync(this, requestId, cancellationToken);
                break;

            case "lobby.settings":
                await _lobbies.UpdateSettingsAsync(
                    this,
                    requestId,
                    ProtocolJson.RequiredObject(payload, "settings"),
                    cancellationToken);
                break;

            case "lobby.start":
                await _lobbies.StartAsync(this, requestId, cancellationToken);
                break;

            case "lobby.finish":
                await _lobbies.FinishAsync(
                    this,
                    requestId,
                    ProtocolJson.RequiredBoolean(payload, "completed"),
                    ProtocolJson.OptionalBoolean(payload, "difficultyPenalty"),
                    cancellationToken);
                break;

            case "game.input":
                EnsureGameRate(_inputLimiter, "input");
                await RelayAsync(
                    "input",
                    requestId,
                    payload,
                    cancellationToken);
                break;

            case "game.snapshot":
                EnsureGameRate(_snapshotLimiter, "snapshot");
                await RelayAsync(
                    "snapshot",
                    requestId,
                    payload,
                    cancellationToken);
                break;

            default:
                throw new ProtocolException(
                    "UNKNOWN_TYPE",
                    $"Unknown message type '{type}'.");
        }
    }

    private async Task AuthenticateAsync(
        AccountIdentity identity,
        string? requestId,
        CancellationToken cancellationToken)
    {
        var result = _connections.Authenticate(identity, this);
        await AuthenticationSucceededAsync(result, requestId, cancellationToken);
    }

    private async Task AuthenticationSucceededAsync(
        AuthenticationResult result,
        string? requestId,
        CancellationToken cancellationToken)
    {
        await SendAsync(
            "auth.ok",
            requestId,
            new
            {
                playerId = result.Identity.PlayerId,
                username = result.Identity.Username,
                resumeToken = result.ResumeToken,
                resumeExpiresAtUtc = result.ExpiresAtUtc
            },
            cancellationToken);
        result.ReplacedSession?.RetireForResume();
        await _lobbies.ReattachAsync(
            result.Identity.PlayerId,
            this,
            cancellationToken);
    }

    internal void RetireForResume()
    {
        try
        {
            _socket.Abort();
        }
        catch (ObjectDisposedException)
        {
            // The previous transport already noticed its disconnect.
        }
    }

    private async Task RelayAsync(
        string kind,
        string? requestId,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var clientSequence = ProtocolJson.RequiredNonNegativeInt64(
            payload,
            "clientSequence");
        long? authorityEpoch = kind == "snapshot"
            ? ProtocolJson.RequiredNonNegativeInt64(payload, "authorityEpoch")
            : null;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("payload", out var gamePayload))
        {
            throw new ProtocolException(
                "INVALID_REQUEST",
                "'payload' must include a nested gameplay payload.");
        }

        await _lobbies.RelayGameEventAsync(
            this,
            requestId,
            kind,
            clientSequence,
            authorityEpoch,
            gamePayload,
            cancellationToken);
    }

    private void EnsureAuthAttemptAllowed()
    {
        var now = Environment.TickCount64;
        while (_authAttempts.Count > 0 && now - _authAttempts.Peek() >= 60_000)
            _authAttempts.Dequeue();
        if (_authAttempts.Count >= 5)
        {
            throw new ProtocolException(
                "RATE_LIMITED",
                "Too many authentication attempts; wait one minute.");
        }

        _authAttempts.Enqueue(now);
    }

    private void EnsureUnauthenticated()
    {
        if (Identity is not null)
        {
            throw new ProtocolException(
                "ALREADY_AUTHENTICATED",
                "This connection is already authenticated.");
        }
    }

    private static void EnsureGameRate(TokenBucket limiter, string kind)
    {
        if (!limiter.TryTake())
        {
            throw new ProtocolException(
                "RATE_LIMITED",
                $"Too many {kind} messages.");
        }
    }

    private async Task SendErrorAsync(
        string? requestId,
        string code,
        string message,
        CancellationToken cancellationToken) =>
        await SendAsync(
            "error",
            requestId,
            new { code, message },
            cancellationToken);

    private async Task<ReadOnlyMemory<byte>?> ReceiveTextMessageAsync(
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8 * 1024];
        using var message = new MemoryStream();
        WebSocketMessageType? messageType = null;

        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            messageType ??= result.MessageType;
            if (messageType != WebSocketMessageType.Text ||
                result.MessageType != messageType)
            {
                await CloseSocketBoundedAsync(
                    WebSocketCloseStatus.InvalidMessageType,
                    "JSON text messages are required.");
                return null;
            }

            if (message.Length + result.Count > _maxMessageBytes)
            {
                await CloseSocketBoundedAsync(
                    WebSocketCloseStatus.MessageTooBig,
                    "Message exceeds the server limit.");
                return null;
            }

            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return message.ToArray();
        }
    }

    private async Task CloseSocketQuietlyAsync()
    {
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await CloseSocketBoundedAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Connection closed.");
        }
        finally
        {
            _socket.Dispose();
        }
    }

    private async Task CloseSocketBoundedAsync(
        WebSocketCloseStatus status,
        string description)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await _socket.CloseAsync(status, description, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                _socket.Abort();
            }
            catch (ObjectDisposedException)
            {
                // Another cleanup path won the timeout race.
            }
        }
        catch (WebSocketException)
        {
            // Peer is already gone.
        }
        catch (ObjectDisposedException)
        {
            // Another cleanup path won the race.
        }
    }
}
