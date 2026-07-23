namespace Dust;

// Explicit values are part of the save-file format. Never reorder or reuse them.
internal enum AchievementId
{
    Ankles = 1,
    Oops = 2,
    SpeedDemon = 3,
    GhostLover = 4,
    LastSurprise = 5,
    FirstTry = 6,
    IWantToBeNinja = 7,
    Greedy = 8,
    Nonstop = 9,
    Unstoppable = 10,
    ImLost = 11,
    CantBeThatBad = 12,
    TheCartographer = 13,
    BabySteps = 14,
    Wimpy = 15,
    IDidItQuestion = 16,
    IDidIt = 17,
    CageMatch = 18,
    ImpossibleOdds = 19,
    LoveOfTheGame = 20
}

// Explicit values are part of the save-file format. Never reorder or reuse them.
internal enum PerkId
{
    Durable = 1,
    MoneyMagnet = 2,
    Hop = 3,
    Camouflage = 4,
    MiniMap = 5,
    GhostForm = 6,
    Retracer = 7,
    HollowKiller = 8
}

internal enum AchievementRank
{
    Easy = 1,
    Moderate = 2,
    Hard = 3,
    Extreme = 4
}

internal enum AchievementProgressUnit
{
    Trigger = 0,
    Seconds = 1,
    ConsecutiveWins = 2,
    Percent = 3
}

internal enum PerkActivation
{
    Passive = 0,
    Space = 1
}

internal enum PerkEquipResult
{
    Equipped,
    AlreadyEquipped,
    RequiredAchievementLocked,
    UnknownPerk
}
