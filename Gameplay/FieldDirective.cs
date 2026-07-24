namespace Dust;

/// <summary>
/// Personal field contracts distributed across storage rooms. These contracts
/// pay independently of manifested cargo and do not seal the extraction hatch.
/// </summary>
internal enum FieldDirectiveKind
{
    ArchiveDecrypt,
    PressurePurge,
    SignalCalibrate,
    SpecimenSeal
}

internal sealed class FieldDirectiveNode
{
    public required int Number { get; init; }
    public required int RoomId { get; init; }
    public required Point Cell { get; init; }
    public required Direction WallSide { get; init; }
    public required float Phase { get; init; }
}

internal sealed class FieldDirective
{
    public required int Id { get; init; }
    public required FieldDirectiveKind Kind { get; init; }
    public required List<FieldDirectiveNode> Nodes { get; init; }
    public string? AssignedPlayerId { get; set; }
    public int ActivatedMask { get; set; }

    public int ActivatedCount
    {
        get
        {
            var count = 0;
            for (var index = 0; index < Nodes.Count; index++)
                if ((ActivatedMask & (1 << index)) != 0)
                    count++;
            return count;
        }
    }

    public bool IsComplete =>
        Nodes.Count > 0 && (ActivatedMask & ((1 << Nodes.Count) - 1)) ==
        (1 << Nodes.Count) - 1;

    public bool IsNodeActive(int nodeIndex) =>
        nodeIndex >= 0 && nodeIndex < Nodes.Count &&
        (ActivatedMask & (1 << nodeIndex)) != 0;

    public bool CanActivate(int nodeIndex) =>
        !IsNodeActive(nodeIndex) &&
        (Kind != FieldDirectiveKind.SignalCalibrate ||
         nodeIndex == ActivatedCount);

    public void Activate(int nodeIndex)
    {
        if (CanActivate(nodeIndex))
            ActivatedMask |= 1 << nodeIndex;
    }
}

internal sealed record ObjectiveRunPlayer(
    string PlayerId,
    string Username,
    int JoinOrder);
