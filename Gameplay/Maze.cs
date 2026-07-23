namespace Dust;

internal enum Direction { Up, Right, Down, Left }

internal sealed class Maze
{
    private static readonly (int dx, int dy)[] Delta = [(0, -1), (1, 0), (0, 1), (-1, 0)];
    private readonly bool[,,] _walls;
    private readonly CargoRoom?[,] _roomByCell;
    private readonly List<CargoRoom> _rooms = [];
    private readonly IReadOnlyList<CargoRoom> _roomView;

    public int Width { get; }
    public int Height { get; }
    public MazeStrictness Strictness { get; }
    public IReadOnlyList<CargoRoom> Rooms => _roomView;

    public Maze(int width, int height, Random random, int? cargoRoomCount = null,
        MazeStrictness strictness = MazeStrictness.Normal)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(random);

        Width = width;
        Height = height;
        Strictness = strictness;
        _walls = new bool[width, height, 4];
        _roomByCell = new CargoRoom?[width, height];
        _roomView = _rooms.AsReadOnly();

        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        for (var d = 0; d < 4; d++)
            _walls[x, y, d] = true;

        Generate(random);
        var area = width * height;
        var loopCount = strictness switch
        {
            MazeStrictness.Strict => Math.Max(3, area / 45),
            MazeStrictness.Loose => Math.Max(20, area / 2),
            _ => Math.Max(12, area / 4)
        };
        var clearingCount = strictness switch
        {
            MazeStrictness.Strict => Math.Max(1, area / 520),
            MazeStrictness.Loose => Math.Max(8, area / 75),
            _ => Math.Max(5, area / 130)
        };
        AddLoops(random, loopCount);
        CarveClearings(random, clearingCount);
        var desiredRooms = cargoRoomCount ?? Math.Clamp(width * height / 280, 3, 7);
        GenerateCargoRooms(random, Math.Max(0, desiredRooms));
    }

    public bool HasWall(int x, int y, Direction direction) => _walls[x, y, (int)direction];

    public int GetOpeningMask(int x, int y)
    {
        var mask = 0;
        for (var d = 0; d < 4; d++)
            if (!_walls[x, y, d]) mask |= 1 << d;
        return mask;
    }

    public bool CanMove(Point cell, Direction direction)
    {
        // Rendering/QA probes and interrupted online reconciliation can briefly
        // ask about a position outside the plate. Treat it as sealed instead of
        // indexing the wall array with an invalid source cell.
        if (!IsInBounds(cell)) return false;
        var (dx, dy) = Delta[(int)direction];
        var nx = cell.X + dx;
        var ny = cell.Y + dy;
        return nx >= 0 && nx < Width && ny >= 0 && ny < Height && !HasWall(cell.X, cell.Y, direction);
    }

    public Point Move(Point cell, Direction direction)
    {
        var (dx, dy) = Delta[(int)direction];
        return new Point(cell.X + dx, cell.Y + dy);
    }

    public CargoRoom? GetRoomAt(Point cell) => IsInBounds(cell) ? _roomByCell[cell.X, cell.Y] : null;

    public bool TryGetRoomAt(Point cell, out CargoRoom room)
    {
        var found = GetRoomAt(cell);
        if (found is null)
        {
            room = null!;
            return false;
        }

        room = found;
        return true;
    }

    /// <summary>
    /// Identifies the exact outside-to-inside door crossing that should reveal a room.
    /// Merely standing beside a room or entering one of its cells by any other means does not qualify.
    /// </summary>
    public bool TryGetEnteredRoom(Point from, Point to, out CargoRoom room)
    {
        var found = GetRoomAt(to);
        if (found is null || !found.IsEntry(from, to))
        {
            room = null!;
            return false;
        }

        room = found;
        return true;
    }

    public Point FindFarthest(Point start)
    {
        var distances = new int[Width, Height];
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            distances[x, y] = -1;

        var queue = new Queue<Point>();
        queue.Enqueue(start);
        distances[start.X, start.Y] = 0;
        var farthest = start;

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            if (distances[cell.X, cell.Y] > distances[farthest.X, farthest.Y])
                farthest = cell;

            for (var d = 0; d < 4; d++)
            {
                var direction = (Direction)d;
                if (!CanMove(cell, direction)) continue;
                var next = Move(cell, direction);
                if (distances[next.X, next.Y] >= 0) continue;
                distances[next.X, next.Y] = distances[cell.X, cell.Y] + 1;
                queue.Enqueue(next);
            }
        }

        return farthest;
    }

    public void EnsureJunction(Point cell, Random random, int desiredOpenings)
    {
        while (CountOpenings(cell) < desiredOpenings)
        {
            var blocked = new List<Direction>(4);
            for (var d = 0; d < 4; d++)
            {
                var direction = (Direction)d;
                var (dx, dy) = Delta[d];
                var nx = cell.X + dx;
                var ny = cell.Y + dy;
                if (nx >= 0 && nx < Width && ny >= 0 && ny < Height &&
                    HasWall(cell.X, cell.Y, direction) &&
                    CanOpenWithoutBreachingRoom(cell, new Point(nx, ny)))
                    blocked.Add(direction);
            }
            if (blocked.Count == 0) break;
            RemoveWall(cell.X, cell.Y, blocked[random.Next(blocked.Count)]);
        }
    }

    private void Generate(Random random)
    {
        var visited = new bool[Width, Height];
        var first = new Point(random.Next(Width), random.Next(Height));
        visited[first.X, first.Y] = true;
        var frontier = new List<(Point From, Direction Direction, Point To)>();
        AddFrontier(first);

        while (frontier.Count > 0)
        {
            var index = random.Next(frontier.Count);
            var edge = frontier[index];
            frontier[index] = frontier[^1];
            frontier.RemoveAt(frontier.Count - 1);
            if (visited[edge.To.X, edge.To.Y]) continue;

            RemoveWall(edge.From.X, edge.From.Y, edge.Direction);
            visited[edge.To.X, edge.To.Y] = true;
            AddFrontier(edge.To);
        }

        void AddFrontier(Point cell)
        {
            for (var d = 0; d < 4; d++)
            {
                var (dx, dy) = Delta[d];
                var next = new Point(cell.X + dx, cell.Y + dy);
                if (next.X >= 0 && next.X < Width && next.Y >= 0 && next.Y < Height && !visited[next.X, next.Y])
                    frontier.Add((cell, (Direction)d, next));
            }
        }
    }

    private void CarveClearings(Random random, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var clearingWidth = random.Next(2, 5);
            var clearingHeight = random.Next(2, 4);
            var left = random.Next(1, Math.Max(2, Width - clearingWidth));
            var top = random.Next(1, Math.Max(2, Height - clearingHeight));
            for (var x = left; x < Math.Min(Width, left + clearingWidth); x++)
            for (var y = top; y < Math.Min(Height, top + clearingHeight); y++)
            {
                if (x < left + clearingWidth - 1 && x < Width - 1 && HasWall(x, y, Direction.Right))
                    RemoveWall(x, y, Direction.Right);
                if (y < top + clearingHeight - 1 && y < Height - 1 && HasWall(x, y, Direction.Down))
                    RemoveWall(x, y, Direction.Down);
            }
        }
    }

    private void AddLoops(Random random, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var x = random.Next(Width);
            var y = random.Next(Height);
            var possible = new List<Direction>();
            if (x < Width - 1 && HasWall(x, y, Direction.Right)) possible.Add(Direction.Right);
            if (y < Height - 1 && HasWall(x, y, Direction.Down)) possible.Add(Direction.Down);
            if (possible.Count > 0) RemoveWall(x, y, possible[random.Next(possible.Count)]);
        }
    }

    private void GenerateCargoRooms(Random random, int desiredCount)
    {
        if (desiredCount == 0 || Width < 9 || Height < 9) return;

        var maximumAttempts = Math.Max(120, desiredCount * 180);
        for (var attempt = 0; attempt < maximumAttempts && _rooms.Count < desiredCount; attempt++)
        {
            var candidate = CreateRoomCandidate(random);
            if (candidate is null || TouchesExistingRoom(candidate.Value.Cells)) continue;
            if (!OutsideRemainsConnected(candidate.Value.Cells)) continue;

            var doors = FindDoorCandidates(candidate.Value.Cells);
            if (doors.Count == 0) continue;
            var door = doors[random.Next(doors.Count)];

            StampRoom(candidate.Value.Cells, door);
            var room = new CargoRoom(
                _rooms.Count,
                candidate.Value.Shape,
                candidate.Value.Cells,
                door.RoomCell,
                door.ApproachCell,
                door.OutwardDirection);
            _rooms.Add(room);
            foreach (var cell in room.Cells)
                _roomByCell[cell.X, cell.Y] = room;
        }
    }

    private RoomCandidate? CreateRoomCandidate(Random random)
    {
        var shape = (CargoRoomShape)random.Next(3);
        int roomWidth;
        int roomHeight;
        var armThickness = 0;
        var orientation = 0;

        switch (shape)
        {
            case CargoRoomShape.Square:
                roomWidth = roomHeight = random.Next(4, 7);
                break;
            case CargoRoomShape.Rectangle:
            {
                var longSide = random.Next(6, 9);
                var shortSide = random.Next(3, 5);
                if (random.Next(2) == 0)
                {
                    roomWidth = longSide;
                    roomHeight = shortSide;
                }
                else
                {
                    roomWidth = shortSide;
                    roomHeight = longSide;
                }
                break;
            }
            default:
                roomWidth = random.Next(5, 8);
                roomHeight = random.Next(5, 8);
                armThickness = random.Next(2, Math.Min(4, Math.Min(roomWidth, roomHeight) - 1));
                orientation = random.Next(4);
                break;
        }

        const int edgeMargin = 2;
        var maximumLeft = Width - roomWidth - edgeMargin;
        var maximumTop = Height - roomHeight - edgeMargin;
        if (maximumLeft < edgeMargin || maximumTop < edgeMargin) return null;

        var left = random.Next(edgeMargin, maximumLeft + 1);
        var top = random.Next(edgeMargin, maximumTop + 1);
        var cells = new HashSet<Point>();

        for (var localX = 0; localX < roomWidth; localX++)
        for (var localY = 0; localY < roomHeight; localY++)
        {
            var included = shape != CargoRoomShape.LShape || orientation switch
            {
                0 => localX < armThickness || localY >= roomHeight - armThickness,
                1 => localX >= roomWidth - armThickness || localY >= roomHeight - armThickness,
                2 => localX < armThickness || localY < armThickness,
                _ => localX >= roomWidth - armThickness || localY < armThickness
            };
            if (included) cells.Add(new Point(left + localX, top + localY));
        }

        return new RoomCandidate(shape, cells);
    }

    private bool TouchesExistingRoom(HashSet<Point> candidate)
    {
        // A one-cell buffer prevents room walls and door markings from merging visually.
        foreach (var cell in candidate)
        for (var offsetX = -1; offsetX <= 1; offsetX++)
        for (var offsetY = -1; offsetY <= 1; offsetY++)
        {
            var x = cell.X + offsetX;
            var y = cell.Y + offsetY;
            if (x >= 0 && x < Width && y >= 0 && y < Height && _roomByCell[x, y] is not null)
                return true;
        }

        return false;
    }

    private bool OutsideRemainsConnected(HashSet<Point> candidate)
    {
        Point? first = null;
        var outsideCellCount = 0;
        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
        {
            var cell = new Point(x, y);
            if (candidate.Contains(cell) || _roomByCell[x, y] is not null) continue;
            first ??= cell;
            outsideCellCount++;
        }

        if (first is null) return false;

        var visited = new bool[Width, Height];
        var queue = new Queue<Point>();
        queue.Enqueue(first.Value);
        visited[first.Value.X, first.Value.Y] = true;
        var reached = 0;

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            reached++;
            for (var d = 0; d < 4; d++)
            {
                var direction = (Direction)d;
                if (!CanMove(cell, direction)) continue;
                var next = Move(cell, direction);
                if (visited[next.X, next.Y] || candidate.Contains(next) || _roomByCell[next.X, next.Y] is not null)
                    continue;
                visited[next.X, next.Y] = true;
                queue.Enqueue(next);
            }
        }

        return reached == outsideCellCount;
    }

    private List<DoorCandidate> FindDoorCandidates(HashSet<Point> cells)
    {
        var minX = cells.Min(cell => cell.X);
        var maxX = cells.Max(cell => cell.X);
        var minY = cells.Min(cell => cell.Y);
        var maxY = cells.Max(cell => cell.Y);
        var doors = new List<DoorCandidate>();

        foreach (var cell in cells.OrderBy(point => point.Y).ThenBy(point => point.X))
        for (var d = 0; d < 4; d++)
        {
            var direction = (Direction)d;
            var approach = Move(cell, direction);
            if (!IsInBounds(approach) || cells.Contains(approach)) continue;

            // L-room doors face the surrounding maze, never the inside of the L's notch.
            var facesOutsideBounds = approach.X < minX || approach.X > maxX ||
                                     approach.Y < minY || approach.Y > maxY;
            if (facesOutsideBounds)
                doors.Add(new DoorCandidate(cell, approach, direction));
        }

        return doors;
    }

    private void StampRoom(HashSet<Point> cells, DoorCandidate door)
    {
        foreach (var cell in cells)
        for (var d = 0; d < 4; d++)
        {
            var direction = (Direction)d;
            var next = Move(cell, direction);
            if (!IsInBounds(next)) continue;
            if (cells.Contains(next)) RemoveWall(cell.X, cell.Y, direction);
            else AddWall(cell.X, cell.Y, direction);
        }

        RemoveWall(door.RoomCell.X, door.RoomCell.Y, door.OutwardDirection);
    }

    private bool CanOpenWithoutBreachingRoom(Point from, Point to)
    {
        var fromRoom = GetRoomAt(from);
        var toRoom = GetRoomAt(to);
        if (ReferenceEquals(fromRoom, toRoom)) return true;
        return fromRoom?.IsDoorTransition(from, to) == true || toRoom?.IsDoorTransition(from, to) == true;
    }

    private bool IsInBounds(Point cell) =>
        cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height;

    private void RemoveWall(int x, int y, Direction direction)
    {
        var next = Move(new Point(x, y), direction);
        _walls[x, y, (int)direction] = false;
        _walls[next.X, next.Y, ((int)direction + 2) % 4] = false;
    }

    private void AddWall(int x, int y, Direction direction)
    {
        var next = Move(new Point(x, y), direction);
        _walls[x, y, (int)direction] = true;
        _walls[next.X, next.Y, ((int)direction + 2) % 4] = true;
    }

    private int CountOpenings(Point cell)
    {
        var count = 0;
        for (var d = 0; d < 4; d++)
            if (CanMove(cell, (Direction)d)) count++;
        return count;
    }

    private readonly record struct RoomCandidate(CargoRoomShape Shape, HashSet<Point> Cells);
    private readonly record struct DoorCandidate(Point RoomCell, Point ApproachCell, Direction OutwardDirection);
}
