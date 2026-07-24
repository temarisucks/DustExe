namespace Dust;

internal sealed record OnlineLobbySettings(
    RunMapSize MapSize,
    MazeStrictness MazeStrictness,
    RunHollowAmount HollowAmount,
    RunHollowTypes HollowTypes,
    bool DifficultyScaling)
{
    public static OnlineLobbySettings Default { get; } = new(
        RunMapSize.Medium,
        MazeStrictness.Normal,
        RunHollowAmount.Normal,
        RunHollowTypes.All,
        true);

    public RunSettingsSnapshot Snapshot() => new(
        MapSize, MazeStrictness, HollowAmount, HollowTypes, DifficultyScaling);

    public object ToProtocol() => new
    {
        mapSize = MapSize.ToString().ToLowerInvariant(),
        mazeStrictness = MazeStrictness.ToString().ToLowerInvariant(),
        hollowAmount = HollowAmount.ToString().ToLowerInvariant(),
        hollowTypes = Enum.GetValues<RunHollowTypes>()
            .Where(type => type is RunHollowTypes.Square or RunHollowTypes.Diamond or
                RunHollowTypes.Hex or RunHollowTypes.Sentry or RunHollowTypes.Triangle or
                RunHollowTypes.Camera or RunHollowTypes.Star)
            .Where(type => HollowTypes.HasFlag(type))
            .Select(type => type.ToString().ToLowerInvariant())
            .ToArray(),
        difficultyScaling = DifficultyScaling
    };
}

internal sealed record OnlineLobbyPlayer(
    string PlayerId,
    string Username,
    int JoinOrder,
    bool Connected);

internal sealed record OnlineRunPlayer(
    string PlayerId,
    string Username,
    int JoinOrder);

internal sealed record OnlineLobbySummary(
    string LobbyId,
    string Name,
    string HostUsername,
    int PlayerCount,
    int MaxPlayers,
    string Status);

internal sealed record OnlineLobbyState(
    string LobbyId,
    string Name,
    string HostPlayerId,
    int MaxPlayers,
    string Status,
    long Revision,
    long AuthorityEpoch,
    int RunLevel,
    OnlineLobbySettings Settings,
    IReadOnlyList<OnlineLobbyPlayer> Players,
    long? Seed = null)
{
    public IReadOnlyList<OnlineRunPlayer> RunStartPlayers { get; init; } = [];
}

internal sealed class OnlineRemotePlayer
{
    public required string PlayerId { get; init; }
    public required string Username { get; set; }
    public int JoinOrder { get; set; }
    public bool Connected { get; set; } = true;
    public bool InShop { get; set; }
    public bool Defeated { get; set; }
    public bool Extracted { get; set; }
    public bool Invisible { get; set; }
    public DroneModel Drone { get; set; }
    public Color CoreColor { get; set; }
    public Color FrameColor { get; set; }
    public Point Cell { get; set; }
    public Point PreviousCell { get; set; }
    public PointF VisualCell { get; set; }
    public PointF PreviousVisualCell { get; set; }
    public PointF MoveFrom { get; set; }
    public PointF MoveTo { get; set; }
    public float MoveProgress { get; set; } = 1;
    public float Bank { get; set; }
    public float Pitch { get; set; }
    public int Damage { get; set; }
    public int TotalDamageSustained { get; set; }
    public int MaximumHealth { get; set; } = 3;
    public float Invulnerability { get; set; }
    public long LastInputSequence { get; set; }
    public long LastReceivedInputSequence { get; set; }
    public Queue<OnlineMoveIntent> PendingMoves { get; } = new();
    public List<Point> Traversal { get; } = [];
    public HashSet<PerkId> EquippedPerks { get; } = [];
    public float CamouflageTimer { get; set; }
    public float CamouflageCooldown { get; set; }
    public float GhostFormTimer { get; set; }
    public float GhostFormCooldown { get; set; }
    public float HollowKillerCooldown { get; set; }
    public bool TraversalUsedGhostForm { get; set; }
    public bool LastDamageWasHollow { get; set; }
    public bool AppearanceReady { get; set; }
    public long AccountCredits { get; set; }
    public int ShopRepairReserve { get; set; }
    public int ShopProtectionCharges { get; set; }
    public long ShopTransactionRevision { get; set; }
    public string ShopMessage { get; set; } = string.Empty;
    public int ShopCue { get; set; }

    public bool HasPerk(PerkId perk) => EquippedPerks.Contains(perk);
}

internal readonly record struct OnlineMoveIntent(long Sequence, Direction Direction);

internal sealed class OnlineWorldSnapshot
{
    public int ProtocolVersion { get; set; } = 3;
    public long Tick { get; set; }
    public long WorldRevision { get; set; }
    public long AuthorityRevision { get; set; }
    public string HostPlayerId { get; set; } = string.Empty;
    public long Seed { get; set; }
    public string RandomState { get; set; } = "0";
    public int Level { get; set; } = 1;
    public long ElapsedMilliseconds { get; set; }
    public bool RunCompleted { get; set; }
    public bool RunFailed { get; set; }
    public int FieldCredits { get; set; }
    public OnlinePlayerSnapshot[] Players { get; set; } = [];
    public OnlineHollowSnapshot[] Hollows { get; set; } = [];
    public OnlineSentrySnapshot[] Sentries { get; set; } = [];
    public OnlineProjectileSnapshot[] Projectiles { get; set; } = [];
    public string DestroyedWallBits { get; set; } = string.Empty;
    public OnlineCargoSnapshot[] Cargo { get; set; } = [];
    public OnlineCreditSnapshot[] Credits { get; set; } = [];
    public OnlineSalvageSnapshot[] Salvage { get; set; } = [];
    public OnlineCircuitSnapshot[] CircuitSwitches { get; set; } = [];
    public OnlineFieldDirectiveSnapshot[] FieldDirectives { get; set; } = [];
    public int[] RevealedRoomIds { get; set; } = [];
    public OnlineDoorSnapshot[] Doors { get; set; } = [];
    public int SurvivorStage { get; set; } = -1;
    public string? SurvivorAssignedPlayerId { get; set; }
    public string? SurvivorEscortPlayerId { get; set; }
    public int[] ShopStock { get; set; } = [];
}

internal sealed class OnlinePlayerSnapshot
{
    public string PlayerId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public int JoinOrder { get; set; }
    public bool Connected { get; set; }
    public bool InShop { get; set; }
    public bool Defeated { get; set; }
    public bool Extracted { get; set; }
    public bool Invisible { get; set; }
    public bool Warning { get; set; }
    public int Drone { get; set; }
    public int CoreArgb { get; set; }
    public int FrameArgb { get; set; }
    public int CellX { get; set; }
    public int CellY { get; set; }
    public float VisualX { get; set; }
    public float VisualY { get; set; }
    public float MoveFromX { get; set; }
    public float MoveFromY { get; set; }
    public float MoveToX { get; set; }
    public float MoveToY { get; set; }
    public float MoveProgress { get; set; }
    public float Bank { get; set; }
    public float Pitch { get; set; }
    public int Damage { get; set; }
    public int TotalDamageSustained { get; set; }
    public int MaximumHealth { get; set; }
    public float Invulnerability { get; set; }
    public long LastInputSequence { get; set; }
    public int[] EquippedPerks { get; set; } = [];
    public float CamouflageTimer { get; set; }
    public float CamouflageCooldown { get; set; }
    public float GhostFormTimer { get; set; }
    public float GhostFormCooldown { get; set; }
    public float HollowKillerCooldown { get; set; }
    public bool LastDamageWasHollow { get; set; }
    public long AccountCredits { get; set; }
    public int ShopRepairReserve { get; set; }
    public int ShopProtectionCharges { get; set; }
    public long ShopTransactionRevision { get; set; }
    public string ShopMessage { get; set; } = string.Empty;
    public int ShopCue { get; set; }
}

internal sealed class OnlineHollowSnapshot
{
    public int Type { get; set; }
    public int State { get; set; }
    public int CellX { get; set; }
    public int CellY { get; set; }
    public int TargetX { get; set; }
    public int TargetY { get; set; }
    public int PreviousX { get; set; }
    public int PreviousY { get; set; }
    public int LastSeenX { get; set; }
    public int LastSeenY { get; set; }
    public float VisualX { get; set; }
    public float VisualY { get; set; }
    public float PreviousVisualX { get; set; }
    public float PreviousVisualY { get; set; }
    public float MoveFromX { get; set; }
    public float MoveFromY { get; set; }
    public float MoveToX { get; set; }
    public float MoveToY { get; set; }
    public float MoveProgress { get; set; }
    public float Cooldown { get; set; }
    public float SenseCooldown { get; set; }
    public float SearchTimer { get; set; }
    public float FacingAngle { get; set; }
    public float DesiredFacingAngle { get; set; }
    public float LookCooldown { get; set; }
    public float AnimationPhase { get; set; }
    public float AggressionScale { get; set; }
    public bool HasSight { get; set; }
    public string? TargetPlayerId { get; set; }
    public bool Empowered { get; set; }
    public bool TriangleSplit { get; set; }
    public float TriangleSplitTimer { get; set; }
    public float TriangleOrbitAngle { get; set; }
    public float PreviousTriangleOrbitAngle { get; set; }
    public float AbilityCooldown { get; set; }
    public float ProjectileCooldown { get; set; }
    public float TeleportFlash { get; set; }
}

internal sealed class OnlineSentrySnapshot
{
    public int CellX { get; set; }
    public int CellY { get; set; }
    public int PreviousX { get; set; }
    public int PreviousY { get; set; }
    public float FacingAngle { get; set; }
    public int RotationDirection { get; set; }
    public float AnimationPhase { get; set; }
    public float UnsuccessfulScanTime { get; set; }
    public float RelocationThreshold { get; set; }
    public float FireCooldown { get; set; }
    public float MuzzleFlash { get; set; }
    public bool HasSight { get; set; }
    public int Phase { get; set; }
    public float PhaseTimer { get; set; }
    public string? TargetPlayerId { get; set; }
    public bool Empowered { get; set; }
}

internal sealed class OnlineProjectileSnapshot
{
    public int Serial { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float PreviousX { get; set; }
    public float PreviousY { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float Lifetime { get; set; }
    public int Kind { get; set; }
    public int Damage { get; set; } = 1;
    public bool IgnoreWalls { get; set; }
    public bool DestroyWalls { get; set; }
}

internal sealed class OnlineCargoSnapshot
{
    public int Index { get; set; }
    public int CellX { get; set; }
    public int CellY { get; set; }
    public bool Carried { get; set; }
    public bool Delivered { get; set; }
    public string? AssignedPlayerId { get; set; }
    public string? CarrierPlayerId { get; set; }
}

internal sealed class OnlineCreditSnapshot
{
    public int Index { get; set; }
    public int CellX { get; set; }
    public int CellY { get; set; }
    public float VisualX { get; set; }
    public float VisualY { get; set; }
    public bool Collected { get; set; }
    public bool MagnetMoving { get; set; }
    public int TargetX { get; set; }
    public int TargetY { get; set; }
    public float MagnetProgress { get; set; }
}

internal sealed class OnlineSalvageSnapshot
{
    public int Index { get; set; }
    public bool Collected { get; set; }
    public bool Sold { get; set; }
}

internal sealed class OnlineCircuitSnapshot
{
    public int Number { get; set; }
    public bool Activated { get; set; }
    public string? AssignedPlayerId { get; set; }
}

internal sealed class OnlineFieldDirectiveSnapshot
{
    public int Id { get; set; }
    public string? AssignedPlayerId { get; set; }
    public int ActivatedMask { get; set; }
}

internal sealed class OnlineDoorSnapshot
{
    public int RoomId { get; set; }
    public float Progress { get; set; }
}
