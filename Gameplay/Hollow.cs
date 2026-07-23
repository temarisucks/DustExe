namespace Dust;

internal enum HollowType { Square, Diamond, Hex }
internal enum HollowState { Roam, Chase, Search }

internal sealed class Hollow
{
    public HollowType Type { get; init; }
    public HollowState State { get; set; }
    public Point Cell { get; set; }
    public Point TargetCell { get; set; }
    public Point PreviousCell { get; set; }
    public Point LastSeen { get; set; }
    public PointF LastSeenVisual { get; set; }
    public PointF VisualCell { get; set; }
    public PointF PreviousVisualCell { get; set; }
    public PointF MoveFrom { get; set; }
    public PointF MoveTo { get; set; }
    public float MoveProgress { get; set; } = 1;
    public float Cooldown { get; set; }
    public float SenseCooldown { get; set; }
    public float SearchTimer { get; set; }
    public float FacingAngle { get; set; }
    public float DesiredFacingAngle { get; set; }
    public float LookCooldown { get; set; }
    public float AnimationPhase { get; init; }
    public float AggressionScale { get; init; } = 1f;
    public bool HasSight { get; set; }
    public string? TargetPlayerId { get; set; }

    public bool IsMoving => MoveProgress < 1;

    public float ViewDistance => (Type switch
    {
        HollowType.Square => 4f,
        HollowType.Diamond => 7f,
        _ => 8f
    }) * (1f + (AggressionScale - 1f) * .65f);

    public float MoveDuration => (Type switch
    {
        HollowType.Square => .72f,
        HollowType.Diamond => .39f,
        _ => .43f
    }) / AggressionScale;

    public float FieldOfView => Type switch
    {
        HollowType.Square => 65 * MathF.PI / 180,
        HollowType.Diamond => 95 * MathF.PI / 180,
        _ => 75 * MathF.PI / 180
    };

    public float TurnSpeed => (Type switch
    {
        HollowType.Square => 2.5f,
        HollowType.Diamond => 4.1f,
        _ => 3.6f
    }) * AggressionScale;
}
