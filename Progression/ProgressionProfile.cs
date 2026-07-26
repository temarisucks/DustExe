namespace Dust;

internal sealed class AchievementProgressState
{
    public AchievementId Id { get; set; }
    public bool IsUnlocked { get; set; }
    public DateTimeOffset? UnlockedAtUtc { get; set; }
    public double Progress { get; set; }
}

internal readonly record struct AchievementProgressSnapshot(
    AchievementId Id,
    bool IsUnlocked,
    DateTimeOffset? UnlockedAtUtc,
    double Progress,
    double Target)
{
    public double Completion => Target <= 0 ? 0 : Math.Clamp(Progress / Target, 0, 1);
}

internal readonly record struct AchievementProgressUpdate(
    bool Changed,
    bool UnlockedNow,
    AchievementProgressSnapshot State);

internal sealed class ProgressionProfile
{
    public List<AchievementProgressState> AchievementStates { get; set; } = [];
    public List<PerkId> EquippedPerks { get; set; } = [];
    public int CurrentWinStreak { get; set; }
    public int BestWinStreak { get; set; }

    internal void Normalize()
    {
        AchievementStates ??= [];
        EquippedPerks ??= [];

        var normalizedStates = new Dictionary<AchievementId, AchievementProgressState>();
        foreach (var state in AchievementStates)
        {
            if (state is null || !ProgressionCatalog.TryGetAchievement(state.Id, out var definition))
                continue;

            var progress = double.IsFinite(state.Progress)
                ? Math.Clamp(state.Progress, 0, definition.Target)
                : 0;
            var unlocked = state.IsUnlocked || progress >= definition.Target;
            DateTimeOffset? unlockedAt = unlocked ? NormalizeUnlockTime(state.UnlockedAtUtc) : null;
            if (unlocked) progress = definition.Target;

            if (normalizedStates.TryGetValue(state.Id, out var existing))
            {
                existing.Progress = Math.Max(existing.Progress, progress);
                existing.IsUnlocked |= unlocked;
                existing.UnlockedAtUtc = Earliest(existing.UnlockedAtUtc, unlockedAt);
                if (existing.IsUnlocked && existing.UnlockedAtUtc is null)
                    existing.UnlockedAtUtc = DateTimeOffset.UtcNow;
                continue;
            }

            normalizedStates[state.Id] = new AchievementProgressState
            {
                Id = state.Id,
                IsUnlocked = unlocked,
                UnlockedAtUtc = unlockedAt,
                Progress = progress
            };
        }

        AchievementStates = normalizedStates.Values
            .OrderBy(state => (int)state.Id)
            .ToList();

        CurrentWinStreak = Math.Clamp(CurrentWinStreak, 0, 1_000_000);
        BestWinStreak = Math.Clamp(Math.Max(BestWinStreak, CurrentWinStreak), 0, 1_000_000);
        if (BestWinStreak > 0)
        {
            UpdateProgress(AchievementId.Nonstop, Math.Min(BestWinStreak, 3));
            UpdateProgress(AchievementId.Unstoppable, Math.Min(BestWinStreak, 10));
        }

        var validPerks = EquippedPerks
            .Where(id => ProgressionCatalog.TryGetPerk(id, out var definition) &&
                         definition.RequirementsMet(IsAchievementUnlocked))
            .Distinct()
            .OrderBy(id => (int)id)
            .ToList();

        // The drone has one passive socket and one active socket. Older profiles
        // may contain the former multi-passive loadout, so normalization keeps a
        // deterministic valid perk in each channel instead of activating several
        // modifications behind the player's back.
        EquippedPerks = LimitToLoadoutSlots(validPerks);
    }

    internal bool IsAchievementUnlocked(AchievementId id)
    {
        if (!ProgressionCatalog.TryGetAchievement(id, out _)) return false;
        return AchievementStates.FirstOrDefault(state => state.Id == id)?.IsUnlocked == true;
    }

    internal AchievementProgressSnapshot GetAchievementState(AchievementId id)
    {
        var definition = ProgressionCatalog.GetAchievement(id);
        var state = AchievementStates.FirstOrDefault(candidate => candidate.Id == id);
        return state is null
            ? new AchievementProgressSnapshot(id, false, null, 0, definition.Target)
            : Snapshot(state, definition);
    }

    internal AchievementProgressSnapshot[] GetAchievementStates() =>
        ProgressionCatalog.Achievements
            .Select(definition => GetAchievementState(definition.Id))
            .ToArray();

    internal bool UnlockAchievement(AchievementId id, DateTimeOffset? unlockedAtUtc = null)
    {
        if (!ProgressionCatalog.TryGetAchievement(id, out var definition)) return false;
        var state = FindOrCreateState(id);
        if (state.IsUnlocked) return false;

        state.IsUnlocked = true;
        state.Progress = definition.Target;
        state.UnlockedAtUtc = NormalizeUnlockTime(unlockedAtUtc);
        return true;
    }

    internal AchievementProgressUpdate UpdateProgress(AchievementId id, double progress, bool onlyIncrease = true)
    {
        if (!ProgressionCatalog.TryGetAchievement(id, out var definition))
            return default;

        var state = FindOrCreateState(id);
        var normalized = double.IsFinite(progress) ? Math.Clamp(progress, 0, definition.Target) : 0;
        if (onlyIncrease) normalized = Math.Max(state.Progress, normalized);
        if (state.IsUnlocked) normalized = definition.Target;

        var progressChanged = Math.Abs(normalized - state.Progress) > 0.0001;
        state.Progress = normalized;

        var unlockedNow = false;
        if (!state.IsUnlocked && normalized >= definition.Target)
        {
            state.IsUnlocked = true;
            state.UnlockedAtUtc = DateTimeOffset.UtcNow;
            unlockedNow = true;
        }

        return new AchievementProgressUpdate(
            progressChanged || unlockedNow,
            unlockedNow,
            Snapshot(state, definition));
    }

    internal AchievementProgressUpdate AdvanceProgress(AchievementId id, double amount = 1)
    {
        if (!ProgressionCatalog.TryGetAchievement(id, out _)) return default;
        var current = GetAchievementState(id);
        var next = double.IsFinite(amount) ? current.Progress + amount : current.Progress;
        return UpdateProgress(id, next);
    }

    internal IReadOnlyList<AchievementId> RecordMazeWin()
    {
        if (CurrentWinStreak < 1_000_000) CurrentWinStreak++;
        BestWinStreak = Math.Max(BestWinStreak, CurrentWinStreak);

        var unlocked = new List<AchievementId>(2);
        if (UpdateProgress(AchievementId.Nonstop, CurrentWinStreak).UnlockedNow)
            unlocked.Add(AchievementId.Nonstop);
        if (UpdateProgress(AchievementId.Unstoppable, CurrentWinStreak).UnlockedNow)
            unlocked.Add(AchievementId.Unstoppable);
        return unlocked;
    }

    internal void ResetWinStreak() => CurrentWinStreak = 0;

    internal bool HasEquippedPerk(PerkId id) => EquippedPerks.Contains(id);

    internal PerkEquipResult EquipPerk(PerkId id)
    {
        if (!ProgressionCatalog.TryGetPerk(id, out var definition))
            return PerkEquipResult.UnknownPerk;
        if (!definition.RequirementsMet(IsAchievementUnlocked))
            return PerkEquipResult.RequiredAchievementLocked;
        if (EquippedPerks.Contains(id))
            return PerkEquipResult.AlreadyEquipped;

        EquippedPerks.RemoveAll(equippedId =>
            ProgressionCatalog.TryGetPerk(equippedId, out var equippedDefinition) &&
            equippedDefinition.Activation == definition.Activation);

        EquippedPerks.Add(id);
        EquippedPerks.Sort((left, right) => ((int)left).CompareTo((int)right));
        return PerkEquipResult.Equipped;
    }

    internal bool UnequipPerk(PerkId id) => EquippedPerks.Remove(id);

    /// <summary>
    /// Sanitizes untrusted online loadouts and legacy saves to the same physical
    /// one-passive/one-active socket rule used by the archive equip screen.
    /// </summary>
    internal static List<PerkId> LimitToLoadoutSlots(IEnumerable<PerkId> perks)
    {
        var valid = perks
            .Where(id => ProgressionCatalog.TryGetPerk(id, out _))
            .Distinct()
            .OrderBy(id => (int)id)
            .ToArray();
        var passive = valid
            .Where(id => ProgressionCatalog.GetPerk(id).Activation == PerkActivation.Passive)
            .Cast<PerkId?>()
            .FirstOrDefault();
        var active = valid
            .Where(id => ProgressionCatalog.GetPerk(id).Activation == PerkActivation.Space)
            // Preserve the old deterministic active-channel migration rule.
            .OrderByDescending(id => id == PerkId.GhostForm)
            .ThenBy(id => (int)id)
            .Cast<PerkId?>()
            .FirstOrDefault();

        var result = new List<PerkId>(2);
        if (passive.HasValue) result.Add(passive.Value);
        if (active.HasValue) result.Add(active.Value);
        result.Sort((left, right) => ((int)left).CompareTo((int)right));
        return result;
    }

    private AchievementProgressState FindOrCreateState(AchievementId id)
    {
        var state = AchievementStates.FirstOrDefault(candidate => candidate.Id == id);
        if (state is not null) return state;

        state = new AchievementProgressState { Id = id };
        AchievementStates.Add(state);
        AchievementStates.Sort((left, right) => ((int)left.Id).CompareTo((int)right.Id));
        return state;
    }

    private static AchievementProgressSnapshot Snapshot(
        AchievementProgressState state,
        AchievementDefinition definition) =>
        new(state.Id, state.IsUnlocked, state.UnlockedAtUtc, state.Progress, definition.Target);

    private static DateTimeOffset NormalizeUnlockTime(DateTimeOffset? value)
    {
        var now = DateTimeOffset.UtcNow;
        if (value is null) return now;

        var timestamp = value.Value.ToUniversalTime();
        if (timestamp < DateTimeOffset.UnixEpoch || timestamp > now.AddMinutes(5)) return now;
        return timestamp > now ? now : timestamp;
    }

    private static DateTimeOffset? Earliest(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null) return right;
        if (right is null) return left;
        return left <= right ? left : right;
    }
}
