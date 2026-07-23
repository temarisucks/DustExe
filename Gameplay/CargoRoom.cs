namespace Dust;

internal enum CargoRoomShape
{
    Square,
    Rectangle,
    LShape
}

/// <summary>
/// A contiguous set of maze cells whose perimeter has exactly one opening.
/// DoorCell is inside the room; DoorApproachCell is the adjacent maze cell outside it.
/// </summary>
internal sealed class CargoRoom
{
    private readonly HashSet<Point> _cellLookup;
    private readonly IReadOnlyList<Point> _cells;

    public int Id { get; }
    public CargoRoomShape Shape { get; }
    public IReadOnlyList<Point> Cells => _cells;
    public Rectangle Bounds { get; }
    public Point DoorCell { get; }
    public Point DoorApproachCell { get; }

    /// <summary>The direction from DoorCell toward DoorApproachCell.</summary>
    public Direction DoorOutwardDirection { get; }

    /// <summary>The direction a drone travels from DoorApproachCell into DoorCell.</summary>
    public Direction EntryDirection => (Direction)(((int)DoorOutwardDirection + 2) % 4);

    internal CargoRoom(
        int id,
        CargoRoomShape shape,
        IEnumerable<Point> cells,
        Point doorCell,
        Point doorApproachCell,
        Direction doorOutwardDirection)
    {
        Id = id;
        Shape = shape;

        var orderedCells = cells
            .Distinct()
            .OrderBy(cell => cell.Y)
            .ThenBy(cell => cell.X)
            .ToArray();
        if (orderedCells.Length == 0)
            throw new ArgumentException("A cargo room must contain at least one cell.", nameof(cells));

        _cellLookup = new HashSet<Point>(orderedCells);
        _cells = Array.AsReadOnly(orderedCells);
        if (!_cellLookup.Contains(doorCell))
            throw new ArgumentException("The door cell must be inside the cargo room.", nameof(doorCell));
        if (_cellLookup.Contains(doorApproachCell))
            throw new ArgumentException("The door approach must be outside the cargo room.", nameof(doorApproachCell));

        var expectedApproach = Step(doorCell, doorOutwardDirection);
        if (expectedApproach != doorApproachCell)
            throw new ArgumentException("The door cells must be adjacent and match the outward direction.");

        DoorCell = doorCell;
        DoorApproachCell = doorApproachCell;
        DoorOutwardDirection = doorOutwardDirection;

        var minX = orderedCells.Min(cell => cell.X);
        var minY = orderedCells.Min(cell => cell.Y);
        var maxX = orderedCells.Max(cell => cell.X);
        var maxY = orderedCells.Max(cell => cell.Y);
        Bounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    public bool Contains(Point cell) => _cellLookup.Contains(cell);

    /// <summary>True only for the outside-to-inside move that reveals this room.</summary>
    public bool IsEntry(Point from, Point to) => from == DoorApproachCell && to == DoorCell;

    public bool IsExit(Point from, Point to) => from == DoorCell && to == DoorApproachCell;

    public bool IsDoorTransition(Point from, Point to) => IsEntry(from, to) || IsExit(from, to);

    private static Point Step(Point cell, Direction direction) => direction switch
    {
        Direction.Up => new Point(cell.X, cell.Y - 1),
        Direction.Right => new Point(cell.X + 1, cell.Y),
        Direction.Down => new Point(cell.X, cell.Y + 1),
        Direction.Left => new Point(cell.X - 1, cell.Y),
        _ => cell
    };
}
