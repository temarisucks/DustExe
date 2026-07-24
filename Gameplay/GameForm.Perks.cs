namespace Dust;

internal sealed partial class GameForm
{
    private const float CamouflageDuration = 4.25f;
    private const float CamouflageRecharge = 14f;
    private const float GhostFormDuration = 3.5f;
    private const float GhostFormRecharge = 18f;
    private const float HollowKillerPulseDuration = .78f;
    private const float HollowKillerRecharge = 45f;
    private const float HollowKillerRadius = 4f;
    private const int MoneyMagnetRange = 5;
    private const float MoneyMagnetSpeed = 1.05f;

    private readonly List<Point> _pendingTraversalCells = [];
    private readonly List<(Point From, Point To)> _retraceSegments = [];
    private readonly HashSet<(Point From, Point To)> _retraceEdges = [];
    private int _pendingTraversalIndex;
    private float _camouflageTimer;
    private float _camouflageCooldown;
    private float _ghostFormTimer;
    private float _ghostFormCooldown;
    private float _hollowKillerPulse;
    private float _hollowKillerCooldown;
    private PointF _hollowKillerCenter;
    private float _moneyMagnetSenseTimer;
    private bool _pendingMovementUsedGhostForm;

    private bool IsCamouflaged => _camouflageTimer > 0;
    private bool IsGhostFormActive => _ghostFormTimer > 0;
    private bool IsHollowKillerPulseActive => _hollowKillerPulse > 0;
    private bool IsPlayerInvisibleToEnemies => IsCamouflaged;
    private bool HasSpacePerk =>
        _settings.HasEquippedPerk(PerkId.GhostForm) ||
        _settings.HasEquippedPerk(PerkId.Camouflage) ||
        _settings.HasEquippedPerk(PerkId.HollowKiller);

    private int GetMaximumHealth() =>
        _settings.HasEquippedPerk(PerkId.Durable) ? 5 : 3;

    private void ResetPerkRunState()
    {
        _camouflageTimer = 0;
        _camouflageCooldown = 0;
        _ghostFormTimer = 0;
        _ghostFormCooldown = 0;
        _hollowKillerPulse = 0;
        _hollowKillerCooldown = 0;
        _hollowKillerCenter = PointF.Empty;
        _moneyMagnetSenseTimer = 0;
        _pendingMovementUsedGhostForm = false;
        _pendingTraversalIndex = 0;
        _pendingTraversalCells.Clear();
        _retraceSegments.Clear();
        _retraceEdges.Clear();
    }

    private void UpdatePerks(float deltaTime)
    {
        if (IsOnlineGameplayActive && !IsOnlineSimulationHost) return;
        _camouflageTimer = Math.Max(0, _camouflageTimer - deltaTime);
        _camouflageCooldown = Math.Max(0, _camouflageCooldown - deltaTime);
        _ghostFormTimer = Math.Max(0, _ghostFormTimer - deltaTime);
        _ghostFormCooldown = Math.Max(0, _ghostFormCooldown - deltaTime);
        _hollowKillerPulse = Math.Max(0, _hollowKillerPulse - deltaTime);
        _hollowKillerCooldown = Math.Max(0, _hollowKillerCooldown - deltaTime);
        UpdateMoneyMagnet(deltaTime);
    }

    /// <summary>
    /// Handles the shared active-perk key. Ghost Form deliberately wins if a profile has both
    /// Space abilities equipped, so a malformed/legacy load can never activate both at once.
    /// </summary>
    private bool TryActivateSpacePerk()
    {
        if (_mode != ScreenMode.Playing || _hitEffect > 0 || _pendingWin) return HasSpacePerk;
        if (IsOnlineGameplayActive && _onlineLocalDefeated) return HasSpacePerk;
        if (RelayOnlinePerkActivation()) return HasSpacePerk;

        if (_settings.HasEquippedPerk(PerkId.HollowKiller))
        {
            if (_hollowKillerCooldown > 0)
            {
                SetPerkNotice($"VOID PULSE RECYCLE {_hollowKillerCooldown:00.0}");
                return true;
            }

            ActivateHollowKiller();
            return true;
        }

        if (_settings.HasEquippedPerk(PerkId.GhostForm))
        {
            if (IsGhostFormActive)
            {
                SetPerkNotice("PHASE COIL ALREADY OPEN");
                return true;
            }
            if (_ghostFormCooldown > 0)
            {
                SetPerkNotice($"PHASE COIL RECYCLE {_ghostFormCooldown:00.0}");
                return true;
            }

            _ghostFormTimer = GhostFormDuration;
            _ghostFormCooldown = GhostFormRecharge;
            SetPerkNotice("PHASE COIL OPEN / 03.5 SEC");
            _audio.Play(AudioCue.Confirm);
            return true;
        }

        if (!_settings.HasEquippedPerk(PerkId.Camouflage)) return false;
        if (IsCamouflaged)
        {
            SetPerkNotice("OPTIC VEIL ALREADY ACTIVE");
            return true;
        }
        if (_camouflageCooldown > 0)
        {
            SetPerkNotice($"OPTIC VEIL RECYCLE {_camouflageCooldown:00.0}");
            return true;
        }

        _camouflageTimer = CamouflageDuration;
        _camouflageCooldown = CamouflageRecharge;
        SetPerkNotice("OPTIC VEIL ACTIVE / 04.2 SEC");
        _audio.Play(AudioCue.Confirm);
        return true;
    }

    private void ActivateHollowKiller()
    {
        _hollowKillerCenter = _visualCell;
        _hollowKillerPulse = HollowKillerPulseDuration;
        _hollowKillerCooldown = HollowKillerRecharge;
        var radiusSquared = HollowKillerRadius * HollowKillerRadius;
        var hollowCount = _hollows.RemoveAll(hollow =>
            PerkDistanceSquared(hollow.VisualCell, _hollowKillerCenter) <= radiusSquared);
        var sentryCount = _sentries.RemoveAll(sentry =>
            PerkDistanceSquared(sentry.Cell, _hollowKillerCenter) <= radiusSquared);

        // Orphaned rounds inside the protection field disappear with their source signal.
        _sentryProjectiles.RemoveAll(projectile =>
            PerkDistanceSquared(projectile.Position, _hollowKillerCenter) <= radiusSquared);
        var removed = hollowCount + sentryCount;
        SetPerkNotice(removed == 0
            ? "VOID PULSE / NO CONTACT"
            : $"VOID PULSE / {removed:00} CONTACT{(removed == 1 ? string.Empty : "S")} ERASED");
        _audio.Play(AudioCue.Confirm);
    }

    private static float PerkDistanceSquared(PointF first, PointF second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }

    private void SetPerkNotice(string text)
    {
        _missionNotice = text;
        _missionNoticeTimer = 1.65f;
    }

    private List<Point> BuildPlayerTraversal(Direction direction, out bool usedGhostForm)
    {
        var traversal = new List<Point>(2);
        usedGhostForm = false;
        if (_maze is null) return traversal;

        var cursor = _playerCell;
        var distance = _settings.HasEquippedPerk(PerkId.Hop) ? 2 : 1;
        for (var step = 0; step < distance; step++)
        {
            var normallyOpen = _maze.CanMove(cursor, direction);
            var destination = _maze.Move(cursor, direction);
            var canPhase = IsGhostFormActive && InsideMaze(destination);
            if (!normallyOpen && !canPhase) break;
            // Phase coils open wall boundaries; they do not dematerialize
            // installed machinery. A decoration tile is always solid.
            if (IsRoomDecorationBlockingCell(destination)) break;
            if (IsSurvivorBlockingCell(destination)) break;

            usedGhostForm |= !normallyOpen;
            traversal.Add(destination);
            cursor = destination;

            // Never hop over the receiver. Touching it completes the run.
            if (cursor == _exitCell) break;
        }
        return traversal;
    }

    private void BeginPlayerTraversal(Point start, IReadOnlyList<Point> traversal, bool usedGhostForm)
    {
        _pendingTraversalCells.Clear();
        _pendingTraversalCells.AddRange(traversal);
        _pendingMovementUsedGhostForm = usedGhostForm;
        _pendingTraversalIndex = 0;
    }

    private void AdvancePlayerTraversalArrivals()
    {
        if (_pendingTraversalCells.Count == 0 || _pendingTraversalIndex >= _pendingTraversalCells.Count)
            return;
        var visualCell = PositionCell(_visualCell);
        var reachedIndex = _pendingTraversalCells.FindIndex(_pendingTraversalIndex,
            cell => cell == visualCell);
        if (reachedIndex < _pendingTraversalIndex) return;

        while (_pendingTraversalIndex <= reachedIndex)
        {
            var cell = _pendingTraversalCells[_pendingTraversalIndex];
            var from = _pendingTraversalIndex == 0
                ? _playerPreviousCell
                : _pendingTraversalCells[_pendingTraversalIndex - 1];
            _visited.Add(cell);
            if (_settings.HasEquippedPerk(PerkId.Retracer))
            {
                var edge = NormalizeRetraceEdge(from, cell);
                if (_retraceEdges.Add(edge)) _retraceSegments.Add((from, cell));
            }
            OnPlayerEnteredCell(from, cell);
            if (_pendingMovementUsedGhostForm)
                RevealRoomAfterPhaseEntry(from, cell);
            _pendingTraversalIndex++;
        }

        if (_pendingTraversalIndex < _pendingTraversalCells.Count) return;
        _pendingTraversalCells.Clear();
        _pendingTraversalIndex = 0;
        _pendingMovementUsedGhostForm = false;
        _movementArrivalHandled = true;
    }

    private void RevealRoomAfterPhaseEntry(Point from, Point to)
    {
        if (_maze is null || !_maze.TryGetRoomAt(to, out var room)) return;
        if (_maze.GetRoomAt(from)?.Id == room.Id || !_revealedRoomIds.Add(room.Id)) return;

        _lastRevealedDoor = to;
        _roomRevealPulse = 1;
        SetPerkNotice($"ROOM {room.Id + 1:00} / PHASE BREACH");
    }

    private void CancelPendingTraversal()
    {
        _pendingTraversalCells.Clear();
        _pendingTraversalIndex = 0;
        _pendingMovementUsedGhostForm = false;
    }

    private static (Point From, Point To) NormalizeRetraceEdge(Point first, Point second) =>
        first.X < second.X || first.X == second.X && first.Y <= second.Y
            ? (first, second)
            : (second, first);

    private int PlayerDroneAlpha()
    {
        if (IsCamouflaged)
            return 48 + (int)((MathF.Sin(_time * 21f) + 1) * 10);
        if (IsGhostFormActive)
            return 126 + (int)((MathF.Sin(_time * 15f) + 1) * 18);
        return 255;
    }

    private int PlayerShadowAlpha()
    {
        if (IsCamouflaged) return 30;
        if (IsGhostFormActive) return 92;
        return 255;
    }

    private void UpdateMoneyMagnet(float deltaTime)
    {
        if (_maze is null) return;
        var magnetPositions = new List<PointF>();
        if (!IsOnlineGameplayActive)
        {
            if (_settings.HasEquippedPerk(PerkId.MoneyMagnet))
                magnetPositions.Add(_visualCell);
        }
        else if (IsOnlineSimulationHost)
        {
            if (_settings.HasEquippedPerk(PerkId.MoneyMagnet) &&
                !_onlineLocalDefeated && _mode == ScreenMode.Playing)
                magnetPositions.Add(_visualCell);
            magnetPositions.AddRange(_onlinePlayers.Values
                .Where(player =>
                    player.Connected && !player.Defeated && !player.Extracted &&
                    !player.InShop && player.HasPerk(PerkId.MoneyMagnet))
                .Select(player => player.VisualCell));
        }
        if (magnetPositions.Count == 0) return;

        _moneyMagnetSenseTimer -= deltaTime;
        int[,]? distanceToPlayer = null;
        if (_moneyMagnetSenseTimer <= 0)
        {
            _moneyMagnetSenseTimer = .14f;
            distanceToPlayer = BuildMoneyMagnetDistanceMap(
                magnetPositions.Select(PositionCell).Distinct().ToArray(),
                MoneyMagnetRange);
        }

        var collected = new List<CreditPickup>();
        foreach (var pickup in _creditPickups)
        {
            if (pickup.Collected || IsCellConcealed(pickup.Cell)) continue;

            if (pickup.MagnetMoving)
            {
                pickup.MagnetProgress = Math.Min(1, pickup.MagnetProgress + deltaTime * MoneyMagnetSpeed);
                var eased = 1 - MathF.Pow(1 - pickup.MagnetProgress, 2);
                pickup.VisualCell = new PointF(
                    pickup.Cell.X + (pickup.MagnetTargetCell.X - pickup.Cell.X) * eased,
                    pickup.Cell.Y + (pickup.MagnetTargetCell.Y - pickup.Cell.Y) * eased);
                if (pickup.MagnetProgress >= 1)
                {
                    pickup.Cell = pickup.MagnetTargetCell;
                    pickup.VisualCell = pickup.Cell;
                    pickup.MagnetMoving = false;
                }
            }
            else
            {
                pickup.VisualCell = pickup.Cell;
                var next = distanceToPlayer is null
                    ? null
                    : FindMoneyMagnetStep(pickup.Cell, distanceToPlayer);
                if (next.HasValue)
                {
                    pickup.MagnetTargetCell = next.Value;
                    pickup.MagnetProgress = 0;
                    pickup.MagnetMoving = true;
                }
            }

            if (magnetPositions.Any(position =>
                    PerkDistanceSquared(pickup.VisualCell, position) <= .13f))
                collected.Add(pickup);
        }

        if (collected.Count == 0) return;
        var amount = collected.Sum(pickup => pickup.Value);
        foreach (var pickup in collected) pickup.Collected = true;
        _fieldCredits += amount;
        SetPerkNotice($"MAGNETIC RECOVERY +{amount:000}");
        _audio.Play(AudioCue.Collect);
    }

    private int[,] BuildMoneyMagnetDistanceMap(Point player, int maximumDistance)
        => BuildMoneyMagnetDistanceMap([player], maximumDistance);

    private int[,] BuildMoneyMagnetDistanceMap(
        IReadOnlyCollection<Point> players,
        int maximumDistance)
    {
        if (_maze is null) return new int[0, 0];
        var distance = new int[_maze.Width, _maze.Height];
        for (var x = 0; x < _maze.Width; x++)
        for (var y = 0; y < _maze.Height; y++)
            distance[x, y] = -1;

        var queue = new Queue<Point>();
        foreach (var player in players.Where(InsideMaze))
        {
            if (distance[player.X, player.Y] == 0) continue;
            queue.Enqueue(player);
            distance[player.X, player.Y] = 0;
        }
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            var nextDistance = distance[cell.X, cell.Y] + 1;
            if (nextDistance > maximumDistance) continue;
            foreach (var direction in AllDirections)
            {
                if (!_maze.CanMove(cell, direction)) continue;
                var next = _maze.Move(cell, direction);
                if (IsCellConcealed(next) || distance[next.X, next.Y] >= 0) continue;
                distance[next.X, next.Y] = nextDistance;
                queue.Enqueue(next);
            }
        }
        return distance;
    }

    private Point? FindMoneyMagnetStep(Point start, int[,] distanceToPlayer)
    {
        if (_maze is null || distanceToPlayer.Length == 0) return null;
        var currentDistance = distanceToPlayer[start.X, start.Y];
        if (currentDistance <= 0 || currentDistance > MoneyMagnetRange) return null;
        foreach (var direction in AllDirections)
        {
            if (!_maze.CanMove(start, direction)) continue;
            var next = _maze.Move(start, direction);
            if (distanceToPlayer[next.X, next.Y] == currentDistance - 1)
                return next;
        }
        return null;
    }
}
