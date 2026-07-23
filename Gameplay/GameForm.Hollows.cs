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

        void AddType(HollowType type, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var cell = FindHollowSpawn(i == 0);
                var facing = InitialHollowFacing(cell);
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
                    AggressionScale = RunAggressionScale
                });
            }
        }
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
        foreach (var hollow in _hollows)
        {
            hollow.PreviousVisualCell = hollow.VisualCell;
            if (hollow.State == HollowState.Search && hollow.SearchTimer > 0)
                hollow.SearchTimer = Math.Max(0, hollow.SearchTimer - deltaTime);

            UpdateHollowFacing(hollow, deltaTime);

            hollow.SenseCooldown -= deltaTime;
            if (hollow.SenseCooldown <= 0)
            {
                UpdateHollowPerception(hollow);
                hollow.SenseCooldown = hollow.Type == HollowType.Square ? .16f : .095f;
            }
            AdvanceHollow(hollow, deltaTime);
        }
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
            var newlyAlerted = hollow.State != HollowState.Chase;
            hollow.State = HollowState.Chase;
            hollow.TargetPlayerId = targetPlayerId;
            hollow.LastSeen = PositionCell(targetVisual);
            hollow.LastSeenVisual = targetVisual;
            if (!wasSeeingPlayer) TriggerOnlineDetectionWarning(targetPlayerId);
            if (newlyAlerted && !hollow.IsMoving)
                hollow.Cooldown = hollow.Type == HollowType.Square ? .22f : .10f;
            return;
        }

        if (hollow.Type == HollowType.Diamond && hollow.State == HollowState.Chase)
        {
            hollow.State = HollowState.Search;
            hollow.SearchTimer = -1;
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
