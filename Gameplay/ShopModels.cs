namespace Dust;

internal enum RoomPropKind
{
    CargoStack,
    PipeManifold,
    SpecimenCabinet,
    PressureTank,
    CableReel,
    WorkLight
}

internal sealed class RoomProp
{
    public required int RoomId { get; init; }
    public required Point Cell { get; init; }
    public required RoomPropKind Kind { get; init; }
    public required int Variant { get; init; }
}

internal enum SalvageKind
{
    CopperSpool,
    OpticShard,
    ServoClutch,
    MemoryFoil
}

internal sealed class RoomSalvage
{
    public required int RoomId { get; init; }
    public required Point Cell { get; init; }
    public required SalvageKind Kind { get; init; }
    public required int Value { get; init; }
    public bool Collected { get; set; }
    public bool Sold { get; set; }
}

internal sealed class ShopKiosk
{
    public required int RoomId { get; init; }
    public required Point Cell { get; init; }
}

internal enum ShopItemKind
{
    FramePatch,
    ReconstructionGel,
    AegisFuse
}

internal sealed class ShopStockItem
{
    public required ShopItemKind Kind { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required int Price { get; init; }
    public required int StartingStock { get; init; }
    public int Stock { get; set; }
}

internal enum ShopPage
{
    Commands,
    Buy,
    Sell,
    Talk
}
