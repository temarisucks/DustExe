namespace Dust;

internal sealed partial class GameForm
{
    private void CheckHollowCollision()
    {
        if (_mode != ScreenMode.Playing || _invulnerability > 0 || _hitEffect > 0) return;
        if (IsOnlineGameplayActive &&
            (!IsOnlineSimulationHost || IsOnlineLocalPlayerProtected)) return;
        foreach (var hollow in _hollows)
        {
            if (hollow.Type == HollowType.Camera) continue;
            var visualContact = HollowMakesContact(
                hollow, _previousVisualCell, _visualCell);
            var settledTogether = !hollow.TriangleSplit &&
                                  _moveProgress >= 1 && !hollow.IsMoving &&
                                  _playerCell == hollow.Cell;
            if (!visualContact && !settledTogether) continue;
            BeginHollowHit(HollowContactDamage(hollow));
            break;
        }
    }

    private static bool HollowMakesContact(
        Hollow hollow,
        PointF playerFrom,
        PointF playerTo)
    {
        if (hollow.Type != HollowType.Triangle || !hollow.TriangleSplit)
            return SweptSeparationSquared(
                playerFrom, playerTo,
                hollow.PreviousVisualCell, hollow.VisualCell) <= .27f;

        var previousMembers = PreviousTriangleMemberPositions(hollow);
        var currentMembers = TriangleMemberPositions(hollow);
        for (var index = 0; index < currentMembers.Length; index++)
            if (SweptSeparationSquared(
                    playerFrom, playerTo,
                    previousMembers[index], currentMembers[index]) <= .27f)
                return true;
        return false;
    }

    private static int HollowContactDamage(Hollow hollow) =>
        hollow.Type == HollowType.Square && hollow.Empowered ? 2 : 1;

    private static float SweptSeparationSquared(PointF playerFrom, PointF playerTo,
        PointF hollowFrom, PointF hollowTo)
    {
        var startX = hollowFrom.X - playerFrom.X;
        var startY = hollowFrom.Y - playerFrom.Y;
        var velocityX = (hollowTo.X - hollowFrom.X) - (playerTo.X - playerFrom.X);
        var velocityY = (hollowTo.Y - hollowFrom.Y) - (playerTo.Y - playerFrom.Y);
        var velocitySquared = velocityX * velocityX + velocityY * velocityY;
        var time = velocitySquared <= .000001f
            ? 0
            : Math.Clamp(-(startX * velocityX + startY * velocityY) / velocitySquared, 0, 1);
        var closestX = startX + velocityX * time;
        var closestY = startY + velocityY * time;
        return closestX * closestX + closestY * closestY;
    }

    private void BeginHollowHit(int damage = 1, bool causedByHollow = true)
    {
        if (TryConsumeShopProtection()) return;
        var wasCarryingCargo = _cargoItems.Any(item =>
            IsOnlineGameplayActive
                ? item.CarrierPlayerId == _onlinePlayerId
                : item.Carried);
        DropCarriedCargo();
        damage = Math.Max(1, damage);
        _damageTaken += damage;
        _totalDamageSustained += damage;
        RecordHitForAchievements(causedByHollow);
        _damageTaken = Math.Min(_damageTaken, GetMaximumHealth());
        _failurePending = _damageTaken >= GetMaximumHealth();
        if (_failurePending) _cargoLostOnFailure = wasCarryingCargo;
        _hitEffect = 1.16f;
        _invulnerability = 2.4f;
        _teleportDone = _failurePending;
        _pendingWin = false;
        CancelPendingTraversal();
        _moveFrom = _visualCell;
        _moveTo = _visualCell;
        _moveProgress = 1;
        _movementArrivalHandled = true;
        _droneBank = 0;
        _dronePitch = 0;
    }

    private void TeleportPlayerToSafety()
    {
        if (_maze is null) return;
        var threatDistance = BuildHollowDistanceMap();
        var safe = new List<Point>();
        var fallback = new List<Point>();
        var fallbackScore = int.MinValue;
        var oldLogicalCell = _playerCell;
        var oldVisualCell = new Point((int)MathF.Round(_visualCell.X), (int)MathF.Round(_visualCell.Y));
        for (var x = 0; x < _maze.Width; x++)
        for (var y = 0; y < _maze.Height; y++)
        {
            var cell = new Point(x, y);
            if (cell == _exitCell || cell == oldLogicalCell || cell == oldVisualCell) continue;
            if (IsRoomDecorationBlockingCell(cell)) continue;
            if (IsSurvivorBlockingCell(cell)) continue;
            if (_maze.GetRoomAt(cell) is not null) continue;
            if (_hollows.Any(hollow => HollowOccupiesCell(hollow, cell))) continue;
            if (_sentries.Any(sentry => sentry.Cell == cell)) continue;
            var openings = CountBits(_maze.GetOpeningMask(x, y));
            var distance = threatDistance[x, y] < 0 ? 999 : threatDistance[x, y];
            var visible = _hollows.Any(hollow =>
                CanHollowSeeFrom(hollow, hollow.VisualCell, cell) ||
                CanHollowSeeFrom(hollow, hollow.Cell, cell) ||
                (hollow.TargetCell != hollow.Cell && CanHollowSeeFrom(hollow, hollow.TargetCell, cell))) ||
                _sentries.Any(sentry =>
                    sentry.Phase == SentryPhase.Scanning && CanSentrySeePosition(sentry, cell));
            var score = distance * 10 + openings * 3 - (visible ? 5000 : 0) -
                        (Manhattan(cell, _exitCell) < 3 ? 200 : 0);
            if (score > fallbackScore)
            {
                fallbackScore = score;
                fallback.Clear();
                fallback.Add(cell);
            }
            else if (score == fallbackScore)
            {
                fallback.Add(cell);
            }
            if (distance < 8 || visible || openings < 2 || Manhattan(cell, _exitCell) < 3) continue;
            safe.Add(cell);
        }
        var destination = safe.Count > 0
            ? safe[_random.Next(safe.Count)]
            : fallback.Count > 0 ? fallback[_random.Next(fallback.Count)] : oldLogicalCell;
        _playerCell = destination;
        _playerPreviousCell = destination;
        _visualCell = destination;
        _previousVisualCell = destination;
        _moveFrom = destination;
        _moveTo = destination;
        _cameraCell = destination;
        _moveProgress = 1;
        _movementArrivalHandled = true;
        _droneBank = 0;
        _dronePitch = 0;
        _pendingWin = false;
        _visited.Add(destination);
        _teleportDone = true;
        foreach (var hollow in _hollows)
        {
            hollow.HasSight = false;
            hollow.SenseCooldown = 0;
        }
    }

    private int[,] BuildHollowDistanceMap()
    {
        if (_maze is null) return new int[0, 0];
        var distance = new int[_maze.Width, _maze.Height];
        for (var x = 0; x < _maze.Width; x++)
        for (var y = 0; y < _maze.Height; y++)
            distance[x, y] = -1;
        var queue = new Queue<Point>();
        foreach (var hollow in _hollows)
        {
            if (hollow.TriangleSplit && hollow.TriangleMembers.Count == 3)
            {
                foreach (var member in hollow.TriangleMembers)
                {
                    AddSource(member.Cell);
                    AddSource(member.TargetCell);
                }
            }
            else
            {
                AddSource(hollow.Cell);
                AddSource(hollow.TargetCell);
            }
        }
        foreach (var sentry in _sentries) AddSource(sentry.Cell);
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var direction in AllDirections)
            {
                if (!_maze.CanMove(cell, direction)) continue;
                var next = _maze.Move(cell, direction);
                if (distance[next.X, next.Y] >= 0) continue;
                distance[next.X, next.Y] = distance[cell.X, cell.Y] + 1;
                queue.Enqueue(next);
            }
        }
        return distance;

        void AddSource(Point cell)
        {
            if (distance[cell.X, cell.Y] >= 0) return;
            distance[cell.X, cell.Y] = 0;
            queue.Enqueue(cell);
        }
    }

    private static int CountBits(int value)
    {
        var count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }
        return count;
    }

    private static int Manhattan(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}
