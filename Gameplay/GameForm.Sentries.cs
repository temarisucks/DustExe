namespace Dust;

internal sealed partial class GameForm
{
    private const float SentryViewDistance = 10f;
    private const float SentryFieldOfView = 54f * MathF.PI / 180f;
    private const float SentryTurnSpeed = 1.08f;
    private const float SentrySubmergeDuration = .72f;
    private const float SentryBuriedDuration = .48f;
    private const float SentryEmergeDuration = .82f;
    private const float SentryProjectileSpeed = 15.5f;

    private readonly List<Sentry> _sentries = [];
    private readonly List<SentryProjectile> _sentryProjectiles = [];
    private int _sentryProjectileSerial;

    private float SentryRunViewDistance => SentryViewDistance *
        (1f + (RunAggressionScale - 1f) * .65f);

    private float SentryRunFieldOfView => SentryFieldOfView *
        (1f + (RunAggressionScale - 1f) * .35f);

    private void SpawnSentries()
    {
        ResetSentries();
        if (_maze is null) return;

        var count = GetEnemyRoster().Sentries;
        for (var index = 0; index < count; index++)
        {
            var cell = FindSentryPlacement(null, initialPlacement: true);
            if (!cell.HasValue) break;
            _sentries.Add(new Sentry
            {
                Cell = cell.Value,
                PreviousCell = cell.Value,
                FacingAngle = (float)_random.NextDouble() * MathF.PI * 2 - MathF.PI,
                RotationDirection = _random.Next(2) == 0 ? -1 : 1,
                AnimationPhase = (float)_random.NextDouble() * MathF.PI * 2,
                UnsuccessfulScanTime = (float)_random.NextDouble() * 2.1f,
                RelocationThreshold = NextSentryRelocationThreshold(),
                FireCooldown = .35f + (float)_random.NextDouble() * .7f,
                Phase = SentryPhase.Scanning
            });
        }
    }

    private void ResetSentries()
    {
        _sentries.Clear();
        _sentryProjectiles.Clear();
        _sentryProjectileSerial = 0;
    }

    private void UpdateSentries(float deltaTime)
    {
        if (_mode != ScreenMode.Playing &&
            !(IsOnlineGameplayActive && _mode == ScreenMode.Shop) ||
            _maze is null) return;
        if (IsOnlineGameplayActive && !IsOnlineSimulationHost) return;
        if (_hitEffect > 0 && !IsOnlineGameplayActive)
        {
            _sentryProjectiles.Clear();
            foreach (var sentry in _sentries) sentry.HasSight = false;
            return;
        }

        if (!IsOnlineLocalPlayerProtected && _invulnerability <= 0 &&
            _sentries.Any(sentry =>
                sentry.Phase != SentryPhase.Buried &&
                DistanceSquared(_visualCell, sentry.Cell) <= .24f))
        {
            BeginHollowHit(causedByHollow: false);
            _sentryProjectiles.Clear();
            return;
        }
        CheckOnlineRemoteSentryContact();

        foreach (var sentry in _sentries)
        {
            sentry.FireCooldown = Math.Max(0, sentry.FireCooldown - deltaTime);
            sentry.MuzzleFlash = Math.Max(0, sentry.MuzzleFlash - deltaTime);

            switch (sentry.Phase)
            {
                case SentryPhase.Scanning:
                    UpdateScanningSentry(sentry, deltaTime);
                    break;
                case SentryPhase.Submerging:
                    sentry.HasSight = false;
                    sentry.PhaseTimer += deltaTime;
                    if (sentry.PhaseTimer >= SentrySubmergeDuration)
                        BeginSentryTransit(sentry);
                    break;
                case SentryPhase.Buried:
                    sentry.PhaseTimer += deltaTime;
                    if (sentry.PhaseTimer >= SentryBuriedDuration)
                    {
                        sentry.Phase = SentryPhase.Emerging;
                        sentry.PhaseTimer = 0;
                    }
                    break;
                case SentryPhase.Emerging:
                    sentry.PhaseTimer += deltaTime;
                    if (sentry.PhaseTimer >= SentryEmergeDuration)
                    {
                        sentry.Phase = SentryPhase.Scanning;
                        sentry.PhaseTimer = 0;
                        sentry.UnsuccessfulScanTime = 0;
                        sentry.RelocationThreshold = NextSentryRelocationThreshold();
                        sentry.FireCooldown = .35f;
                    }
                    break;
            }
        }

        UpdateSentryProjectiles(deltaTime);
    }

    private void UpdateScanningSentry(Sentry sentry, float deltaTime)
    {
        sentry.FacingAngle = NormalizeAngle(
            sentry.FacingAngle + sentry.RotationDirection * SentryTurnSpeed *
            RunAggressionScale * deltaTime);

        var seesPlayer = CanSentrySeePlayer(sentry);
        if (seesPlayer)
        {
            if (!sentry.HasSight) TriggerOnlineDetectionWarning(sentry.TargetPlayerId);
            sentry.HasSight = true;
            sentry.UnsuccessfulScanTime = 0;
            if (sentry.FireCooldown <= 0)
                FireSentryProjectile(sentry);
            return;
        }

        sentry.HasSight = false;
        sentry.UnsuccessfulScanTime += deltaTime;
        if (sentry.UnsuccessfulScanTime < sentry.RelocationThreshold) return;
        sentry.Phase = SentryPhase.Submerging;
        sentry.PhaseTimer = 0;
        sentry.MuzzleFlash = 0;
    }

    private bool CanSentrySeePlayer(Sentry sentry)
    {
        if (IsOnlineSimulationHost)
        {
            var found = TryFindOnlineSentryTarget(
                sentry, out var targetPlayerId, out _);
            sentry.TargetPlayerId = found ? targetPlayerId : null;
            return found;
        }
        sentry.TargetPlayerId = _onlinePlayerId;
        return !IsPlayerInvisibleToEnemies && CanSentrySeePosition(sentry, _visualCell);
    }

    private bool CanSentrySeePosition(Sentry sentry, PointF target)
    {
        var origin = new PointF(sentry.Cell.X, sentry.Cell.Y);
        var dx = target.X - origin.X;
        var dy = target.Y - origin.Y;
        var distanceSquared = dx * dx + dy * dy;
        if (distanceSquared <= .001f) return true;
        if (distanceSquared > SentryRunViewDistance * SentryRunViewDistance) return false;

        var targetAngle = MathF.Atan2(dy, dx);
        if (Math.Abs(NormalizeAngle(targetAngle - sentry.FacingAngle)) > SentryRunFieldOfView / 2)
            return false;

        var distance = MathF.Sqrt(distanceSquared);
        var clearDistance = RaycastVisionDistance(origin, targetAngle, distance, ignoreWalls: false);
        return clearDistance >= distance - .06f;
    }

    private void FireSentryProjectile(Sentry sentry)
    {
        var target = OnlineSentryTargetVisual(sentry);
        var dx = target.X - sentry.Cell.X;
        var dy = target.Y - sentry.Cell.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= .001f)
        {
            dx = MathF.Cos(sentry.FacingAngle);
            dy = MathF.Sin(sentry.FacingAngle);
            length = 1;
        }

        var directionX = dx / length;
        var directionY = dy / length;
        var position = new PointF(
            sentry.Cell.X + directionX * .44f,
            sentry.Cell.Y + directionY * .44f);
        _sentryProjectiles.Add(new SentryProjectile
        {
            Position = position,
            PreviousPosition = position,
            Velocity = new PointF(directionX * SentryProjectileSpeed * RunAggressionScale,
                directionY * SentryProjectileSpeed * RunAggressionScale),
            Lifetime = .82f,
            Serial = ++_sentryProjectileSerial
        });
        sentry.MuzzleFlash = .16f;
        sentry.FireCooldown = 1.05f / RunAggressionScale;
    }

    private void UpdateSentryProjectiles(float deltaTime)
    {
        for (var index = _sentryProjectiles.Count - 1; index >= 0; index--)
        {
            var projectile = _sentryProjectiles[index];
            projectile.Lifetime -= deltaTime;
            if (projectile.Lifetime <= 0)
            {
                _sentryProjectiles.RemoveAt(index);
                continue;
            }

            projectile.PreviousPosition = projectile.Position;
            var speed = MathF.Sqrt(
                projectile.Velocity.X * projectile.Velocity.X +
                projectile.Velocity.Y * projectile.Velocity.Y);
            var travel = speed * deltaTime;
            var angle = MathF.Atan2(projectile.Velocity.Y, projectile.Velocity.X);
            var clearDistance = RaycastVisionDistance(
                projectile.PreviousPosition, angle, travel + .015f, ignoreWalls: false);
            if (clearDistance + .003f < travel)
            {
                _sentryProjectiles.RemoveAt(index);
                continue;
            }

            projectile.Position = new PointF(
                projectile.PreviousPosition.X + projectile.Velocity.X * deltaTime,
                projectile.PreviousPosition.Y + projectile.Velocity.Y * deltaTime);

            if (!InsideMaze(PositionCell(projectile.Position)))
            {
                _sentryProjectiles.RemoveAt(index);
                continue;
            }

            if (TryHitOnlinePlayerWithProjectile(projectile))
            {
                _sentryProjectiles.Clear();
                return;
            }

            if (_invulnerability > 0 || _hitEffect > 0 || IsOnlineLocalPlayerProtected) continue;
            var separationSquared = SweptSeparationSquared(
                _previousVisualCell, _visualCell,
                projectile.PreviousPosition, projectile.Position);
            if (separationSquared > .075f) continue;

            BeginHollowHit(causedByHollow: false);
            _sentryProjectiles.Clear();
            return;
        }
    }

    private void BeginSentryTransit(Sentry sentry)
    {
        sentry.PreviousCell = sentry.Cell;
        var destination = FindSentryPlacement(sentry, initialPlacement: false);
        if (destination.HasValue) sentry.Cell = destination.Value;
        sentry.FacingAngle = (float)_random.NextDouble() * MathF.PI * 2 - MathF.PI;
        sentry.Phase = SentryPhase.Buried;
        sentry.PhaseTimer = 0;
        sentry.UnsuccessfulScanTime = 0;
    }

    private Point? FindSentryPlacement(Sentry? self, bool initialPlacement)
    {
        if (_maze is null) return null;
        var preferred = new List<Point>();
        var fallback = new List<Point>();
        var minimumPlayerDistance = initialPlacement ? 10 : 7;

        for (var x = 0; x < _maze.Width; x++)
        for (var y = 0; y < _maze.Height; y++)
        {
            var candidate = new Point(x, y);
            if (candidate == _playerCell || candidate == _exitCell) continue;
            if (IsSurvivorPlacementCell(candidate)) continue;
            if (_maze.GetRoomAt(candidate) is not null) continue;
            if (self is not null && candidate == self.PreviousCell) continue;
            if (_hollows.Any(hollow => hollow.Cell == candidate || hollow.TargetCell == candidate)) continue;
            if (_sentries.Any(other => other != self && other.Cell == candidate)) continue;
            if (_creditPickups.Any(pickup => !pickup.Collected && pickup.Cell == candidate)) continue;
            if (_cargoItems.Any(item =>
                    !item.Carried && item.CarrierPlayerId is null &&
                    !item.Delivered && item.Cell == candidate)) continue;
            if (Manhattan(candidate, _playerCell) < minimumPlayerDistance) continue;
            if (Manhattan(candidate, _exitCell) < 4) continue;

            fallback.Add(candidate);
            var openingMask = _maze.GetOpeningMask(x, y);
            if (SentryOpeningCount(openingMask) >= 2)
                preferred.Add(candidate);
        }

        var choices = preferred.Count > 0 ? preferred : fallback;
        return choices.Count == 0 ? null : choices[_random.Next(choices.Count)];
    }

    private float NextSentryRelocationThreshold() =>
        (11.5f + (float)_random.NextDouble() * 3.2f) / RunAggressionScale;

    private static float DistanceSquared(PointF first, Point second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }

    private static int SentryOpeningCount(int mask)
    {
        var count = 0;
        while (mask != 0)
        {
            count += mask & 1;
            mask >>= 1;
        }
        return count;
    }
}
