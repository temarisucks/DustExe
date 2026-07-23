namespace Dust;

internal enum RunMapSize
{
    Small,
    Medium,
    Large
}

internal enum MazeStrictness
{
    Strict,
    Normal,
    Loose
}

internal enum RunHollowAmount
{
    None,
    Small,
    Normal,
    Large
}

[Flags]
internal enum RunHollowTypes
{
    None = 0,
    Square = 1 << 0,
    Diamond = 1 << 1,
    Hex = 1 << 2,
    Sentry = 1 << 3,
    All = Square | Diamond | Hex | Sentry
}

/// <summary>
/// An immutable copy of the options used by a run.  It is captured before the
/// loading screen opens, so later menu changes can never alter a live run.
/// </summary>
internal readonly record struct RunSettingsSnapshot(
    RunMapSize MapSize,
    MazeStrictness Strictness,
    RunHollowAmount HollowAmount,
    RunHollowTypes HollowTypes,
    bool DifficultyScaling)
{
    public static RunSettingsSnapshot Default { get; } = new(
        RunMapSize.Medium,
        MazeStrictness.Normal,
        RunHollowAmount.Normal,
        RunHollowTypes.All,
        DifficultyScaling: true);

    public bool Allows(RunHollowTypes type) => (HollowTypes & type) != 0;
}

/// <summary>Session-persistent values edited on the run configuration screen.</summary>
internal sealed class RunSettingsState
{
    public RunMapSize MapSize { get; set; } = RunMapSize.Medium;
    public MazeStrictness Strictness { get; set; } = MazeStrictness.Normal;
    public RunHollowAmount HollowAmount { get; set; } = RunHollowAmount.Normal;
    public RunHollowTypes HollowTypes { get; set; } = RunHollowTypes.All;
    public bool DifficultyScaling { get; set; } = true;

    public RunSettingsSnapshot Snapshot()
    {
        var types = HollowTypes & RunHollowTypes.All;
        if (types == RunHollowTypes.None) types = RunHollowTypes.All;
        return new RunSettingsSnapshot(MapSize, Strictness, HollowAmount, types, DifficultyScaling);
    }
}

internal readonly record struct EnemyRoster(int Squares, int Diamonds, int Hexes, int Sentries)
{
    public int Total => Squares + Diamonds + Hexes + Sentries;
}
