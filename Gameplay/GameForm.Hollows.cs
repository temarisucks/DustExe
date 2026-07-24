namespace Dust;

internal sealed partial class GameForm
{
    private void SpawnHollows()
    {
        _hollows.Clear();
        var roster = GetEnemyRoster();
        AddType(HollowType.Square, roster.Squares);
        AddType(HollowType.Diamond, roster.Diamonds);
        AddType(HollowType.Hex, roster.Hexes);
        AddType(HollowType.Triangle, roster.Triangles);
        AddType(HollowType.Camera, roster.Cameras);
        AddType(HollowType.Star, roster.Stars);

        void AddType(HollowType type, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var cell = type == HollowType.Camera
                    ? FindCameraSpawn(i)
                    : FindHollowSpawn(i == 0);
                var facing = InitialHollowFacing(cell);
                var orbitAngle = (float)_random.NextDouble() * MathF.PI * 2;
                _hollows.Add(new Hollow
                {
                    Type = type,
                    Cell = cell,
                    TargetCell = cell,
                    PreviousCell = cell,
                    LastSeen = cell,
                    LastSeenVisual = cell,
                    VisualCell = cell,
                    PreviousVisualCell = cell,
                    MoveFrom = cell,
                    MoveTo = cell,
                    Cooldown = .8f + (float)_random.NextDouble() * 1.8f,
                    SenseCooldown = (float)_random.NextDouble() * .1f,
                    FacingAngle = facing,
                    DesiredFacingAngle = facing,
                    LookCooldown = .25f + (float)_random.NextDouble() * .8f,
                    AnimationPhase = (float)_random.NextDouble() * MathF.PI * 2,
                    AggressionScale = RunAggressionScale,
                    AbilityCooldown = 1.4f + (float)_random.NextDouble() * 2.2f,
                    ProjectileCooldown = .45f + (float)_random.NextDouble(),
                    TriangleOrbitAngle = orbitAngle,
                    PreviousTriangleOrbitAngle = orbitAngle
                });
            }
        }
    }

    private Point FindCameraSpawn(int cameraIndex)
    {
        if (_maze is null) return Point.Empty;
        var corners = new[]
        {
            new Point(1, 1),
            new Point(Math.Max(0, _maze.Width - 2), 1),
            new Point(Math.Max(0, _maze.Width - 2), Math.Max(0, _maze.Height - 2)),
            new Point(1, Math.Max(0, _maze.Height - 2))
        };
        var corner = corners[cameraIndex % corners.Length];
        var candidates = new List<(Point Cell, int Score)>();
        for (var x = 0; x < _maze.Width; x++)
        for (var y = 0; y < _maze.Height; y++)
        {
            var cell = new Point(x, y);
            if (cell == _playerCell || cell == _exitCell ||
                _maze.GetRoomAt(cell) is not null ||
                IsSurvivorPlacementCell(cell) ||
                _hollows.Any(hollow => hollow.Cell == cell || hollow.TargetCell == cell))
                continue;
            var wallCount = 4 - CountBits(_maze.GetOpeningMask(x, y));
            var cornerDistance = Manhattan(cell, corner);
            // Prefer a genuine wall corner, then the corresponding map quadrant.
            var score = cornerDistance * 8 - Math.Min(2, wallCount) * 14;
            candidates.Add((cell, score));
        }
        return candidates.Count == 0
            ? FindHollowSpawn(placeInEncounterBand: false)
            : candidates.OrderBy(entry => entry.Score)
                .ThenBy(entry => entry.Cell.Y)
                .ThenBy(entry => entry.Cell.X)
                .First().Cell;
    }

    private float InitialHollowFacing(Point cell)
    {
        if (_maze is null) return 0;
        var openings = AllDirections.Where(direction => _maze.CanMove(cell, direction)).ToList();
        var direction = openings.Count > 0
            ? openings[_random.Next(openings.Count)]
            : AllDirections[_random.Next(AllDirections.Length)];
        return DirectionAngle(direction);
    }

    private Point FindHollowSpawn(bool placeInEncounterBand)
    {
        if (_maze is null) return Point.Empty;
        var valid = new List<Point>();
        var preferred = new List<Point>();
        for (var x = 0; x < _maze.Width; x++)
        for (var y = 0; y < _maze.Height; y++)
        {
            var candidate = new Point(x, y);
            if (candidate == _playerCell || candidate == _exitCell) continue;
            if (IsSurvivorPlacementCell(candidate)) continue;
            if (_maze.GetRoomAt(candidate) is not null) continue;
            if (_hollows.Any(hollow => hollow.Cell == candidate || hollow.TargetCell == candidate)) continue;
            valid.Add(candidate);
            var playerDistance = Manhattan(candidate, _playerCell);
            if (playerDistance >= 12 && Manhattan(candidate, _exitCell) >= 4 &&
                (!placeInEncounterBand || playerDistance <= 18))
                preferred.Add(candidate);
        }
        if (preferred.Count > 0) return preferred[_random.Next(preferred.Count)];
        if (valid.Count == 0) return _playerCell;
        var farthest = valid.Max(cell => Manhattan(cell, _playerCell));
        var fallbacks = valid.Where(cell => Manhattan(cell, _playerCell) == farthest).ToList();
        return fallbacks[_random.Next(fallbacks.Count)];
    }

    private void UpdateHollows(float deltaTime)
    {
        if (_mode != ScreenMode.Playing &&
            !(IsOnlineGameplayActive && _mode == ScreenMode.Shop) ||
            _maze is null ||
            _hitEffect > 0 && !IsOnlineGameplayActive) return;
        if (IsOnlineGameplayActive && !IsOnlineSimulationHost) return;
        UpdateEnemyEmpowerment();
        foreach (var hollow in _hollows)
        {
            hollow.PreviousVisualCell = hollow.VisualCell;
            hollow.AbilityCooldown = Math.Max(0, hollow.AbilityCooldown - deltaTime);
            hollow.ProjectileCooldown = Math.Max(0, hollow.ProjectileCooldown - deltaTime);
            hollow.TeleportFlash = Math.Max(0, hollow.TeleportFlash - deltaTime);
            hollow.PreviousTriangleOrbitAngle = hollow.TriangleOrbitAngle;
            hollow.TriangleOrbitAngle = NormalizeAngle(
                hollow.TriangleOrbitAngle + deltaTime *
                (hollow.TriangleSplit ? 2.75f : .82f));
            if (hollow.TriangleSplit)
            {
                hollow.TriangleSplitTimer = Math.Max(0, hollow.TriangleSplitTimer - deltaTime);
                if (hollow.TriangleSplitTimer <= 0 && !hollow.HasSight)
                {
                    hollow.TriangleSplit = false;
                    hollow.State = HollowState.Roam;
                    hollow.TargetPlayerId = null;
                }
            }
            if (hollow.State == HollowState.Search && hollow.SearchTimer > 0)
                hollow.SearchTimer = Math.Max(0, hollow.SearchTimer - deltaTime);

            if (hollow.Type == HollowType.Camera)
            {
                UpdateCameraHollow(hollow, deltaTime);
                continue;
            }

            UpdateHollowFacing(hollow, deltaTime);

            hollow.SenseCooldown -= deltaTime;
            if (hollow.SenseCooldown <= 0)
            {
                UpdateHollowPerception(hollow);
                hollow.SenseCooldown = hollow.Type == HollowType.Square ? .16f : .095f;
            }
            UpdateEmpoweredHollowAbility(hollow);
            AdvanceHollow(hollow, deltaTime);
        }
    }

    private void UpdateEnemyEmpowerment()
    {
        foreach (var hollow in _hollows) hollow.Empowered = false;
        foreach (var sentry in _sentries) sentry.Empowered = false;

        const float radiusSquared = 36f;
        var stars = _hollows.Where(hollow => hollow.Type == HollowType.Star).ToArray();
        foreach (var star in stars)
        {
            foreach (var hollow in _hollows)
            {
                // A Star never powers itself. Two distinct Stars may power one
                // another, which unlocks their advanced attack package.
                if (ReferenceEquals(star, hollow) ||
                    PerkDistanceSquared(star.VisualCell, hollow.VisualCell) > radiusSquared)
                    continue;
                hollow.Empowered = true;
            }
            foreach (var sentry in _sentries)
                if (PerkDistanceSquared(star.VisualCell, sentry.Cell) <= radiusSquared)
                    sentry.Empowered = true;
        }
    }

    private void UpdateCameraHollow(Hollow camera, float deltaTime)
    {
        camera.FacingAngle = NormalizeAngle(camera.FacingAngle +
            camera.TurnSpeed * .72f * deltaTime);
        camera.DesiredFacingAngle = camera.FacingAngle;
        camera.VisualCell = camera.Cell;
        camera.MoveFrom = camera.Cell;
        camera.MoveTo = camera.Cell;
        camera.MoveProgress = 1;

        camera.SenseCooldown -= deltaTime;
        if (camera.SenseCooldown > 0) return;
        camera.SenseCooldown = .075f;

        var wasSeeingPlayer = camera.HasSight;
        var previousTargetPlayerId = camera.TargetPlayerId;
        var targetPlayerId = _onlinePlayerId ?? string.Empty;
        var targetVisual = _visualCell;
        var targetCell = _playerCell;
        var seesPlayer = !IsPlayerInvisibleToEnemies &&
                         CanHollowSee(camera, _visualCell);
        if (IsOnlineSimulationHost)
            seesPlayer = TryFindOnlineHollowTarget(
                camera, out targetPlayerId, out targetVisual, out targetCell);

        camera.HasSight = seesPlayer;
        camera.State = seesPlayer ? HollowState.Chase : HollowState.Roam;
        camera.TargetPlayerId = seesPlayer ? targetPlayerId : null;
        if (!seesPlayer) return;

        camera.LastSeen = targetCell;
        camera.LastSeenVisual = targetVisual;
        var newlyDetected = !wasSeeingPlayer ||
                            !string.Equals(previousTargetPlayerId, targetPlayerId,
                                StringComparison.Ordinal);
        if (!newlyDetected) return;
        TriggerOnlineDetectionWarning(targetPlayerId);
        DispatchCameraDistress(camera, targetPlayerId, targetVisual);
    }

    private void DispatchCameraDistress(Hollow camera, string targetPlayerId, PointF target)
    {
        const float responseRadiusSquared = 144f;
        foreach (var responder in _hollows)
        {
            if (ReferenceEquals(responder, camera) ||
                responder.Type == HollowType.Camera ||
                PerkDistanceSquared(camera.VisualCell, responder.VisualCell) >
                responseRadiusSquared)
                continue;

            responder.LastSeenVisual = target;
            responder.LastSeen = PositionCell(target);
            responder.TargetPlayerId = targetPlayerId;
            responder.HasSight = false;
            responder.State = HollowState.Search;
            responder.SearchTimer = -1;
            responder.Cooldown = 0;
            if (camera.Empowered)
                TryTeleportHollowNear(responder, target, 1, 3);
        }

        foreach (var sentry in _sentries)
        {
            if (PerkDistanceSquared(camera.VisualCell, sentry.Cell) >
                responseRadiusSquared) continue;
            sentry.TargetPlayerId = targetPlayerId;
            sentry.UnsuccessfulScanTime = 0;
            sentry.FacingAngle = MathF.Atan2(
                target.Y - sentry.Cell.Y, target.X - sentry.Cell.X);
            if (camera.Empowered)
                TryTeleportSentryNear(sentry, target);
        }
    }

    private void UpdateEmpoweredHollowAbility(Hollow hollow)
    {
        if (!hollow.Empowered) return;
        var target = OnlineHollowTargetVisual(hollow);
        switch (hollow.Type)
        {
            case HollowType.Hex when hollow.State == HollowState.Chase &&
                                     hollow.AbilityCooldown <= 0:
                if (TryTeleportHollowNear(hollow, target, 1, 3))
                    hollow.AbilityCooldown = 4.1f / hollow.AggressionScale;
                break;
            case HollowType.Triangle when hollow.TriangleSplit && hollow.HasSight &&
                                          hollow.ProjectileCooldown <= 0:
                FireHollowProjectile(hollow, EnemyProjectileKind.Triangle, target);
                hollow.ProjectileCooldown = .82f / hollow.AggressionScale;
                break;
            case HollowType.Star when hollow.State == HollowState.Chase:
                if (hollow.ProjectileCooldown <= 0)
                {
                    FireHollowProjectile(hollow, EnemyProjectileKind.Star, target);
                    hollow.ProjectileCooldown = 1.12f / hollow.AggressionScale;
                }
                if (hollow.AbilityCooldown <= 0 &&
                    TryTeleportHollowNear(hollow, target, 2, 4))
                    hollow.AbilityCooldown = 5.2f / hollow.AggressionScale;
                break;
        }
    }

    private void FireHollowProjectile(
        Hollow hollow,
        EnemyProjectileKind kind,
        PointF target)
    {
        var origin = hollow.VisualCell;
        if (kind == EnemyProjectileKind.Triangle && hollow.TriangleSplit)
        {
            var members = TriangleMemberPositions(hollow);
            origin = members[_sentryProjectileSerial % members.Length];
        }
        var dx = target.X - origin.X;
        var dy = target.Y - origin.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= .001f)
        {
            dx = MathF.Cos(hollow.FacingAngle);
            dy = MathF.Sin(hollow.FacingAngle);
            length = 1;
        }
        var speed = kind == EnemyProjectileKind.Star ? 10.8f : 12.6f;
        var directionX = dx / length;
        var directionY = dy / length;
        var muzzleOffset = kind == EnemyProjectileKind.Triangle ? .10f : .34f;
        var position = new PointF(
            origin.X + directionX * muzzleOffset,
            origin.Y + directionY * muzzleOffset);
        _sentryProjectiles.Add(new SentryProjectile
        {
            Position = position,
            PreviousPosition = position,
            Velocity = new PointF(directionX * speed * hollow.AggressionScale,
                directionY * speed * hollow.AggressionScale),
            Lifetime = kind == EnemyProjectileKind.Star ? 1.45f : 1.05f,
            Serial = ++_sentryProjectileSerial,
            Kind = kind,
            IgnoreWalls = kind == EnemyProjectileKind.Star,
            Damage = 1
        });
    }

    private bool TryTeleportHollowNear(
        Hollow hollow,
        PointF target,
        int minimumDistance,
        int maximumDistance)
    {
        if (_maze is null || hollow.Type == HollowType.Camera) return false;
        var targetCell = PositionCell(target);
        var candidates = new List<Point>();
        for (var x = Math.Max(0, targetCell.X - maximumDistance);
             x <= Math.Min(_maze.Width - 1, targetCell.X + maximumDistance); x++)
        for (var y = Math.Max(0, targetCell.Y - maximumDistance);
             y <= Math.Min(_maze.Height - 1, targetCell.Y + maximumDistance); y++)
        {
            var cell = new Point(x, y);
            var distance = Manhattan(cell, targetCell);
            if (distance < minimumDistance || distance > maximumDistance ||
                _maze.GetRoomAt(cell) is not null || IsCellConcealed(cell) ||
                IsRoomDecorationBlockingCell(cell) ||
                IsAnyLivingPlayerAtCell(cell) ||
                IsOccupiedByOtherHollow(hollow, cell) ||
                IsSurvivorBlockingCell(cell))
                continue;
            candidates.Add(cell);
        }
        if (candidates.Count == 0) return false;
        var destination = candidates[_random.Next(candidates.Count)];
        hollow.Cell = destination;
        hollow.TargetCell = destination;
        hollow.PreviousCell = destination;
        hollow.VisualCell = destination;
        hollow.PreviousVisualCell = destination;
        hollow.MoveFrom = destination;
        hollow.MoveTo = destination;
        hollow.MoveProgress = 1;
        hollow.Cooldown = .08f;
        hollow.TeleportFlash = .42f;
        return true;
    }

    private void TryTeleportSentryNear(Sentry sentry, PointF target)
    {
        if (_maze is null) return;
        var targetCell = PositionCell(target);
        var candidates = new List<Point>();
        for (var x = Math.Max(0, targetCell.X - 4);
             x <= Math.Min(_maze.Width - 1, targetCell.X + 4); x++)
        for (var y = Math.Max(0, targetCell.Y - 4);
             y <= Math.Min(_maze.Height - 1, targetCell.Y + 4); y++)
        {
            var cell = new Point(x, y);
            var distance = Manhattan(cell, targetCell);
            if (distance is < 1 or > 4 || _maze.GetRoomAt(cell) is not null ||
                IsRoomDecorationBlockingCell(cell) ||
                IsSurvivorBlockingCell(cell) ||
                IsAnyLivingPlayerAtCell(cell) ||
                _hollows.Any(hollow => hollow.Cell == cell || hollow.TargetCell == cell) ||
                _sentries.Any(other => other != sentry && other.Cell == cell))
                continue;
            candidates.Add(cell);
        }
        if (candidates.Count == 0) return;
        sentry.PreviousCell = sentry.Cell;
        sentry.Cell = candidates[_random.Next(candidates.Count)];
        sentry.Phase = SentryPhase.Emerging;
        sentry.PhaseTimer = SentryEmergeDuration * .72f;
    }

    private bool IsAnyLivingPlayerAtCell(Point cell)
    {
        if (!IsOnlineGameplayActive)
            return cell == _playerCell || cell == PositionCell(_visualCell);
        if (!_onlineLocalDefeated &&
            (cell == _playerCell || cell == PositionCell(_visualCell)))
            return true;
        return _onlinePlayers.Values.Any(player =>
            player.Connected && !player.Defeated && !player.Extracted &&
            (cell == player.Cell || cell == PositionCell(player.VisualCell)));
    }

    private PointF OnlineHollowTargetVisual(Hollow hollow)
    {
        if (!IsOnlineSimulationHost || hollow.TargetPlayerId == _onlinePlayerId)
            return _visualCell;
        return hollow.TargetPlayerId is not null &&
               _onlinePlayers.TryGetValue(hollow.TargetPlayerId, out var player)
            ? player.VisualCell
            : hollow.LastSeenVisual;
    }

    private static PointF[] TriangleMemberPositions(Hollow hollow) =>
        TriangleMemberPositions(
            hollow.VisualCell, hollow.TriangleOrbitAngle, hollow.TriangleSplit);

    private static PointF[] PreviousTriangleMemberPositions(Hollow hollow) =>
        TriangleMemberPositions(
            hollow.PreviousVisualCell,
            hollow.PreviousTriangleOrbitAngle,
            hollow.TriangleSplit);

    private static PointF[] TriangleMemberPositions(
        PointF center,
        float orbitAngle,
        bool split)
    {
        if (!split) return [center];
        // Keep split members inside their logical tile. At .30 cell they read
        // as three bodies but cannot overlap or damage through an intact wall.
        var radius = .30f;
        var points = new PointF[3];
        for (var index = 0; index < points.Length; index++)
        {
            var angle = orbitAngle + index * MathF.PI * 2 / 3;
            points[index] = new PointF(
                center.X + MathF.Cos(angle) * radius,
                center.Y + MathF.Sin(angle) * radius);
        }
        return points;
    }

    private void AdvanceHollow(Hollow hollow, float deltaTime)
    {
        var remaining = deltaTime;
        for (var pass = 0; pass < 4 && remaining > .0001f; pass++)
        {
            if (hollow.IsMoving)
            {
                var timeToWaypoint = (1 - hollow.MoveProgress) * hollow.MoveDuration;
                if (timeToWaypoint > remaining)
                {
                    hollow.MoveProgress += remaining / hollow.MoveDuration;
                    SetHollowVisualPosition(hollow);
                    break;
                }

                remaining -= timeToWaypoint;
                hollow.MoveProgress = 1;
                hollow.Cell = hollow.TargetCell;
                hollow.VisualCell = hollow.Cell;
                hollow.Cooldown = HollowPause(hollow);
                if (hollow.Cooldown <= 0) DecideHollowMove(hollow);
                continue;
            }

            if (hollow.Cooldown > 0)
            {
                if (hollow.Cooldown >= remaining)
                {
                    hollow.Cooldown -= remaining;
                    break;
                }
                remaining -= hollow.Cooldown;
                hollow.Cooldown = 0;
            }

            DecideHollowMove(hollow);
        }
    }

    private static void SetHollowVisualPosition(Hollow hollow)
    {
        hollow.VisualCell = new PointF(
            hollow.MoveFrom.X + (hollow.MoveTo.X - hollow.MoveFrom.X) * hollow.MoveProgress,
            hollow.MoveFrom.Y + (hollow.MoveTo.Y - hollow.MoveFrom.Y) * hollow.MoveProgress);
    }

    private void UpdateHollowFacing(Hollow hollow, float deltaTime)
    {
        if (hollow.State == HollowState.Chase && hollow.HasSight)
        {
            var dx = hollow.LastSeenVisual.X - hollow.VisualCell.X;
            var dy = hollow.LastSeenVisual.Y - hollow.VisualCell.Y;
            if (dx * dx + dy * dy > .001f)
                hollow.DesiredFacingAngle = MathF.Atan2(dy, dx);
        }
        else if (hollow.IsMoving)
        {
            var dx = hollow.MoveTo.X - hollow.VisualCell.X;
            var dy = hollow.MoveTo.Y - hollow.VisualCell.Y;
            if (dx * dx + dy * dy > .001f)
                hollow.DesiredFacingAngle = MathF.Atan2(dy, dx);
        }
        else
        {
            hollow.LookCooldown -= deltaTime;
            if (hollow.LookCooldown <= 0)
            {
                var direction = _random.Next(2) == 0 ? -1 : 1;
                var sweep = .5f + (float)_random.NextDouble() *
                    (hollow.Type == HollowType.Square ? .85f : 1.25f);
                hollow.DesiredFacingAngle = NormalizeAngle(hollow.FacingAngle + direction * sweep);
                hollow.LookCooldown = .55f + (float)_random.NextDouble() * 1.15f;
            }
        }

        hollow.FacingAngle = RotateTowards(
            hollow.FacingAngle, hollow.DesiredFacingAngle, hollow.TurnSpeed * deltaTime);
    }

    private void DecideHollowMove(Hollow hollow)
    {
        Point? next = null;
        if (hollow.State == HollowState.Chase)
            next = FindNextPathStep(hollow, OnlineHollowTargetCell(hollow));
        else if (hollow.State == HollowState.Search)
        {
            var atSearchArea = hollow.Cell == hollow.LastSeen ||
                               GraphDistance(hollow.Cell, hollow.LastSeen, 1) >= 0;
            if (!atSearchArea)
                next = FindNextPathStep(hollow, hollow.LastSeen);
            else
            {
                if (hollow.SearchTimer < 0) hollow.SearchTimer = 4.8f;
                if (hollow.SearchTimer > 0)
                    next = ChooseRoamStep(hollow, hollow.LastSeen, 3);
                else
                    hollow.State = HollowState.Roam;
            }
        }
        if (hollow.State == HollowState.Roam)
            next = ChooseRoamStep(hollow, null, 0);

        if (next.HasValue) StartHollowMove(hollow, next.Value);
        else hollow.Cooldown = .2f;
    }

    private void UpdateHollowPerception(Hollow hollow)
    {
        var wasSeeingPlayer = hollow.HasSight;
        var previousTargetPlayerId = hollow.TargetPlayerId;
        var targetPlayerId = _onlinePlayerId ?? string.Empty;
        var targetVisual = _visualCell;
        var targetCell = _playerCell;
        var seesPlayer = !IsPlayerInvisibleToEnemies &&
                         !IsPositionConcealed(hollow.VisualCell) &&
                         CanHollowSee(hollow, _visualCell);
        if (IsOnlineSimulationHost)
        {
            seesPlayer = !IsPositionConcealed(hollow.VisualCell) &&
                         TryFindOnlineHollowTarget(
                             hollow, out targetPlayerId, out targetVisual, out targetCell);
        }
        hollow.HasSight = seesPlayer;
        if (seesPlayer)
        {
            var newlyDetected = !wasSeeingPlayer ||
                                !string.Equals(previousTargetPlayerId, targetPlayerId,
                                    StringComparison.Ordinal);
            hollow.State = hollow.Type == HollowType.Triangle
                ? HollowState.Search
                : HollowState.Chase;
            hollow.TargetPlayerId = targetPlayerId;
            hollow.LastSeen = PositionCell(targetVisual);
            hollow.LastSeenVisual = targetVisual;
            if (newlyDetected)
                TriggerOnlineDetectionWarning(targetPlayerId);
            if (hollow.Type == HollowType.Triangle)
            {
                hollow.TriangleSplit = true;
                hollow.TriangleSplitTimer = 6.5f;
                hollow.SearchTimer = 6.5f;
            }
            if (newlyDetected && !hollow.IsMoving)
                hollow.Cooldown = hollow.Type == HollowType.Square ? .22f : .10f;
            return;
        }

        if (hollow.Type == HollowType.Diamond && hollow.Empowered &&
            hollow.State == HollowState.Chase)
        {
            if (IsHollowPursuitTargetAvailable(hollow))
            {
                var pursuitTarget = OnlineHollowTargetVisual(hollow);
                if (PerkDistanceSquared(hollow.VisualCell, pursuitTarget) <= 196f)
                {
                    hollow.LastSeen = PositionCell(pursuitTarget);
                    hollow.LastSeenVisual = pursuitTarget;
                    return;
                }
            }
            hollow.State = HollowState.Search;
            hollow.SearchTimer = -1;
        }
        else if (hollow.Type == HollowType.Diamond && hollow.State == HollowState.Chase)
        {
            hollow.State = HollowState.Search;
            hollow.SearchTimer = -1;
        }
        else if (hollow.Type == HollowType.Triangle && hollow.TriangleSplit)
        {
            hollow.State = HollowState.Search;
            if (hollow.SearchTimer <= 0)
                hollow.SearchTimer = hollow.TriangleSplitTimer;
        }
        else if (hollow.Type != HollowType.Diamond && hollow.State == HollowState.Chase)
        {
            hollow.State = HollowState.Roam;
            hollow.TargetPlayerId = null;
        }
        else if (hollow.State == HollowState.Search && hollow.SearchTimer == 0)
        {
            hollow.State = HollowState.Roam;
        }
    }

    private bool IsHollowPursuitTargetAvailable(Hollow hollow)
    {
        if (!IsOnlineGameplayActive) return true;
        if (string.IsNullOrWhiteSpace(hollow.TargetPlayerId)) return false;
        if (hollow.TargetPlayerId == _onlinePlayerId)
            return !_onlineLocalDefeated && !IsOnlineLocalPlayerProtected;
        return _onlinePlayers.TryGetValue(hollow.TargetPlayerId, out var player) &&
               OnlinePlayerCanBeTargeted(player);
    }

    private void StartHollowMove(Hollow hollow, Point target)
    {
        hollow.PreviousCell = hollow.Cell;
        hollow.TargetCell = target;
        hollow.MoveFrom = hollow.VisualCell;
        hollow.MoveTo = target;
        hollow.MoveProgress = 0;
        if (hollow.State != HollowState.Chase || !hollow.HasSight)
            hollow.DesiredFacingAngle = MathF.Atan2(
                target.Y - hollow.VisualCell.Y, target.X - hollow.VisualCell.X);
    }

    private float HollowPause(Hollow hollow)
    {
        if (hollow.State == HollowState.Chase) return 0;
        if (hollow.State == HollowState.Search)
        {
            if (_random.NextDouble() > .22) return 0;
            hollow.LookCooldown = 0;
            return .22f + (float)_random.NextDouble() * .34f;
        }
        var pauseChance = hollow.Type == HollowType.Square ? .18 : .1;
        if (_random.NextDouble() > pauseChance) return 0;
        hollow.LookCooldown = 0;
        return hollow.Type == HollowType.Square
            ? .28f + (float)_random.NextDouble() * .42f
            : .16f + (float)_random.NextDouble() * .28f;
    }

    private void TriggerDetectionWarning()
    {
        RecordDetectionForAchievements();
        _warningFlash = .82f;
        if (_warningSoundCooldown > 0) return;
        _audio.Play(AudioCue.Caught);
        _warningSoundCooldown = .48f;
    }
}
