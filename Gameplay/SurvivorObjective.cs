namespace Dust;

internal enum SurvivorObjectiveStage
{
    Uncontacted,
    Searching,
    Escorting,
    Rescued
}

/// <summary>
/// Optional human-recovery contract attached to one cargo room. The requester
/// remains at the room station while the named worker is found elsewhere.
/// </summary>
internal sealed class SurvivorObjective
{
    public required string WorkerName { get; init; }
    public required int RequesterRoomId { get; init; }
    public required Point RequesterCell { get; init; }
    public required Point WorkerCell { get; init; }
    public required float VisualPhase { get; init; }
    public SurvivorObjectiveStage Stage { get; set; }
    public string? EscortPlayerId { get; set; }

    public bool IsResolved => Stage == SurvivorObjectiveStage.Rescued;
}
