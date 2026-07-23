using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dust.OnlineServer.Configuration;
using Dust.OnlineServer.Networking;
using Dust.OnlineServer.Protocol;
using Microsoft.Extensions.Options;

namespace Dust.OnlineServer.Lobbies;

internal sealed class LobbyManager
{
    private const int MaximumRunLevel = 1000;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Lobby> _lobbies =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, string> _playerLobbies = [];
    private readonly ConnectionHub _connections;
    private readonly TimeSpan _disconnectGrace;
    private readonly int _maxSnapshotPayloadBytes;
    private readonly int _maxInputPayloadBytes;
    private readonly ILogger<LobbyManager> _logger;
    private long _directoryRevision;
    private int _lobbyCount;
    private int _openLobbyCount;

    public LobbyManager(
        ConnectionHub connections,
        IOptions<OnlineServerOptions> options,
        ILogger<LobbyManager> logger)
    {
        _connections = connections;
        _logger = logger;
        _disconnectGrace = TimeSpan.FromSeconds(
            Math.Clamp(options.Value.DisconnectGraceSeconds, 5, 120));
        _maxSnapshotPayloadBytes = Math.Clamp(
            options.Value.MaxSnapshotPayloadBytes,
            1024,
            64 * 1024);
        _maxInputPayloadBytes = Math.Clamp(
            options.Value.MaxInputPayloadBytes,
            1024,
            32 * 1024);
    }

    public int LobbyCount => Volatile.Read(ref _lobbyCount);
    public int OpenLobbyCount => Volatile.Read(ref _openLobbyCount);

    public async Task ListAsync(
        ClientSession peer,
        string? requestId,
        string? search,
        CancellationToken cancellationToken)
    {
        var normalizedSearch = (search ?? string.Empty).Trim();
        if (normalizedSearch.Length > 40)
        {
            throw new ProtocolException(
                "INVALID_SEARCH",
                "Lobby searches may contain at most 40 characters.");
        }

        object[] summaries;
        long revision;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            summaries = BuildLobbySummariesLocked(normalizedSearch);
            revision = _directoryRevision;
        }
        finally
        {
            _gate.Release();
        }

        await peer.SendAsync(
            "lobby.list",
            requestId,
            new { revision, lobbies = summaries },
            cancellationToken);
    }

    public async Task CreateAsync(
        ClientSession peer,
        string? requestId,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var identity = RequiredIdentity(peer);
        var name = ProtocolJson.OptionalString(payload, "name", 32)?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = $"{identity.Username}'s lobby";
        if (name.Length is < 3 or > 32)
        {
            throw new ProtocolException(
                "INVALID_LOBBY_NAME",
                "Lobby names must contain 3-32 characters.");
        }

        var maxPlayers = 4;
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("maxPlayers", out var maxPlayersElement))
        {
            if (!maxPlayersElement.TryGetInt32(out maxPlayers) ||
                maxPlayers is < 2 or > 4)
            {
                throw new ProtocolException(
                    "INVALID_MAX_PLAYERS",
                    "maxPlayers must be an integer from 2 through 4.");
            }
        }

        var settingsElement = default(JsonElement);
        if (payload.ValueKind == JsonValueKind.Object)
            payload.TryGetProperty("settings", out settingsElement);
        var settings = RunSettings.Parse(settingsElement);

        Lobby lobby;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureCurrentSessionLocked(identity.PlayerId, peer);
            EnsureNotInLobbyLocked(identity.PlayerId);
            var lobbyId = NewLobbyIdLocked();
            lobby = new Lobby
            {
                LobbyId = lobbyId,
                Name = name,
                HostPlayerId = identity.PlayerId,
                MaxPlayers = maxPlayers,
                Settings = settings
            };
            lobby.Members.Add(new LobbyMember
            {
                PlayerId = identity.PlayerId,
                Username = identity.Username,
                JoinOrder = 0,
                Peer = peer
            });
            _lobbies.Add(lobbyId, lobby);
            _playerLobbies.Add(identity.PlayerId, lobbyId);
            DirectoryChangedLocked();
        }
        finally
        {
            _gate.Release();
        }

        await BroadcastLobbyStateAsync(lobby, peer, requestId, CancellationToken.None);
        await PushLobbyRefreshAsync(CancellationToken.None);
    }

    public async Task JoinAsync(
        ClientSession peer,
        string? requestId,
        string lobbyId,
        CancellationToken cancellationToken)
    {
        var identity = RequiredIdentity(peer);
        Lobby lobby;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureCurrentSessionLocked(identity.PlayerId, peer);
            EnsureNotInLobbyLocked(identity.PlayerId);
            if (!_lobbies.TryGetValue(lobbyId, out lobby!))
                throw new ProtocolException("LOBBY_NOT_FOUND", "That lobby no longer exists.");
            if (lobby.Status != LobbyStatus.Waiting)
                throw new ProtocolException("LOBBY_IN_GAME", "That lobby has already started.");
            if (lobby.Members.Count >= lobby.MaxPlayers)
                throw new ProtocolException("LOBBY_FULL", "That lobby is full.");

            var joinOrder = lobby.Members.Count == 0
                ? 0
                : lobby.Members.Max(member => member.JoinOrder) + 1;
            lobby.Members.Add(new LobbyMember
            {
                PlayerId = identity.PlayerId,
                Username = identity.Username,
                JoinOrder = joinOrder,
                Peer = peer
            });
            _playerLobbies.Add(identity.PlayerId, lobby.LobbyId);
            lobby.Revision++;
            DirectoryChangedLocked();
        }
        finally
        {
            _gate.Release();
        }

        await BroadcastLobbyStateAsync(lobby, peer, requestId, CancellationToken.None);
        await PushLobbyRefreshAsync(CancellationToken.None);
    }

    public async Task LeaveAsync(
        ClientSession peer,
        string? requestId,
        CancellationToken cancellationToken)
    {
        var identity = RequiredIdentity(peer);
        Lobby? serializedLobby = null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_playerLobbies.ContainsKey(identity.PlayerId))
                serializedLobby = RequiredLobbyLocked(identity.PlayerId, peer);
        }
        finally
        {
            _gate.Release();
        }

        RemovalResult removal;
        if (serializedLobby is null)
        {
            removal = new RemovalResult(null, null, null, false);
        }
        else
        {
            await serializedLobby.RelayGate.WaitAsync(cancellationToken);
            try
            {
                await _gate.WaitAsync(cancellationToken);
                try
                {
                    _ = RequiredLobbyLocked(identity.PlayerId, peer);
                    removal = RemoveMemberLocked(identity.PlayerId);
                }
                finally
                {
                    _gate.Release();
                }

                await peer.SendAsync(
                    "lobby.left",
                    requestId,
                    new { lobbyId = removal.LobbyId },
                    CancellationToken.None);
                if (removal.RemainingLobby is not null)
                {
                    await BroadcastLobbyStateAsync(
                        removal.RemainingLobby,
                        null,
                        null,
                        CancellationToken.None);
                }
                if (removal.NewHostPeer is not null)
                {
                    await SendCheckpointAsync(
                        removal.RemainingLobby!,
                        removal.NewHostPeer,
                        CancellationToken.None);
                }
            }
            finally
            {
                serializedLobby.RelayGate.Release();
            }
        }

        if (serializedLobby is null)
        {
            await peer.SendAsync(
                "lobby.left",
                requestId,
                new { lobbyId = (string?)null },
                CancellationToken.None);
        }
        if (removal.LobbyId is not null)
            await PushLobbyRefreshAsync(CancellationToken.None);
    }

    public async Task UpdateSettingsAsync(
        ClientSession peer,
        string? requestId,
        JsonElement settingsElement,
        CancellationToken cancellationToken)
    {
        var identity = RequiredIdentity(peer);
        var settings = RunSettings.Parse(settingsElement);
        Lobby lobby;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            lobby = RequiredLobbyLocked(identity.PlayerId, peer);
            EnsureHostLocked(lobby, identity.PlayerId);
            if (lobby.Status != LobbyStatus.Waiting)
            {
                throw new ProtocolException(
                    "LOBBY_IN_GAME",
                    "Run settings cannot be changed during a game.");
            }

            if (!EquivalentSettings(lobby.Settings, settings))
                lobby.RunLevel = 1;
            lobby.Settings = settings;
            lobby.Revision++;
            DirectoryChangedLocked();
        }
        finally
        {
            _gate.Release();
        }

        await BroadcastLobbyStateAsync(lobby, peer, requestId, CancellationToken.None);
        await PushLobbyRefreshAsync(CancellationToken.None);
    }

    public async Task StartAsync(
        ClientSession peer,
        string? requestId,
        CancellationToken cancellationToken)
    {
        var identity = RequiredIdentity(peer);
        Lobby lobby;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            lobby = RequiredLobbyLocked(identity.PlayerId, peer);
        }
        finally
        {
            _gate.Release();
        }

        await lobby.RelayGate.WaitAsync(cancellationToken);
        try
        {
            object startedData;
            IReadOnlyList<ClientSession> recipients;
            await _gate.WaitAsync(cancellationToken);
            try
            {
                lobby = RequiredLobbyLocked(identity.PlayerId, peer);
                EnsureHostLocked(lobby, identity.PlayerId);
                if (lobby.Status != LobbyStatus.Waiting)
                {
                    throw new ProtocolException(
                        "ALREADY_STARTED",
                        "This lobby is already in a run.");
                }

                lobby.Status = LobbyStatus.InGame;
                lobby.Seed = RandomSeed();
                lobby.ServerSequence = 0;
                lobby.AuthorityEpoch++;
                lobby.LatestSnapshot = null;
                foreach (var member in lobby.Members)
                {
                    member.LastInputClientSequence = -1;
                    member.LastSnapshotClientSequence = -1;
                }
                lobby.Revision++;
                DirectoryChangedLocked();
                startedData = BuildStartedDataLocked(lobby);
                recipients = ConnectedPeers(lobby);
            }
            finally
            {
                _gate.Release();
            }

            await BroadcastAsync(
                recipients,
                "lobby.started",
                startedData,
                peer,
                requestId,
                CancellationToken.None);
        }
        finally
        {
            lobby.RelayGate.Release();
        }
        await PushLobbyRefreshAsync(CancellationToken.None);
    }

    public async Task FinishAsync(
        ClientSession peer,
        string? requestId,
        bool completed,
        bool difficultyPenalty,
        CancellationToken cancellationToken)
    {
        var identity = RequiredIdentity(peer);
        Lobby lobby;

        // Finish is serialized behind all relayed gameplay events so clients never
        // receive an event from the old run after the waiting-room state.
        await _gate.WaitAsync(cancellationToken);
        try
        {
            lobby = RequiredLobbyLocked(identity.PlayerId, peer);
        }
        finally
        {
            _gate.Release();
        }

        await lobby.RelayGate.WaitAsync(cancellationToken);
        try
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                lobby = RequiredLobbyLocked(identity.PlayerId, peer);
                EnsureHostLocked(lobby, identity.PlayerId);
                if (lobby.Status != LobbyStatus.InGame)
                {
                    throw new ProtocolException(
                        "NOT_IN_GAME",
                        "This lobby does not have an active run.");
                }

                if (completed && lobby.Settings.DifficultyScaling)
                    lobby.RunLevel = Math.Min(
                        MaximumRunLevel,
                        lobby.RunLevel + 1 + (difficultyPenalty ? 1 : 0));
                lobby.Status = LobbyStatus.Waiting;
                lobby.Seed = null;
                lobby.ServerSequence = 0;
                lobby.LatestSnapshot = null;
                lobby.Revision++;
                DirectoryChangedLocked();
            }
            finally
            {
                _gate.Release();
            }

            await BroadcastLobbyStateAsync(
                lobby, peer, requestId, CancellationToken.None);
        }
        finally
        {
            lobby.RelayGate.Release();
        }

        await PushLobbyRefreshAsync(CancellationToken.None);
    }

    public async Task RelayGameEventAsync(
        ClientSession peer,
        string? requestId,
        string kind,
        long clientSequence,
        long? submittedAuthorityEpoch,
        JsonElement eventPayload,
        CancellationToken cancellationToken)
    {
        var identity = RequiredIdentity(peer);
        var rawBytes = Encoding.UTF8.GetByteCount(eventPayload.GetRawText());
        var limit = kind == "snapshot"
            ? _maxSnapshotPayloadBytes
            : _maxInputPayloadBytes;
        if (rawBytes > limit)
        {
            throw new ProtocolException(
                "PAYLOAD_TOO_LARGE",
                $"{kind} payloads may contain at most {limit} UTF-8 bytes.");
        }

        Lobby lobby;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            lobby = RequiredLobbyLocked(identity.PlayerId, peer);
        }
        finally
        {
            _gate.Release();
        }

        await lobby.RelayGate.WaitAsync(cancellationToken);
        try
        {
            object eventData;
            IReadOnlyList<ClientSession> recipients;
            await _gate.WaitAsync(cancellationToken);
            try
            {
                lobby = RequiredLobbyLocked(identity.PlayerId, peer);
                if (lobby.Status != LobbyStatus.InGame)
                    throw new ProtocolException("NOT_IN_GAME", "The lobby has not started.");
                if (kind == "input" &&
                    lobby.Members.First(
                        member => member.PlayerId == lobby.HostPlayerId).Peer is null)
                {
                    throw new ProtocolException(
                        "HOST_UNAVAILABLE",
                        "Input is paused while the host is reconnecting.");
                }
                if (kind == "snapshot" && lobby.HostPlayerId != identity.PlayerId)
                {
                    throw new ProtocolException(
                        "HOST_ONLY",
                        "Only the host may send authoritative snapshots.");
                }
                if (kind == "snapshot" &&
                    submittedAuthorityEpoch != lobby.AuthorityEpoch)
                {
                    throw new ProtocolException(
                        "STALE_AUTHORITY",
                        "Snapshot authorityEpoch does not match the current host epoch.");
                }

                var member = lobby.Members.First(
                    candidate => candidate.PlayerId == identity.PlayerId);
                var lastClientSequence = kind == "snapshot"
                    ? member.LastSnapshotClientSequence
                    : member.LastInputClientSequence;
                if (clientSequence <= lastClientSequence)
                {
                    throw new ProtocolException(
                        "OUT_OF_ORDER_SEQUENCE",
                        $"{kind} clientSequence must increase for each accepted message.");
                }
                if (kind == "snapshot")
                    member.LastSnapshotClientSequence = clientSequence;
                else
                    member.LastInputClientSequence = clientSequence;

                var serverSequence = ++lobby.ServerSequence;
                var serverTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (kind == "snapshot")
                {
                    lobby.LatestSnapshot = new CachedSnapshot(
                        identity.PlayerId,
                        clientSequence,
                        serverSequence,
                        lobby.AuthorityEpoch,
                        serverTimeUnixMs,
                        eventPayload.Clone());
                }
                recipients = ConnectedPeers(lobby);
                eventData = new
                {
                    lobbyId = lobby.LobbyId,
                    kind,
                    senderPlayerId = identity.PlayerId,
                    clientSequence,
                    serverSequence,
                    authorityEpoch = lobby.AuthorityEpoch,
                    serverTimeUnixMs,
                    payload = eventPayload
                };
            }
            finally
            {
                _gate.Release();
            }

            // The sender is deliberately included. Its echoed event is the
            // canonical acknowledgement and provides the total server ordering.
            await BroadcastAsync(
                recipients,
                "game.event",
                eventData,
                peer,
                requestId,
                CancellationToken.None);
        }
        finally
        {
            lobby.RelayGate.Release();
        }
    }

    public async Task OnConnectionLostAsync(
        Guid playerId,
        ClientSession disconnectedPeer)
    {
        Lobby? lobby = null;
        CancellationTokenSource? eviction = null;

        await _gate.WaitAsync();
        try
        {
            if (!_playerLobbies.TryGetValue(playerId, out var lobbyId) ||
                !_lobbies.TryGetValue(lobbyId, out lobby))
                return;

            var member = lobby.Members.FirstOrDefault(item => item.PlayerId == playerId);
            if (member is null || !ReferenceEquals(member.Peer, disconnectedPeer))
                return;

            member.Peer = null;
            member.DisconnectEviction?.Cancel();
            member.DisconnectEviction?.Dispose();
            eviction = new CancellationTokenSource();
            member.DisconnectEviction = eviction;
            lobby.Revision++;
            DirectoryChangedLocked();
        }
        finally
        {
            _gate.Release();
        }

        if (lobby is not null)
            await BroadcastLobbyStateAsync(lobby, null, null, CancellationToken.None);
        if (lobby is not null)
            await PushLobbyRefreshAsync(CancellationToken.None);
        if (eviction is not null && lobby is not null)
            _ = EvictAfterGraceAsync(lobby.LobbyId, playerId, eviction);
    }

    public async Task ReattachAsync(
        Guid playerId,
        ClientSession peer,
        CancellationToken cancellationToken)
    {
        Lobby? lobby;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_playerLobbies.TryGetValue(playerId, out var lobbyId) ||
                !_lobbies.TryGetValue(lobbyId, out lobby))
                return;
        }
        finally
        {
            _gate.Release();
        }

        // Keep reattachment state and its recovery checkpoint in the same
        // relay order as gameplay. A concurrent snapshot must not overtake an
        // older checkpoint on the newly attached socket.
        await lobby.RelayGate.WaitAsync(cancellationToken);
        try
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (!_playerLobbies.TryGetValue(playerId, out var lobbyId) ||
                    !_lobbies.TryGetValue(lobbyId, out var current) ||
                    !ReferenceEquals(current, lobby))
                    return;

                var member = lobby.Members.FirstOrDefault(item =>
                    item.PlayerId == playerId);
                if (member is null)
                    return;

                member.DisconnectEviction?.Cancel();
                member.DisconnectEviction?.Dispose();
                member.DisconnectEviction = null;
                member.Peer = peer;
                lobby.Revision++;
                DirectoryChangedLocked();
            }
            finally
            {
                _gate.Release();
            }

            await BroadcastLobbyStateAsync(
                lobby, peer, null, CancellationToken.None);
            await SendCheckpointAsync(lobby, peer, CancellationToken.None);
        }
        finally
        {
            lobby.RelayGate.Release();
        }

        await PushLobbyRefreshAsync(CancellationToken.None);
    }

    private async Task EvictAfterGraceAsync(
        string lobbyId,
        Guid playerId,
        CancellationTokenSource eviction)
    {
        try
        {
            await Task.Delay(_disconnectGrace, eviction.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        RemovalResult removal = new(null, null, null, false);
        var removed = false;

        Lobby? serializedLobby;
        await _gate.WaitAsync();
        try
        {
            if (!_lobbies.TryGetValue(lobbyId, out serializedLobby))
                return;
        }
        finally
        {
            _gate.Release();
        }

        await serializedLobby.RelayGate.WaitAsync();
        try
        {
            await _gate.WaitAsync();
            try
            {
                if (!_lobbies.TryGetValue(lobbyId, out var current) ||
                    !ReferenceEquals(current, serializedLobby))
                    return;
                var member = current.Members.FirstOrDefault(
                    item => item.PlayerId == playerId);
                if (member is null ||
                    member.Peer is not null ||
                    !ReferenceEquals(member.DisconnectEviction, eviction))
                    return;

                removal = RemoveMemberLocked(playerId);
                removed = true;
            }
            finally
            {
                _gate.Release();
            }

            if (!removed)
                return;

            _logger.LogInformation(
                "Player {PlayerId} left lobby {LobbyId} after disconnect grace expired.",
                playerId,
                lobbyId);
            if (removal.RemainingLobby is not null)
            {
                await BroadcastLobbyStateAsync(
                    removal.RemainingLobby,
                    null,
                    null,
                    CancellationToken.None);
            }
            if (removal.NewHostPeer is not null)
            {
                await SendCheckpointAsync(
                    removal.RemainingLobby!,
                    removal.NewHostPeer,
                    CancellationToken.None);
            }
        }
        finally
        {
            serializedLobby.RelayGate.Release();
            eviction.Dispose();
        }

        if (!removed)
            return;

        await PushLobbyRefreshAsync(CancellationToken.None);
    }

    private async Task SendCheckpointAsync(
        Lobby lobby,
        ClientSession peer,
        CancellationToken cancellationToken)
    {
        object? checkpoint = null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_lobbies.TryGetValue(lobby.LobbyId, out var current) ||
                !ReferenceEquals(current, lobby) ||
                current.Status != LobbyStatus.InGame ||
                !current.Members.Any(member => ReferenceEquals(member.Peer, peer)))
                return;

            var latest = current.LatestSnapshot;
            checkpoint = latest is null
                ? new
                {
                    lobbyId = current.LobbyId,
                    available = false,
                    authorityEpoch = current.AuthorityEpoch,
                    serverSequence = current.ServerSequence
                }
                : (object)new
                {
                    lobbyId = current.LobbyId,
                    available = true,
                    kind = "snapshot",
                    checkpoint = true,
                    senderPlayerId = latest.SenderPlayerId,
                    clientSequence = latest.ClientSequence,
                    serverSequence = latest.ServerSequence,
                    authorityEpoch = current.AuthorityEpoch,
                    sourceAuthorityEpoch = latest.AuthorityEpoch,
                    serverTimeUnixMs = latest.ServerTimeUnixMs,
                    payload = latest.Payload
                };
        }
        finally
        {
            _gate.Release();
        }

        await peer.SendAsync(
            "game.checkpoint",
            null,
            checkpoint,
            cancellationToken);
    }

    private RemovalResult RemoveMemberLocked(Guid playerId)
    {
        if (!_playerLobbies.Remove(playerId, out var lobbyId) ||
            !_lobbies.TryGetValue(lobbyId, out var lobby))
            return new RemovalResult(null, null, null, false);

        var member = lobby.Members.FirstOrDefault(item => item.PlayerId == playerId);
        if (member is null)
            return new RemovalResult(lobby, lobbyId, null, false);

        member.DisconnectEviction?.Cancel();
        member.DisconnectEviction?.Dispose();
        lobby.Members.Remove(member);

        if (lobby.Members.Count == 0)
        {
            _lobbies.Remove(lobbyId);
            DirectoryChangedLocked();
            return new RemovalResult(null, lobbyId, null, false);
        }

        var hostMigrated = false;
        if (lobby.HostPlayerId == playerId)
        {
            lobby.HostPlayerId = lobby.Members
                .OrderBy(candidate => candidate.Peer is null ? 1 : 0)
                .ThenBy(candidate => candidate.JoinOrder)
                .First()
                .PlayerId;
            lobby.AuthorityEpoch++;
            hostMigrated = true;
        }

        lobby.Revision++;
        DirectoryChangedLocked();
        var newHostPeer = hostMigrated
            ? lobby.Members.First(member => member.PlayerId == lobby.HostPlayerId).Peer
            : null;
        return new RemovalResult(lobby, lobbyId, newHostPeer, hostMigrated);
    }

    private async Task PushLobbyRefreshAsync(CancellationToken cancellationToken)
    {
        object[] summaries;
        long revision;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            summaries = BuildLobbySummariesLocked(string.Empty);
            revision = _directoryRevision;
        }
        finally
        {
            _gate.Release();
        }

        await BroadcastAsync(
            _connections.AuthenticatedConnections(),
            "lobby.refresh",
            new { revision, lobbies = summaries },
            null,
            null,
            cancellationToken);
    }

    private async Task BroadcastLobbyStateAsync(
        Lobby lobby,
        ClientSession? requester,
        string? requestId,
        CancellationToken cancellationToken)
    {
        object state;
        IReadOnlyList<ClientSession> peers;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // A final disconnect can delete a lobby before a queued broadcast.
            if (!_lobbies.TryGetValue(lobby.LobbyId, out var current) ||
                !ReferenceEquals(current, lobby))
                return;
            state = BuildStateLocked(lobby);
            peers = ConnectedPeers(lobby);
        }
        finally
        {
            _gate.Release();
        }

        await BroadcastAsync(
            peers,
            "lobby.state",
            state,
            requester,
            requestId,
            cancellationToken);
    }

    private static async Task BroadcastAsync(
        IReadOnlyList<ClientSession> peers,
        string type,
        object data,
        ClientSession? requester,
        string? requestId,
        CancellationToken cancellationToken)
    {
        await Task.WhenAll(peers.Select(peer => peer.SendAsync(
            type,
            ReferenceEquals(peer, requester) ? requestId : null,
            data,
            cancellationToken)));
    }

    private object[] BuildLobbySummariesLocked(string search)
    {
        return _lobbies.Values
            .Where(lobby =>
                lobby.Status == LobbyStatus.Waiting &&
                lobby.Members.Count < lobby.MaxPlayers)
            .Where(lobby =>
                search.Length == 0 ||
                lobby.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                HostUsername(lobby).Contains(search, StringComparison.OrdinalIgnoreCase) ||
                lobby.LobbyId.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(lobby => lobby.Name, StringComparer.OrdinalIgnoreCase)
            .Select(lobby => (object)new
            {
                lobbyId = lobby.LobbyId,
                name = lobby.Name,
                hostUsername = HostUsername(lobby),
                playerCount = lobby.Members.Count,
                connectedPlayerCount = lobby.Members.Count(member => member.Peer is not null),
                maxPlayers = lobby.MaxPlayers,
                authorityEpoch = lobby.AuthorityEpoch,
                settings = lobby.Settings
            })
            .ToArray();
    }

    private static object BuildStateLocked(Lobby lobby) => new
    {
        lobbyId = lobby.LobbyId,
        name = lobby.Name,
        revision = lobby.Revision,
        status = StatusName(lobby.Status),
        hostPlayerId = lobby.HostPlayerId,
        authorityEpoch = lobby.AuthorityEpoch,
        maxPlayers = lobby.MaxPlayers,
        seed = lobby.Seed,
        runLevel = lobby.RunLevel,
        settings = lobby.Settings,
        players = BuildPlayers(lobby)
    };

    private static object BuildStartedDataLocked(Lobby lobby) => new
    {
        lobbyId = lobby.LobbyId,
        revision = lobby.Revision,
        status = StatusName(lobby.Status),
        seed = lobby.Seed,
        hostPlayerId = lobby.HostPlayerId,
        authorityEpoch = lobby.AuthorityEpoch,
        runLevel = lobby.RunLevel,
        settings = lobby.Settings,
        players = BuildPlayers(lobby)
    };

    private static object[] BuildPlayers(Lobby lobby) =>
        lobby.Members
            .OrderBy(member => member.JoinOrder)
            .Select(member => (object)new
            {
                playerId = member.PlayerId,
                username = member.Username,
                joinOrder = member.JoinOrder,
                connected = member.Peer is not null
            })
            .ToArray();

    private static bool EquivalentSettings(RunSettings left, RunSettings right) =>
        string.Equals(left.MapSize, right.MapSize, StringComparison.Ordinal) &&
        string.Equals(left.MazeStrictness, right.MazeStrictness, StringComparison.Ordinal) &&
        string.Equals(left.HollowAmount, right.HollowAmount, StringComparison.Ordinal) &&
        left.DifficultyScaling == right.DifficultyScaling &&
        left.HollowTypes.Count == right.HollowTypes.Count &&
        left.HollowTypes.All(type =>
            right.HollowTypes.Contains(type, StringComparer.Ordinal));

    private static IReadOnlyList<ClientSession> ConnectedPeers(Lobby lobby) =>
        lobby.Members
            .Select(member => member.Peer)
            .Where(peer => peer is not null)
            .Cast<ClientSession>()
            .ToArray();

    private static string StatusName(LobbyStatus status) =>
        status == LobbyStatus.Waiting ? "waiting" : "inGame";

    private static string HostUsername(Lobby lobby) =>
        lobby.Members.First(member => member.PlayerId == lobby.HostPlayerId).Username;

    private static long RandomSeed()
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToInt64(bytes) & long.MaxValue;
    }

    private Lobby RequiredLobbyLocked(Guid playerId, ClientSession peer)
    {
        EnsureCurrentSessionLocked(playerId, peer);
        if (!_playerLobbies.TryGetValue(playerId, out var lobbyId) ||
            !_lobbies.TryGetValue(lobbyId, out var lobby))
        {
            throw new ProtocolException("NOT_IN_LOBBY", "Join or create a lobby first.");
        }

        var member = lobby.Members.FirstOrDefault(item => item.PlayerId == playerId);
        if (member is null || !ReferenceEquals(member.Peer, peer))
        {
            throw new ProtocolException(
                "SESSION_REPLACED",
                "This connection is no longer the active lobby session.");
        }
        return lobby;
    }

    private void EnsureCurrentSessionLocked(Guid playerId, ClientSession peer)
    {
        if (!_connections.IsCurrentSession(playerId, peer))
        {
            throw new ProtocolException(
                "SESSION_REPLACED",
                "This connection has been replaced by a resumed session.");
        }
    }

    private static void EnsureHostLocked(Lobby lobby, Guid playerId)
    {
        if (lobby.HostPlayerId != playerId)
            throw new ProtocolException("HOST_ONLY", "Only the lobby host may do that.");
    }

    private void EnsureNotInLobbyLocked(Guid playerId)
    {
        if (_playerLobbies.ContainsKey(playerId))
        {
            throw new ProtocolException(
                "ALREADY_IN_LOBBY",
                "Leave the current lobby before joining another.");
        }
    }

    private static Security.AccountIdentity RequiredIdentity(ClientSession peer) =>
        peer.Identity ?? throw new ProtocolException(
            "AUTH_REQUIRED",
            "Sign up, log in, or resume a session first.");

    private string NewLobbyIdLocked()
    {
        string id;
        do
        {
            id = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        } while (_lobbies.ContainsKey(id));

        return id;
    }

    private void DirectoryChangedLocked()
    {
        _directoryRevision++;
        Volatile.Write(ref _lobbyCount, _lobbies.Count);
        Volatile.Write(
            ref _openLobbyCount,
            _lobbies.Values.Count(lobby =>
                lobby.Status == LobbyStatus.Waiting &&
                lobby.Members.Count < lobby.MaxPlayers));
    }

    private sealed record RemovalResult(
        Lobby? RemainingLobby,
        string? LobbyId,
        ClientSession? NewHostPeer,
        bool HostMigrated);
}
