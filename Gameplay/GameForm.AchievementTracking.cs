namespace Dust;

internal sealed partial class GameForm
{
    private readonly List<DateTime> _runHitTimes = [];
    private readonly Queue<AchievementId> _achievementToastQueue = [];
    private AchievementId? _achievementToast;
    private float _achievementToastTimer;
    private bool _runHadAlert;
    private bool _runHadQuickDoubleHit;
    private bool _runProgressFinalized = true;
    private float _continuousChaseTime;
    private Hollow? _ghostLoverPursuer;
    private Direction? _lastAchievementMoveDirection;
    private Hollow? _anklesCandidate;
    private int _anklesDamageBaseline;
    private float _anklesWindow;
    private bool _anklesPassedClose;

    private void InitializeProgression()
    {
        // Job pay could only be earned by completing a plate in builds predating
        // achievements, so preserve that accomplishment for existing profiles.
        if (_settings.TotalCredits > 0 &&
            _settings.UnlockAchievement(AchievementId.CantBeThatBad))
            SaveSettings();
    }

    private void BeginAchievementRun()
    {
        _runHitTimes.Clear();
        _runHadAlert = false;
        _runHadQuickDoubleHit = false;
        _runProgressFinalized = false;
        _continuousChaseTime = 0;
        _ghostLoverPursuer = null;
        _lastAchievementMoveDirection = null;
        _anklesCandidate = null;
        _anklesDamageBaseline = 0;
        _anklesWindow = 0;
        _anklesPassedClose = false;

        if (ActiveRunSettings.Strictness == MazeStrictness.Strict &&
            ActiveRunSettings.HollowAmount == RunHollowAmount.None &&
            AwardAchievement(AchievementId.LoveOfTheGame, persistImmediately: false))
            QueueSettingsSave();
    }

    private void UpdateAchievementTracking(float deltaTime)
    {
        _progressionNoticeTimer = Math.Max(0, _progressionNoticeTimer - deltaTime);
        UpdateAchievementToast(deltaTime);
        if (_mode != ScreenMode.Playing || _maze is null) return;

        if ((DateTime.Now - _startedAt).TotalSeconds >= 180)
            AwardAchievement(AchievementId.ImLost);

        // This must be one uninterrupted pursuit by one Hollow. Do not stitch
        // together shorter chases from several enemies, or count the hit/static
        // sequence after the pursuing Hollow has already connected.
        if (_hitEffect > 0)
        {
            _ghostLoverPursuer = null;
            _continuousChaseTime = 0;
        }
        else
        {
            if (_ghostLoverPursuer is null || !_hollows.Contains(_ghostLoverPursuer) ||
                _ghostLoverPursuer.State != HollowState.Chase ||
                !IsLocalAchievementPursuer(_ghostLoverPursuer))
            {
                _ghostLoverPursuer = _hollows
                    .Where(hollow =>
                        hollow.State == HollowState.Chase &&
                        IsLocalAchievementPursuer(hollow))
                    .OrderBy(hollow => AchievementDistance(hollow.VisualCell, _visualCell))
                    .FirstOrDefault();
                _continuousChaseTime = 0;
            }

            if (_ghostLoverPursuer is not null)
            {
                _continuousChaseTime += deltaTime;
                if (_continuousChaseTime >= 15f)
                    AwardAchievement(AchievementId.GhostLover);
            }
        }

        UpdateAnklesCandidate(deltaTime);
        if (_movementArrivalHandled && _visited.Count >= CartographerTileTarget())
            AwardAchievement(AchievementId.TheCartographer);
    }

    private void RecordDetectionForAchievements() => _runHadAlert = true;

    private void RecordHitForAchievements(bool causedByHollow)
    {
        if (causedByHollow)
        {
            var now = DateTime.Now;
            _runHitTimes.RemoveAll(time => (now - time).TotalSeconds > 60);
            _runHitTimes.Add(now);
            if (_runHitTimes.Count >= 2) _runHadQuickDoubleHit = true;
        }
        _continuousChaseTime = 0;
        _ghostLoverPursuer = null;
        _anklesCandidate = null;
    }

    private void RecordMovementForAchievements(Point from, Point to)
    {
        var direction = MovementDirection(from, to);
        if (direction.HasValue && _lastAchievementMoveDirection.HasValue &&
            IsPerpendicularTurn(_lastAchievementMoveDirection.Value, direction.Value) &&
            IsMazeCorner(from, _lastAchievementMoveDirection.Value, direction.Value))
        {
            var candidate = _hollows
                .Where(hollow =>
                    hollow.State == HollowState.Chase &&
                    IsLocalAchievementPursuer(hollow))
                .Select(hollow => new
                {
                    Hollow = hollow,
                    Distance = AchievementDistance(hollow.VisualCell, _visualCell)
                })
                .Where(entry => entry.Distance <= 6.5f)
                .OrderBy(entry => entry.Distance)
                .FirstOrDefault();
            if (candidate is not null)
            {
                _anklesCandidate = candidate.Hollow;
                _anklesDamageBaseline = _totalDamageSustained;
                _anklesWindow = 4.5f;
                _anklesPassedClose = candidate.Distance <= 1.55f;
            }
        }

        if (direction.HasValue) _lastAchievementMoveDirection = direction;
        if (_maze is not null && _visited.Count >= CartographerTileTarget())
            AwardAchievement(AchievementId.TheCartographer);
    }

    private int CartographerTileTarget()
    {
        if (_maze is null) return int.MaxValue;
        var blocked = 0;
        if (_survivorObjective is { } objective)
        {
            if (IsSurvivorBlockingCell(objective.RequesterCell)) blocked++;
            if (objective.WorkerCell != objective.RequesterCell &&
                IsSurvivorBlockingCell(objective.WorkerCell)) blocked++;
        }
        return Math.Max(0, _maze.Width * _maze.Height - blocked);
    }

    private void RecordAchievementWin()
    {
        if (_runProgressFinalized) return;
        _runProgressFinalized = true;

        AwardAchievement(AchievementId.CantBeThatBad, persistImmediately: false);
        if (_wonTime.TotalSeconds < 60)
            AwardAchievement(AchievementId.SpeedDemon, persistImmediately: false);
        if (_totalDamageSustained == 0)
            AwardAchievement(AchievementId.FirstTry, persistImmediately: false);
        if (!_runHadAlert && ActiveRunSettings.HollowAmount != RunHollowAmount.None)
            AwardAchievement(AchievementId.IWantToBeNinja, persistImmediately: false);
        if (_runHadQuickDoubleHit)
            AwardAchievement(AchievementId.LastSurprise, persistImmediately: false);

        var run = ActiveRunSettings;
        if (run.HollowAmount == RunHollowAmount.None)
            AwardAchievement(AchievementId.Wimpy, persistImmediately: false);
        if (run.MapSize == RunMapSize.Small && run.Strictness == MazeStrictness.Loose &&
            run.HollowAmount is RunHollowAmount.None or RunHollowAmount.Small)
            AwardAchievement(AchievementId.BabySteps, persistImmediately: false);
        if (run.MapSize == RunMapSize.Small && run.HollowAmount == RunHollowAmount.Large)
            AwardAchievement(AchievementId.CageMatch, persistImmediately: false);

        var maximumHealth = GetMaximumHealth();
        var fullChallenge = run.MapSize == RunMapSize.Large &&
                            run.Strictness == MazeStrictness.Strict &&
                            run.HollowAmount == RunHollowAmount.Large;
        if (fullChallenge)
        {
            AwardAchievement(AchievementId.IDidItQuestion, persistImmediately: false);
            if (_totalDamageSustained == 0)
                AwardAchievement(AchievementId.IDidIt, persistImmediately: false);
        }
        if (run.Strictness == MazeStrictness.Strict &&
            run.HollowAmount == RunHollowAmount.Large &&
            RemainingHealth * 2 < maximumHealth)
            AwardAchievement(AchievementId.ImpossibleOdds, persistImmediately: false);

        var recoveredAllCargo = _cargoItems
            .Where(item => item.Required)
            .All(item => item.Carried || item.CarrierPlayerId is not null || item.Delivered);
        var sweptAllCredits = _creditPickups.All(pickup => pickup.Collected);
        var liquidatedAllSalvage = _roomSalvage.All(item => item.Sold);
        if (recoveredAllCargo && sweptAllCredits && liquidatedAllSalvage && _totalDamageSustained == 0)
            AwardAchievement(AchievementId.Greedy, persistImmediately: false);

        foreach (var unlocked in _settings.RecordMazeWin())
            QueueAchievementNotification(unlocked);
        QueueSettingsSave();
    }

    private void RecordAchievementAbandonment()
    {
        if (_runProgressFinalized) return;
        _runProgressFinalized = true;
        var now = DateTime.Now;
        var elapsed = now - _startedAt;
        if (_mode == ScreenMode.Shop)
            elapsed -= now - _shopEnteredAt;
        if (elapsed.TotalSeconds <= 10)
            AwardAchievement(AchievementId.Oops);
        _settings.ResetWinStreak();
        SaveSettings();
    }

    private void RecordAchievementFailure()
    {
        if (_runProgressFinalized) return;
        _runProgressFinalized = true;
        _settings.ResetWinStreak();
        SaveSettings();
    }

    private bool AwardAchievement(AchievementId id, bool persistImmediately = true)
    {
        if (!_settings.UnlockAchievement(id)) return false;
        QueueAchievementNotification(id);
        if (persistImmediately) SaveSettings();
        return true;
    }

    private void QueueAchievementNotification(AchievementId id)
    {
        _achievementToastQueue.Enqueue(id);
        if (!_achievementToast.HasValue) ShowNextAchievementToast();
    }

    private void UpdateAchievementToast(float deltaTime)
    {
        if (!_achievementToast.HasValue)
        {
            ShowNextAchievementToast();
            return;
        }

        _achievementToastTimer = Math.Max(0, _achievementToastTimer - deltaTime);
        if (_achievementToastTimer <= 0)
        {
            _achievementToast = null;
            ShowNextAchievementToast();
        }
    }

    private void ShowNextAchievementToast()
    {
        if (_achievementToastQueue.Count == 0) return;
        _achievementToast = _achievementToastQueue.Dequeue();
        _achievementToastTimer = 4.2f;
        _audio.Play(AudioCue.Confirm);
    }

    private void UpdateAnklesCandidate(float deltaTime)
    {
        if (_anklesCandidate is null) return;
        _anklesWindow -= deltaTime;
        if (_anklesWindow <= 0 || _totalDamageSustained != _anklesDamageBaseline ||
            !_hollows.Contains(_anklesCandidate) ||
            !IsLocalAchievementPursuer(_anklesCandidate))
        {
            _anklesCandidate = null;
            return;
        }

        var distance = AchievementDistance(_anklesCandidate.VisualCell, _visualCell);
        if (distance <= 1.55f) _anklesPassedClose = true;
        if (!_anklesPassedClose || distance < 2.25f) return;
        AwardAchievement(AchievementId.Ankles);
        _anklesCandidate = null;
    }

    private static Direction? MovementDirection(Point from, Point to)
    {
        var dx = Math.Sign(to.X - from.X);
        var dy = Math.Sign(to.Y - from.Y);
        if (dx > 0 && dy == 0) return Direction.Right;
        if (dx < 0 && dy == 0) return Direction.Left;
        if (dy > 0 && dx == 0) return Direction.Down;
        if (dy < 0 && dx == 0) return Direction.Up;
        return null;
    }

    private static bool IsPerpendicularTurn(Direction first, Direction second) =>
        first != second && ((int)first + 2) % 4 != (int)second;

    private bool IsMazeCorner(Point cell, Direction incoming, Direction outgoing)
    {
        if (_maze is null || CountBits(_maze.GetOpeningMask(cell.X, cell.Y)) != 2) return false;
        var returnDirection = (Direction)(((int)incoming + 2) % 4);
        return _maze.CanMove(cell, returnDirection) && _maze.CanMove(cell, outgoing);
    }

    private static float AchievementDistance(PointF first, PointF second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private bool IsLocalAchievementPursuer(Hollow hollow) =>
        !IsOnlineGameplayActive ||
        hollow.TargetPlayerId == _onlinePlayerId;
}
