using Dust.OnlineServer.Networking;

namespace Dust.OnlineServer.Lobbies;

internal enum LobbyStatus
{
    Waiting,
    InGame
}

internal sealed class Lobby
{
    public required string LobbyId { get; init; }
    public required string Name { get; init; }
    public required Guid HostPlayerId { get; set; }
    public required int MaxPlayers { get; init; }
    public required RunSettings Settings { get; set; }
    public LobbyStatus Status { get; set; } = LobbyStatus.Waiting;
    public long Revision { get; set; } = 1;
    public long? Seed { get; set; }
    public int RunLevel { get; set; } = 1;
    public long ServerSequence { get; set; }
    public long AuthorityEpoch { get; set; }
    public CachedSnapshot? LatestSnapshot { get; set; }
    public List<LobbyMember> Members { get; } = [];
    public List<LobbyRunPlayer> RunStartPlayers { get; } = [];
    public SemaphoreSlim RelayGate { get; } = new(1, 1);
}

internal sealed class LobbyMember
{
    public required Guid PlayerId { get; init; }
    public required string Username { get; init; }
    public required int JoinOrder { get; init; }
    public ClientSession? Peer { get; set; }
    public CancellationTokenSource? DisconnectEviction { get; set; }
    public long LastInputClientSequence { get; set; } = -1;
    public long LastSnapshotClientSequence { get; set; } = -1;
}

internal sealed record LobbyRunPlayer(
    Guid PlayerId,
    string Username,
    int JoinOrder);

internal sealed record CachedSnapshot(
    Guid SenderPlayerId,
    long ClientSequence,
    long ServerSequence,
    long AuthorityEpoch,
    long ServerTimeUnixMs,
    System.Text.Json.JsonElement Payload);
