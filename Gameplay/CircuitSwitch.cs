namespace Dust;

internal sealed class CircuitSwitch
{
    public required int Number { get; init; }
    public required int RoomId { get; init; }
    public required Point Cell { get; init; }
    public required float Phase { get; init; }
    public bool Activated { get; set; }
}
