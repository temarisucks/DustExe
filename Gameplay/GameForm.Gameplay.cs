namespace Dust;

internal sealed partial class GameForm
{
    private void StartGame(bool advanceLevel = false, bool preserveLevel = false)
    {
        if (_mode == ScreenMode.Loading) return;
        if (_pauseMenuOpen) SettleOfflinePauseClock();
        ResetPauseMenuState();
        CloseMissionDossier(playSound: false);
        ResetMissionDossier();
        if (_mode == ScreenMode.Playing) RecordAchievementAbandonment();
        StopMusicForGameTransition();
        if (!preserveLevel)
        {
            if (advanceLevel)
            {
                _level++;
                _survivorDifficultyOffset = _activeRunSettings.DifficultyScaling &&
                                            _survivorDifficultyPenaltyPending ? 1 : 0;
            }
            else
            {
                _level = 1;
                _survivorDifficultyOffset = 0;
                _shopProtectionCharges = 0;
                _shopRepairReserve = 0;
            }
            _survivorDifficultyPenaltyPending = false;
        }
        else _level = Math.Max(1, _level);

        _loadingCancellation?.Cancel();
        _loadingCancellation = new CancellationTokenSource();
        var loadSerial = ++_loadingSerial;
        _loadingAge = 0;
        _loadingProgress = .06f;
        _loadingDisplayProgress = 0;
        _loadingStage = "MOUNTING FIELD CASSETTE";
        _loadingFault = false;
        _mode = ScreenMode.Loading;
        ResetHover();
        Invalidate();
        _ = LoadGameAsync(loadSerial, _loadingCancellation.Token);
    }

    private async Task LoadGameAsync(int loadSerial, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Give WinForms a paint cycle before any large resource work begins.
            await Task.Delay(48, cancellationToken);
            if (!IsCurrentLoad(loadSerial, cancellationToken)) return;

            _loadingStage = "DECODING RED SIGNAL BED";
            _loadingProgress = .18f;
            var musicTask = _audio.PrepareMusicAsync(cancellationToken);
            await Task.Run(() => InitializeGameState(cancellationToken), cancellationToken);
            if (!IsCurrentLoad(loadSerial, cancellationToken)) return;

            _loadingStage = "SEALING BEHAVIORAL PLATE";
            _loadingProgress = .64f;
            var musicReady = await musicTask;
            if (!IsCurrentLoad(loadSerial, cancellationToken)) return;

            _loadingStage = musicReady ? "SIGNAL LOCK CONFIRMED" : "SIGNAL BED DEGRADED";
            _loadingProgress = .92f;
            var remaining = 850 - (int)stopwatch.ElapsedMilliseconds;
            if (remaining > 0) await Task.Delay(remaining, cancellationToken);
            if (!IsCurrentLoad(loadSerial, cancellationToken)) return;

            _loadingProgress = 1f;
            _startedAt = DateTime.Now;
            _mode = ScreenMode.Playing;
            BeginAchievementRun();
            _audio.PlayMusic();
        }
        catch (OperationCanceledException)
        {
            // Closing the game or superseding a load is an expected cancellation.
        }
        catch
        {
            if (loadSerial != _loadingSerial || IsDisposed) return;
            _loadingFault = true;
            _loadingStage = "FIELD CASSETTE REJECTED";
            _loadingProgress = 1f;
            await Task.Delay(900);
            if (loadSerial == _loadingSerial && !IsDisposed) EnterTitle();
        }
    }

    private bool IsCurrentLoad(int serial, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && serial == _loadingSerial &&
        !IsDisposed && _mode == ScreenMode.Loading;

    private void InitializeGameState(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PrepareOnlineDeterministicGeneration();
        var dimensions = GetRunMazeDimensions();
        _maze = new Maze(dimensions.Width, dimensions.Height, _random,
            strictness: _activeRunSettings.Strictness);
        _playerCell = RandomCorridorCell();
        _playerPreviousCell = _playerCell;
        _maze.EnsureJunction(_playerCell, _random, RunStartJunctionOpenings);
        _exitCell = FindFarthestCorridorCell(_playerCell);
        _maze.EnsureJunction(_exitCell, _random, RunStartJunctionOpenings);
        _visualCell = _playerCell;
        _previousVisualCell = _playerCell;
        _cameraCell = _playerCell;
        _moveProgress = 1f;
        _movementArrivalHandled = true;
        _droneBank = 0;
        _dronePitch = 0;
        _steps = 0;
        _visited.Clear();
        _visited.Add(_playerCell);
        _impactPulse = 0;
        _transferPulse = 0;
        _damageTaken = 0;
        _totalDamageSustained = 0;
        _hitEffect = 0;
        _invulnerability = 0;
        _teleportDone = false;
        _failurePending = false;
        _cargoLostOnFailure = false;
        _pendingWin = false;
        _warningFlash = 0;
        _warningSoundCooldown = 0;
        ResetPerkRunState();
        SetupMission();
        SpawnHollows();
        SpawnSentries();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void TryMove(Direction direction)
    {
        if (_mode != ScreenMode.Playing || _maze is null || _moveProgress < 1f ||
            _hitEffect > 0 || _pendingWin ||
            IsOnlineGameplayActive && _onlineLocalDefeated) return;
        if (!OnlineGameplayHostAvailable)
        {
            _missionNotice = "AUTHORITY SIGNAL LOST / HOLD POSITION";
            _missionNoticeTimer = 1.8f;
            return;
        }
        var traversal = BuildPlayerTraversal(direction, out var usedGhostForm);
        if (traversal.Count == 0)
        {
            _impactCell = _playerCell;
            _impactPulse = 1;
            return;
        }

        var start = _playerCell;
        BeginRoomDoorTraversal(start, traversal);
        _moveFrom = _visualCell;
        _playerPreviousCell = start;
        _playerCell = traversal[^1];
        _moveTo = _playerCell;
        _moveProgress = 0;
        _movementArrivalHandled = false;
        _steps++;
        BeginPlayerTraversal(start, traversal, usedGhostForm);
        SendOnlineMoveIntent(direction);
        _audio.Play(AudioCue.Move);
        if (_playerCell == _exitCell)
        {
            var transferReady = CircuitObjectiveComplete;
            _pendingWin = transferReady &&
                          (!IsOnlineGameplayActive || IsOnlineSimulationHost);
            if (!transferReady) NotifyCircuitTransferLock();
        }
    }

    private void CompleteWin()
    {
        if (IsOnlineGameplayActive && _onlineLocalDefeated)
        {
            ApplyOnlineCasualtyCompletion(
                Math.Max(0, (long)(DateTime.Now - _startedAt).TotalMilliseconds));
            return;
        }
        if (_pauseMenuOpen) SettleOfflinePauseClock();
        ResetPauseMenuState();
        CloseMissionDossier(playSound: false);
        ResetMissionDossier();
        _wonTime = DateTime.Now - _startedAt;
        _againButton = RectangleF.Empty;
        _menuButton = RectangleF.Empty;
        _mode = ScreenMode.Won;
        _transferPulse = 1;
        _pendingWin = false;
        _audio.StopMusic();
        _audio.Play(AudioCue.MazeClear);
        FinishMission();
        RecordAchievementWin();
        ResetHover();
    }

}
