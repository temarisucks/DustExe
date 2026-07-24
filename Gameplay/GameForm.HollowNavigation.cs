namespace Dust;

internal sealed partial class GameForm
{
    private Point? ChooseRoamStep(Hollow hollow, Point? center, int radius)
    {
        if (_maze is null) return null;
        var choices = new List<Point>(8);
        var travelX = hollow.Cell.X - hollow.PreviousCell.X;
        var travelY = hollow.Cell.Y - hollow.PreviousCell.Y;
        foreach (var direction in AllDirections)
        {
            if (!_maze.CanMove(hollow.Cell, direction)) continue;
            var next = _maze.Move(hollow.Cell, direction);
            if (IsCellConcealed(next)) continue;
            if (IsRoomDecorationBlockingCell(next)) continue;
            if (IsOccupiedByOtherHollow(hollow, next)) continue;
            if (center.HasValue && GraphDistance(next, center.Value, radius) < 0) continue;
            choices.Add(next);
            if (next.X - hollow.Cell.X == travelX && next.Y - hollow.Cell.Y == travelY)
            {
                choices.Add(next);
                choices.Add(next);
            }
        }
        if (choices.Any(cell => cell != hollow.PreviousCell))
            choices.RemoveAll(cell => cell == hollow.PreviousCell);
        return choices.Count == 0 ? null : choices[_random.Next(choices.Count)];
    }

    private int GraphDistance(Point start, Point target, int maximum)
    {
        if (_maze is null) return -1;
        if (start == target) return 0;
        var distance = new int[_maze.Width, _maze.Height];
        for (var x = 0; x < _maze.Width; x++)
        for (var y = 0; y < _maze.Height; y++)
            distance[x, y] = -1;
        var queue = new Queue<Point>();
        queue.Enqueue(start);
        distance[start.X, start.Y] = 0;
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            var nextDistance = distance[cell.X, cell.Y] + 1;
            if (nextDistance > maximum) continue;
            foreach (var direction in AllDirections)
            {
                if (!_maze.CanMove(cell, direction)) continue;
                var next = _maze.Move(cell, direction);
                if (IsCellConcealed(next)) continue;
                if (IsRoomDecorationBlockingCell(next)) continue;
                if (distance[next.X, next.Y] >= 0) continue;
                if (next == target) return nextDistance;
                distance[next.X, next.Y] = nextDistance;
                queue.Enqueue(next);
            }
        }
        return -1;
    }

    private Point? FindNextPathStep(Hollow hollow, Point target)
    {
        if (_maze is null || hollow.Cell == target) return null;
        var visited = new bool[_maze.Width, _maze.Height];
        var previous = new Point[_maze.Width, _maze.Height];
        var queue = new Queue<Point>();
        queue.Enqueue(hollow.Cell);
        visited[hollow.Cell.X, hollow.Cell.Y] = true;
        var found = false;
        while (queue.Count > 0 && !found)
        {
            var cell = queue.Dequeue();
            foreach (var direction in AllDirections)
            {
                if (!_maze.CanMove(cell, direction)) continue;
                var next = _maze.Move(cell, direction);
                if (IsCellConcealed(next)) continue;
                if (IsRoomDecorationBlockingCell(next)) continue;
                if (visited[next.X, next.Y]) continue;
                if (IsOccupiedByOtherHollow(hollow, next)) continue;
                visited[next.X, next.Y] = true;
                previous[next.X, next.Y] = cell;
                if (next == target) { found = true; break; }
                queue.Enqueue(next);
            }
        }
        if (!found) return null;
        var cursor = target;
        while (previous[cursor.X, cursor.Y] != hollow.Cell)
            cursor = previous[cursor.X, cursor.Y];
        return cursor;
    }

    private bool CanHollowSee(Hollow hollow, PointF target) =>
        CanHollowSeeFrom(hollow, hollow.VisualCell, target, hollow.HasSight);

    private bool CanHollowSeeFrom(Hollow hollow, PointF from, PointF target, bool retainSight = false)
    {
        var dx = target.X - from.X;
        var dy = target.Y - from.Y;
        var distanceSquared = dx * dx + dy * dy;
        if (distanceSquared <= .001f) return true;

        var retainedRange = HollowViewRange(hollow, retainSight);
        if (distanceSquared > retainedRange * retainedRange) return false;
        var targetAngle = MathF.Atan2(dy, dx);
        var retainedField = HollowFieldOfView(hollow, retainSight);
        if (Math.Abs(NormalizeAngle(targetAngle - hollow.FacingAngle)) > retainedField / 2) return false;
        if (HollowIgnoresVisionWalls(hollow)) return true;

        var distance = MathF.Sqrt(distanceSquared);
        var clearDistance = RaycastVisionDistance(from, targetAngle, distance, false);
        return clearDistance >= distance - .06f;
    }

    private static float HollowViewRange(Hollow hollow, bool retainSight) =>
        hollow.ViewDistance + (retainSight ? .12f : 0);

    private static float HollowFieldOfView(Hollow hollow, bool retainSight) =>
        hollow.FieldOfView + (retainSight ? 6 * MathF.PI / 180 : 0);

    private static bool HollowIgnoresVisionWalls(Hollow hollow) =>
        hollow.Type == HollowType.Hex ||
        hollow.Empowered && hollow.Type is HollowType.Triangle or HollowType.Star;

    private float RaycastVisionDistance(PointF origin, float angle, float maximum, bool ignoreWalls)
    {
        if (_maze is null || maximum <= 0) return 0;
        var rayX = MathF.Cos(angle);
        var rayY = MathF.Sin(angle);
        var stepX = Math.Abs(rayX) < .000001f ? 0 : Math.Sign(rayX);
        var stepY = Math.Abs(rayY) < .000001f ? 0 : Math.Sign(rayY);
        var current = PositionCell(origin);
        if (!InsideMaze(current)) return 0;

        var tDeltaX = stepX == 0 ? float.PositiveInfinity : 1 / Math.Abs(rayX);
        var tDeltaY = stepY == 0 ? float.PositiveInfinity : 1 / Math.Abs(rayY);
        var boundaryX = stepX > 0 ? current.X + .5f : current.X - .5f;
        var boundaryY = stepY > 0 ? current.Y + .5f : current.Y - .5f;
        var tMaxX = stepX == 0 ? float.PositiveInfinity : (boundaryX - origin.X) / rayX;
        var tMaxY = stepY == 0 ? float.PositiveInfinity : (boundaryY - origin.Y) / rayY;

        while (true)
        {
            var crossingDistance = Math.Min(tMaxX, tMaxY);
            if (crossingDistance > maximum) return maximum;

            Point next;
            if (Math.Abs(tMaxX - tMaxY) < .00001f)
            {
                next = new Point(current.X + stepX, current.Y + stepY);
                if (!InsideMaze(next)) return Math.Max(0, crossingDistance - .001f);
                if (!ignoreWalls && !CanCrossVisionBoundary(current, next))
                    return Math.Max(0, crossingDistance - .001f);
                tMaxX += tDeltaX;
                tMaxY += tDeltaY;
            }
            else if (tMaxX < tMaxY)
            {
                next = new Point(current.X + stepX, current.Y);
                if (!InsideMaze(next)) return Math.Max(0, crossingDistance - .001f);
                if (!ignoreWalls && !CanCrossVisionBoundary(current, next))
                    return Math.Max(0, crossingDistance - .001f);
                tMaxX += tDeltaX;
            }
            else
            {
                next = new Point(current.X, current.Y + stepY);
                if (!InsideMaze(next)) return Math.Max(0, crossingDistance - .001f);
                if (!ignoreWalls && !CanCrossVisionBoundary(current, next))
                    return Math.Max(0, crossingDistance - .001f);
                tMaxY += tDeltaY;
            }
            current = next;
        }
    }

    private bool CanCrossVisionBoundary(Point from, Point to)
    {
        if (_maze is null) return false;
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1) return false;
        if (dx != 0 && dy != 0)
        {
            var xDirection = dx > 0 ? Direction.Right : Direction.Left;
            var yDirection = dy > 0 ? Direction.Down : Direction.Up;
            if (!_maze.CanMove(from, xDirection) || !_maze.CanMove(from, yDirection)) return false;
            var acrossX = _maze.Move(from, xDirection);
            var acrossY = _maze.Move(from, yDirection);
            return _maze.CanMove(acrossX, yDirection) && _maze.CanMove(acrossY, xDirection);
        }
        if (dx != 0) return _maze.CanMove(from, dx > 0 ? Direction.Right : Direction.Left);
        if (dy != 0) return _maze.CanMove(from, dy > 0 ? Direction.Down : Direction.Up);
        return true;
    }

    private Point PositionCell(PointF position) =>
        new((int)MathF.Floor(position.X + .5f), (int)MathF.Floor(position.Y + .5f));

    private bool InsideMaze(Point cell) =>
        _maze is not null && cell.X >= 0 && cell.X < _maze.Width && cell.Y >= 0 && cell.Y < _maze.Height;

    private static float DirectionAngle(Direction direction) => direction switch
    {
        Direction.Up => -MathF.PI / 2,
        Direction.Right => 0,
        Direction.Down => MathF.PI / 2,
        _ => MathF.PI
    };

    private static float RotateTowards(float current, float target, float maximumStep)
    {
        var delta = NormalizeAngle(target - current);
        if (Math.Abs(delta) <= maximumStep) return NormalizeAngle(target);
        return NormalizeAngle(current + Math.Sign(delta) * maximumStep);
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI) angle -= MathF.PI * 2;
        while (angle <= -MathF.PI) angle += MathF.PI * 2;
        return angle;
    }

    private bool IsOccupiedByOtherHollow(Hollow self, Point cell) =>
        _hollows.Any(other => other != self && (other.Cell == cell || other.TargetCell == cell)) ||
        _sentries.Any(sentry => sentry.Cell == cell);
}
