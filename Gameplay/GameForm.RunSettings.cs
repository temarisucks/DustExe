namespace Dust;

internal sealed partial class GameForm
{
    private readonly RunSettingsState _runSettings = new();
    private RunSettingsSnapshot _activeRunSettings = RunSettingsSnapshot.Default;

    /// <summary>
    /// The immutable configuration captured for the current run. Achievement
    /// checks should use this instead of the editable menu state.
    /// </summary>
    internal RunSettingsSnapshot ActiveRunSettings => _activeRunSettings;

    private int RunDifficultyLevel => _level + _survivorDifficultyOffset;

    private float RunAggressionScale => !_activeRunSettings.DifficultyScaling
        ? 1f
        : 1f + Math.Min(.36f, Math.Max(0, RunDifficultyLevel - 1) * .04f);

    private void StartConfiguredRun()
    {
        _activeRunSettings = _runSettings.Snapshot();
        StartGame();
    }

    private Size GetRunMazeDimensions()
    {
        var (baseWidth, baseHeight, maximumWidth, maximumHeight) = _activeRunSettings.MapSize switch
        {
            RunMapSize.Small => (29, 21, 49, 37),
            RunMapSize.Large => (57, 43, 81, 61),
            _ => (41, 31, 65, 49)
        };

        if (!_activeRunSettings.DifficultyScaling)
            return new Size(baseWidth, baseHeight);

        // Medium intentionally preserves Dust's established progression curve.
        var width = Math.Min(maximumWidth, baseWidth + ((RunDifficultyLevel - 1) / 2) * 4);
        var height = Math.Min(maximumHeight, baseHeight + ((RunDifficultyLevel - 1) / 3) * 4);
        return new Size(width, height);
    }

    private int RunStartJunctionOpenings => _activeRunSettings.Strictness switch
    {
        MazeStrictness.Strict => 2,
        MazeStrictness.Loose => 4,
        _ => 3
    };

    private EnemyRoster GetEnemyRoster()
    {
        if (_activeRunSettings.HollowAmount == RunHollowAmount.None ||
            _activeRunSettings.HollowTypes == RunHollowTypes.None)
            return new EnemyRoster(0, 0, 0, 0, 0, 0, 0);

        var total = _activeRunSettings.HollowAmount switch
        {
            RunHollowAmount.Small => 3,
            RunHollowAmount.Large => 12,
            _ => 7
        };
        if (_activeRunSettings.DifficultyScaling)
            total += Math.Min(10, Math.Max(0, RunDifficultyLevel - 1));

        var types = new[]
        {
            (Flag: RunHollowTypes.Square, Weight: 3),
            (Flag: RunHollowTypes.Diamond, Weight: 2),
            (Flag: RunHollowTypes.Hex, Weight: 1),
            (Flag: RunHollowTypes.Sentry, Weight: 1),
            (Flag: RunHollowTypes.Triangle, Weight: 2),
            (Flag: RunHollowTypes.Camera, Weight: 1),
            (Flag: RunHollowTypes.Star, Weight: 1)
        };
        var enabled = types.Where(type => _activeRunSettings.Allows(type.Flag)).ToArray();
        if (enabled.Length == 0) return new EnemyRoster(0, 0, 0, 0, 0, 0, 0);

        // If density can support the whole selected roster, every socket gets
        // one representative before weights distribute the surplus. This keeps
        // specialist types such as Stars and Cameras present in a default All
        // run instead of losing them to fractional rounding.
        var guaranteedPerType = total >= enabled.Length ? 1 : 0;
        var remaining = total - guaranteedPerType * enabled.Length;
        var weightTotal = enabled.Sum(type => type.Weight);
        var allocations = new Dictionary<RunHollowTypes, int>();
        var fractions = new List<(RunHollowTypes Type, float Fraction, int Order)>();
        var assignedSurplus = 0;
        for (var index = 0; index < enabled.Length; index++)
        {
            var exact = remaining * enabled[index].Weight / (float)weightTotal;
            var surplus = (int)MathF.Floor(exact);
            var count = guaranteedPerType + surplus;
            allocations[enabled[index].Flag] = count;
            fractions.Add((enabled[index].Flag, exact - surplus, index));
            assignedSurplus += surplus;
        }

        foreach (var entry in fractions
                     .OrderByDescending(entry => entry.Fraction)
                     .ThenBy(entry => entry.Order)
                     .Take(remaining - assignedSurplus))
            allocations[entry.Type]++;

        int Count(RunHollowTypes type) => allocations.GetValueOrDefault(type);
        return new EnemyRoster(
            Count(RunHollowTypes.Square),
            Count(RunHollowTypes.Diamond),
            Count(RunHollowTypes.Hex),
            Count(RunHollowTypes.Sentry),
            Count(RunHollowTypes.Triangle),
            Count(RunHollowTypes.Camera),
            Count(RunHollowTypes.Star));
    }
}
