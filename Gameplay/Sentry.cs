namespace Dust;

internal enum SentryPhase
{
    Scanning,
    Submerging,
    Buried,
    Emerging
}

internal sealed class Sentry
{
    public Point Cell { get; set; }
    public Point PreviousCell { get; set; }
    public float FacingAngle { get; set; }
    public int RotationDirection { get; init; } = 1;
    public float AnimationPhase { get; init; }
    public float UnsuccessfulScanTime { get; set; }
    public float RelocationThreshold { get; set; }
    public float FireCooldown { get; set; }
    public float MuzzleFlash { get; set; }
    public bool HasSight { get; set; }
    public SentryPhase Phase { get; set; }
    public float PhaseTimer { get; set; }
    public string? TargetPlayerId { get; set; }
    public bool Empowered { get; set; }
    public bool PresentationReady { get; set; }
    public float PresentationFacingAngle { get; set; }
    public float PresentationSnapshotAge { get; set; }
}

internal enum EnemyProjectileKind
{
    Sentry,
    Triangle,
    Star
}

internal sealed class SentryProjectile
{
    public PointF Position { get; set; }
    public PointF PreviousPosition { get; set; }
    public PointF Velocity { get; init; }
    public float Lifetime { get; set; }
    public int Serial { get; init; }
    public EnemyProjectileKind Kind { get; init; }
    public int Damage { get; init; } = 1;
    public bool IgnoreWalls { get; init; }
    public bool DestroyWalls { get; init; }
    public bool PresentationReady { get; set; }
    public PointF PresentationPosition { get; set; }
    public PointF PreviousPresentationPosition { get; set; }
    public float PresentationSnapshotAge { get; set; }
}
