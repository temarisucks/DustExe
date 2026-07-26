namespace Dust;

internal enum HollowType
{
    Square,
    Diamond,
    Hex,
    Triangle,
    Camera,
    Star
}
internal enum HollowState { Roam, Chase, Search }

internal sealed class TriangleMember
{
    public int Index { get; init; }
    public Point Cell { get; set; }
    public Point TargetCell { get; set; }
    public Point PreviousCell { get; set; }
    public PointF VisualCell { get; set; }
    public PointF PreviousVisualCell { get; set; }
    public PointF MoveFrom { get; set; }
    public PointF MoveTo { get; set; }
    public float MoveProgress { get; set; } = 1;
    public float Cooldown { get; set; }
    public float FacingAngle { get; set; }

    // Presentation state is deliberately separate from the host-authoritative
    // member simulation, just like it is for an intact Hollow.
    public bool PresentationReady { get; set; }
    public PointF PresentationCell { get; set; }
    public PointF PreviousPresentationCell { get; set; }
    public float PresentationSnapshotAge { get; set; }

    public bool IsMoving => MoveProgress < 1;
}

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
    public bool Empowered { get; set; }
    public bool TriangleSplit { get; set; }
    public bool TriangleReforming { get; set; }
    public Point TriangleRallyCell { get; set; }
    public List<TriangleMember> TriangleMembers { get; } = [];
    public float TriangleSplitTimer { get; set; }
    public float TriangleOrbitAngle { get; set; }
    public float PreviousTriangleOrbitAngle { get; set; }
    public float AbilityCooldown { get; set; }
    public float ProjectileCooldown { get; set; }
    public float TeleportFlash { get; set; }

    // Guests keep a presentation-only pose separate from the authoritative
    // simulation pose above. This lets the renderer advance smoothly between
    // sparse host checkpoints without contaminating collision, perception, or
    // a checkpoint used for host migration.
    public bool PresentationReady { get; set; }
    public PointF PresentationCell { get; set; }
    public PointF PreviousPresentationCell { get; set; }
    public float PresentationFacingAngle { get; set; }
    public float PresentationSnapshotAge { get; set; }

    public bool IsMoving => MoveProgress < 1;

    public float ViewDistance => (Type switch
    {
        HollowType.Square => 4f,
        HollowType.Diamond => 7f,
        HollowType.Hex => 8f,
        HollowType.Triangle => 7.5f,
        HollowType.Camera => 9f,
        HollowType.Star => 7f,
        _ => 7f
    }) * (1f + (AggressionScale - 1f) * .65f);

    public float MoveDuration => (Type switch
    {
        HollowType.Square => .72f,
        HollowType.Diamond => .39f,
        HollowType.Hex => .43f,
        HollowType.Triangle => .31f,
        HollowType.Camera => float.MaxValue,
        HollowType.Star => .48f,
        _ => .43f
    }) / (AggressionScale * EmpoweredSpeedMultiplier);

    public float EmpoweredSpeedMultiplier => !Empowered ? 1f : Type switch
    {
        HollowType.Square => 1.62f,
        HollowType.Diamond => 1.48f,
        HollowType.Hex => 1.42f,
        HollowType.Triangle => 1.40f,
        HollowType.Star => 1.25f,
        _ => 1f
    };

    public float FieldOfView => Type switch
    {
        HollowType.Square => 65 * MathF.PI / 180,
        HollowType.Diamond => 95 * MathF.PI / 180,
        HollowType.Hex => 75 * MathF.PI / 180,
        HollowType.Triangle => 88 * MathF.PI / 180,
        HollowType.Camera => 58 * MathF.PI / 180,
        HollowType.Star => 92 * MathF.PI / 180,
        _ => 75 * MathF.PI / 180
    };

    public float TurnSpeed => (Type switch
    {
        HollowType.Square => 2.5f,
        HollowType.Diamond => 4.1f,
        HollowType.Hex => 3.6f,
        HollowType.Triangle => 4.8f,
        HollowType.Camera => 1.35f,
        HollowType.Star => 3.25f,
        _ => 3.6f
    }) * AggressionScale;
}
