namespace Dust;

internal readonly record struct AchievementDefinition(
    AchievementId Id,
    string Name,
    string Description,
    AchievementRank Rank,
    double Target,
    AchievementProgressUnit ProgressUnit);

internal readonly record struct PerkDefinition(
    PerkId Id,
    string Name,
    string Description,
    AchievementId RequiredAchievement,
    PerkActivation Activation,
    AchievementId? AdditionalRequiredAchievement = null)
{
    internal IEnumerable<AchievementId> RequiredAchievements
    {
        get
        {
            yield return RequiredAchievement;
            if (AdditionalRequiredAchievement.HasValue)
                yield return AdditionalRequiredAchievement.Value;
        }
    }

    internal bool RequirementsMet(Func<AchievementId, bool> isUnlocked) =>
        RequiredAchievements.All(isUnlocked);
}

internal static class ProgressionCatalog
{
    // Catalog order is also the default order on the Achievements screen: easiest first.
    internal static readonly AchievementDefinition[] Achievements =
    [
        new(AchievementId.Oops, "Oops", "Quit a maze within 10 seconds of starting it.",
            AchievementRank.Easy, 1, AchievementProgressUnit.Trigger),
        new(AchievementId.ImLost, "Im Lost", "Spend over 3 minutes wandering in a single maze.",
            AchievementRank.Easy, 180, AchievementProgressUnit.Seconds),
        new(AchievementId.CantBeThatBad, "Cant Be That Bad", "Beat a maze for the first time.",
            AchievementRank.Easy, 1, AchievementProgressUnit.Trigger),
        new(AchievementId.BabySteps, "Baby Steps", "Beat a small, loose maze with few or no Hollows.",
            AchievementRank.Easy, 1, AchievementProgressUnit.Trigger),
        new(AchievementId.Wimpy, "Wimpy", "Beat any maze with the Hollow amount set to none.",
            AchievementRank.Easy, 1, AchievementProgressUnit.Trigger),
        new(AchievementId.LoveOfTheGame, "Love of the Game", "Enter a strict maze with no Hollows.",
            AchievementRank.Easy, 1, AchievementProgressUnit.Trigger),

        new(AchievementId.Ankles, "Ankles", "While being chased by a Hollow, turn a corner and pass by it without taking damage.",
            AchievementRank.Moderate, 1, AchievementProgressUnit.Trigger),
        new(AchievementId.SpeedDemon, "Speed Demon", "Beat a maze in under 60 seconds.",
            AchievementRank.Moderate, 1, AchievementProgressUnit.Trigger),
        new(AchievementId.GhostLover, "Ghost Lover", "Let a Hollow chase you for over 15 seconds without getting hit.",
            AchievementRank.Moderate, 15, AchievementProgressUnit.Seconds),
        new(AchievementId.LastSurprise, "Last Surprise", "Take 2 hits from a Hollow in less than a minute and still win the maze.",
            AchievementRank.Moderate, 1, AchievementProgressUnit.Trigger),
        new(AchievementId.FirstTry, "First Try!", "Beat any maze without taking damage.",
            AchievementRank.Moderate, 1, AchievementProgressUnit.Trigger),
        new(AchievementId.CageMatch, "Cage Match", "Win a small maze with the Hollow amount set to large.",
            AchievementRank.Moderate, 1, AchievementProgressUnit.Trigger),

        new(AchievementId.IWantToBeNinja, "I Want to Be Ninja", "Complete a maze without alerting any Hollows.",
            AchievementRank.Hard, 1, AchievementProgressUnit.Trigger),
        new(AchievementId.Greedy, "Greedy", "Collect the maximum amount of money available in a maze.",
            AchievementRank.Hard, 1, AchievementProgressUnit.Trigger),
        new(AchievementId.Nonstop, "Nonstop", "Win 3 mazes in a row.",
            AchievementRank.Hard, 3, AchievementProgressUnit.ConsecutiveWins),
        new(AchievementId.TheCartographer, "The Cartographer", "Visit every traversable tile in a maze.",
            AchievementRank.Hard, 100, AchievementProgressUnit.Percent),
        new(AchievementId.IDidItQuestion, "I Did It?", "Beat a large, strict maze with a large Hollow population.",
            AchievementRank.Hard, 1, AchievementProgressUnit.Trigger),

        new(AchievementId.Unstoppable, "Unstoppable", "Win 10 mazes in a row.",
            AchievementRank.Extreme, 10, AchievementProgressUnit.ConsecutiveWins),
        new(AchievementId.IDidIt, "I Did It!", "Meet the I Did It? conditions without taking damage.",
            AchievementRank.Extreme, 1, AchievementProgressUnit.Trigger),
        new(AchievementId.ImpossibleOdds, "Impossible Odds", "Win a strict maze with many Hollows while below half integrity.",
            AchievementRank.Extreme, 1, AchievementProgressUnit.Trigger)
    ];

    internal static readonly PerkDefinition[] Perks =
    [
        new(PerkId.Durable, "Durable", "Increase maximum integrity from 3 hits to 5.",
            AchievementId.CantBeThatBad, PerkActivation.Passive),
        new(PerkId.MoneyMagnet, "Money Magnet", "Loose money on the floor slowly pulls toward the drone.",
            AchievementId.Greedy, PerkActivation.Passive),
        new(PerkId.Hop, "Hop", "Move 2 spaces at a time, falling back to 1 space when the second is blocked.",
            AchievementId.CantBeThatBad, PerkActivation.Passive),
        new(PerkId.Camouflage, "Camouflage", "Press Space to become invisible for a few seconds.",
            AchievementId.IWantToBeNinja, PerkActivation.Space),
        new(PerkId.MiniMap, "Mini Map", "Display a mini map in the bottom-right corner of the feed.",
            AchievementId.TheCartographer, PerkActivation.Passive),
        new(PerkId.GhostForm, "Ghost Form", "Press Space to pass through walls for 3.5 seconds.",
            AchievementId.GhostLover, PerkActivation.Space),
        new(PerkId.Retracer, "Retracer", "Leave a visible trail behind the drone.",
            AchievementId.ImLost, PerkActivation.Passive),
        new(PerkId.HollowKiller, "Hollow Killer", "Press Space to erase every enemy within a 4-tile radius. Recharges in 45 seconds.",
            AchievementId.IDidItQuestion, PerkActivation.Space, AchievementId.IDidIt)
    ];

    private static readonly IReadOnlyDictionary<AchievementId, AchievementDefinition> AchievementLookup =
        Achievements.ToDictionary(definition => definition.Id);

    private static readonly IReadOnlyDictionary<PerkId, PerkDefinition> PerkLookup =
        Perks.ToDictionary(definition => definition.Id);

    internal static bool TryGetAchievement(AchievementId id, out AchievementDefinition definition) =>
        AchievementLookup.TryGetValue(id, out definition);

    internal static bool TryGetPerk(PerkId id, out PerkDefinition definition) =>
        PerkLookup.TryGetValue(id, out definition);

    internal static AchievementDefinition GetAchievement(AchievementId id) =>
        AchievementLookup.TryGetValue(id, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown achievement identifier.");

    internal static PerkDefinition GetPerk(PerkId id) =>
        PerkLookup.TryGetValue(id, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown perk identifier.");
}
