namespace Dust;

internal enum CargoKind
{
    SignalRelay,
    CryoCell,
    TissueArchive,
    SurveyCore,
    BlackRecorder,
    ResinSample
}

internal sealed class CargoItem
{
    public required string Code { get; init; }
    public required CargoKind Kind { get; init; }
    public required Point Cell { get; set; }
    public required int RoomId { get; init; }
    public bool Required { get; set; }
    public bool Carried { get; set; }
    public bool Delivered { get; set; }
    public string? CarrierPlayerId { get; set; }
    public float Phase { get; init; }
}

internal sealed class CreditPickup
{
    public required Point Cell { get; set; }
    public required PointF VisualCell { get; set; }
    public required int Value { get; init; }
    public float Phase { get; init; }
    public bool Collected { get; set; }
    public bool MagnetMoving { get; set; }
    public Point MagnetTargetCell { get; set; }
    public float MagnetProgress { get; set; } = 1;
}
