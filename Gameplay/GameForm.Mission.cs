namespace Dust;

internal sealed partial class GameForm
{
    // The supplied maze-clear stinger is two seconds long. Hold the printer
    // until it finishes so the first typewriter click cannot cut the stinger off.
    private const float ResultPrinterLeadIn = 2.05f;

    private readonly List<CargoItem> _cargoItems = [];
    private readonly List<CreditPickup> _creditPickups = [];
    private readonly Dictionary<int, float> _roomDoorOpenProgress = [];
    private readonly HashSet<int> _revealedRoomIds = [];
    private readonly List<string> _resultLines = [];
    private string _missionNotice = string.Empty;
    private float _missionNoticeTimer;
    private int _fieldCredits;
    private int _jobPay;
    private int _basePay;
    private int _timePay;
    private int _cargoPay;
    private int _allCargoPay;
    private int _missingCargoDock;
    private int _breachDock;
    private int _cargoDelivered;
    private bool _movementArrivalHandled = true;
    private int _cargoRequired;
    private long _creditsBeforeJob;
    private float _resultAge;
    private float _resultPaperHeight;
    private int _resultSelection;
    private long _resultAnimationTimestamp;
    private int _jobPayResultLineIndex = -1;
    private int _accountResultLineIndex = -1;
    private float _roomRevealPulse;
    private Point _lastRevealedDoor;

    private bool ResultTypingComplete => _resultLines.Count > 0 &&
                                         ResultCharacterBudget() >= _resultLines.Sum(line => line.Length + 5);
    private float ResultCompletedPaperHeight => Math.Min(590, 56 + _resultLines.Count * 34 + 86);
    private bool ResultReady => ResultTypingComplete &&
                                Math.Abs(_resultPaperHeight - ResultCompletedPaperHeight) < .75f;

    private void SetupMission()
    {
        _cargoItems.Clear();
        _creditPickups.Clear();
        _roomDoorOpenProgress.Clear();
        _circuitSwitches.Clear();
        _fieldDirectives.Clear();
        _hasCircuitObjective = false;
        _revealedRoomIds.Clear();
        _resultLines.Clear();
        _fieldCredits = 0;
        _jobPay = 0;
        _cargoDelivered = 0;
        _cargoRequired = 0;
        _circuitPay = 0;
        _directivePay = 0;
        _directiveDock = 0;
        _creditsBeforeJob = _settings.TotalCredits;
        _missionNotice = "LOCATE MANIFEST CARGO";
        _missionNoticeTimer = 3.2f;
        _roomRevealPulse = 0;
        _resultAge = 0;
        _resultPaperHeight = 58;
        _resultSelection = 0;
        _resultAnimationTimestamp = 0;
        _jobPayResultLineIndex = -1;
        _accountResultLineIndex = -1;
        if (_maze is null) return;

        var rooms = _maze.Rooms.OrderBy(_ => _random.Next()).ToList();
        var baseCargoRequired = Math.Min(rooms.Count, Math.Clamp(2 + (_level - 1) / 4, 1, 4));
        var kinds = Enum.GetValues<CargoKind>().OrderBy(_ => _random.Next()).ToArray();
        for (var i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            var kind = kinds[i % kinds.Length];
            var cell = room.Cells
                .OrderByDescending(candidate => Manhattan(candidate, room.DoorCell))
                .ThenBy(_ => _random.Next())
                .First();
            _cargoItems.Add(new CargoItem
            {
                Code = CargoCode(kind, 11 + room.Id * 17 + _level * 3),
                Kind = kind,
                Cell = cell,
                RoomId = room.Id,
                Required = false,
                Phase = (float)_random.NextDouble() * MathF.PI * 2
            });
        }

        TrySetupCircuitObjective(rooms);
        _cargoRequired = Math.Max(0, baseCargoRequired - (_hasCircuitObjective ? 2 : 0));
        foreach (var item in _cargoItems.Take(_cargoRequired)) item.Required = true;

        var pickupCount = Math.Clamp(_maze.Width * _maze.Height / 105, 8, 24);
        var occupied = _cargoItems.Select(item => item.Cell)
            .Concat(_circuitSwitches.Select(item => item.Cell))
            .ToHashSet();
        for (var i = 0; i < pickupCount; i++)
        {
            Point cell;
            var attempts = 0;
            do
            {
                cell = new Point(_random.Next(_maze.Width), _random.Next(_maze.Height));
                attempts++;
            } while (attempts < 80 &&
                     (cell == _playerCell || cell == _exitCell || occupied.Contains(cell) ||
                      _creditPickups.Any(pickup => pickup.Cell == cell)));
            if (cell == _playerCell || cell == _exitCell || occupied.Contains(cell) ||
                _creditPickups.Any(pickup => pickup.Cell == cell)) continue;
            var values = new[] { 10, 15, 20, 25, 50 };
            _creditPickups.Add(new CreditPickup
            {
                Cell = cell,
                VisualCell = cell,
                MagnetTargetCell = cell,
                Value = values[_random.Next(values.Length)],
                Phase = (float)_random.NextDouble() * MathF.PI * 2
            });
        }
        SetupCargoRoomContents();
        SetupSurvivorObjective();
        SetupFieldDirectives();
        AssignMissionObjectiveOwners();
        _missionNotice = _hasCircuitObjective
            ? $"NEW ORDERS / CIRCUIT + {LocalDirectiveCount:00} FIELD CONTRACTS"
            : $"NEW ORDERS / CARGO + {LocalDirectiveCount:00} FIELD CONTRACTS";
    }

    private Point RandomCorridorCell()
    {
        if (_maze is null) return Point.Empty;
        var cells = new List<Point>();
        for (var x = 0; x < _maze.Width; x++)
        for (var y = 0; y < _maze.Height; y++)
        {
            var cell = new Point(x, y);
            if (_maze.GetRoomAt(cell) is null) cells.Add(cell);
        }
        return cells.Count > 0 ? cells[_random.Next(cells.Count)] : Point.Empty;
    }

    private Point FindFarthestCorridorCell(Point start)
    {
        if (_maze is null) return start;
        var distances = new int[_maze.Width, _maze.Height];
        for (var x = 0; x < _maze.Width; x++)
        for (var y = 0; y < _maze.Height; y++) distances[x, y] = -1;
        var queue = new Queue<Point>();
        queue.Enqueue(start);
        distances[start.X, start.Y] = 0;
        var farthest = start;
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            if (_maze.GetRoomAt(cell) is null && distances[cell.X, cell.Y] > distances[farthest.X, farthest.Y])
                farthest = cell;
            foreach (var direction in AllDirections)
            {
                if (!_maze.CanMove(cell, direction)) continue;
                var next = _maze.Move(cell, direction);
                if (distances[next.X, next.Y] >= 0) continue;
                distances[next.X, next.Y] = distances[cell.X, cell.Y] + 1;
                queue.Enqueue(next);
            }
        }
        return farthest;
    }

    private void OnPlayerEnteredCell(Point from, Point to)
    {
        if (_maze is null) return;
        if (_maze.TryGetEnteredRoom(from, to, out var room) && _revealedRoomIds.Add(room.Id))
        {
            _lastRevealedDoor = room.DoorCell;
            _roomRevealPulse = 1;
            _missionNotice = $"ROOM {room.Id + 1:00} OPTICS OPEN";
            _missionNoticeTimer = 2.1f;
        }
        CollectLooseCreditsAt(to);
        CollectRoomSalvageAt(to);
        RecordMovementForAchievements(from, to);
    }

    private void UpdateMissionState(float deltaTime)
    {
        _missionNoticeTimer = Math.Max(0, _missionNoticeTimer - deltaTime);
        _roomRevealPulse = Math.Max(0, _roomRevealPulse - deltaTime * .85f);
        foreach (var roomId in _roomDoorOpenProgress.Keys.ToArray())
            _roomDoorOpenProgress[roomId] = Math.Min(1,
                _roomDoorOpenProgress[roomId] + deltaTime * 22f);
    }

    private void BeginRoomDoorTraversal(Point start, IReadOnlyList<Point> traversal)
    {
        if (_maze is null) return;
        var from = start;
        foreach (var to in traversal)
        {
            var room = _maze.GetRoomAt(from) ?? _maze.GetRoomAt(to);
            if (room?.IsDoorTransition(from, to) == true)
                _roomDoorOpenProgress.TryAdd(room.Id, 0);
            from = to;
        }
    }

    private float RoomDoorOpenProgress(int roomId) =>
        _roomDoorOpenProgress.TryGetValue(roomId, out var progress) ? progress : 0;

    private void CollectLooseCreditsAt(Point cell)
    {
        var found = _creditPickups.Where(pickup => !pickup.Collected &&
            (!pickup.MagnetMoving
                ? pickup.Cell == cell
                : DistanceSquared(pickup.VisualCell, _visualCell) <= .18f)).ToList();
        if (found.Count == 0) return;
        var amount = found.Sum(pickup => pickup.Value);
        foreach (var pickup in found) pickup.Collected = true;
        _fieldCredits += amount;
        _missionNotice = $"FIELD CREDIT +{amount:000}";
        _missionNoticeTimer = 1.7f;
        _audio.Play(AudioCue.Collect);
    }

    private static float DistanceSquared(PointF first, PointF second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }

    private void TryPickupCargo()
    {
        if (_mode != ScreenMode.Playing || _moveProgress < 1 || _hitEffect > 0 ||
            IsOnlineGameplayActive && _onlineLocalDefeated) return;
        if (RelayOnlineInteraction()) return;
        if (TryActivateCircuitSwitch()) return;
        if (TryInteractSurvivor()) return;
        if (TryOpenShopAtPlayer()) return;
        if (TryActivateFieldDirective()) return;
        var item = FindCargoInLatchRange();
        if (item is null)
        {
            _missionNotice = TeammateObjectivePrompt() ??
                             "NO CARGO IN LATCH RANGE";
            _missionNoticeTimer = 1.4f;
            _audio.Play(AudioCue.Select);
            return;
        }
        if (!item.Required)
        {
            _missionNotice = $"{item.Code} NOT MANIFESTED";
            _missionNoticeTimer = 2.1f;
            _audio.Play(AudioCue.Select);
            return;
        }
        if (!IsObjectiveAssignedToLocal(item.AssignedPlayerId))
        {
            _missionNotice = $"{item.Code} RESERVED / {ObjectiveOwnerName(item.AssignedPlayerId)}";
            _missionNoticeTimer = 2.4f;
            _audio.Play(AudioCue.Select);
            return;
        }
        item.Carried = !IsOnlineGameplayActive;
        item.CarrierPlayerId = IsOnlineGameplayActive ? _onlinePlayerId : null;
        _missionNotice = $"{item.Code} LATCHED";
        _missionNoticeTimer = 2.1f;
        _audio.Play(AudioCue.Confirm);
    }

    private CargoItem? FindCargoInLatchRange()
    {
        if (_maze is null || _moveProgress < 1) return null;

        // Large cases can obscure their own floor tile or sit tight against a
        // room edge. The latch reaches the current tile and one directly
        // connected neighbour, but never reaches through a closed wall.
        return _cargoItems
            .Where(item => !item.Carried && item.CarrierPlayerId is null &&
                           !item.Delivered && CanLatchCargoAt(item.Cell))
            .OrderByDescending(item => IsObjectiveAssignedToLocal(item.AssignedPlayerId))
            .ThenBy(item => Manhattan(_playerCell, item.Cell))
            .ThenByDescending(item => item.Required)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private bool CanLatchCargoAt(Point cargoCell)
    {
        if (_maze is null) return false;
        if (cargoCell == _playerCell) return true;
        foreach (var direction in AllDirections)
        {
            if (!_maze.CanMove(_playerCell, direction)) continue;
            if (_maze.Move(_playerCell, direction) == cargoCell) return true;
        }
        return false;
    }

    private void DropCarriedCargo()
    {
        var carried = _cargoItems.Where(item =>
            !item.Delivered &&
            (IsOnlineGameplayActive
                ? item.CarrierPlayerId == _onlinePlayerId
                : item.Carried)).ToList();
        if (carried.Count == 0) return;
        var dropCell = new Point(
            Math.Clamp((int)MathF.Round(_visualCell.X), 0, (_maze?.Width ?? 1) - 1),
            Math.Clamp((int)MathF.Round(_visualCell.Y), 0, (_maze?.Height ?? 1) - 1));
        foreach (var item in carried)
        {
            item.Carried = false;
            item.CarrierPlayerId = null;
            item.Cell = dropCell;
        }
        _missionNotice = carried.Count == 1 ? "CARGO LATCH FAILED" : $"{carried.Count:00} CARGO UNITS DROPPED";
        _missionNoticeTimer = 2.8f;
    }

    private void FinishMission()
    {
        var localCargo = LocalRequiredCargoItems.ToList();
        var localSwitches = LocalCircuitSwitches.ToList();
        var localDirectives = LocalFieldDirectives.ToList();
        var localCargoRequired = localCargo.Count;
        _cargoDelivered = localCargo.Count(item =>
            item.Carried || item.CarrierPlayerId is not null || item.Delivered);
        foreach (var item in _cargoItems.Where(item =>
                     item.Required && IsObjectiveAssignedToLocal(item.AssignedPlayerId) &&
                     (item.Carried || item.CarrierPlayerId is not null ||
                      item.Delivered)))
        {
            item.Carried = false;
            item.CarrierPlayerId = null;
            item.Delivered = true;
        }

        var seconds = Math.Max(1, (int)Math.Ceiling(_wonTime.TotalSeconds));
        _basePay = 260 + _level * 40;
        _timePay = Math.Max(0, (210 + _level * 18 - seconds) * 3);
        _cargoPay = _cargoDelivered * 240;
        _circuitPay = localSwitches.Count(item => item.Activated) * 240;
        var completedDirectives = localDirectives.Count(item => item.IsComplete);
        _directivePay = completedDirectives * 260;
        _directiveDock = Math.Max(0, localDirectives.Count - completedDirectives) * 130;
        var localCircuitComplete = localSwitches.All(item => item.Activated);
        var allAssignedObjectivesComplete =
            _cargoDelivered == localCargoRequired &&
            localCircuitComplete &&
            completedDirectives == localDirectives.Count &&
            (localCargoRequired > 0 || localSwitches.Count > 0 || localDirectives.Count > 0);
        _allCargoPay = allAssignedObjectivesComplete ? 320 + _level * 35 : 0;
        _missingCargoDock = Math.Max(0, localCargoRequired - _cargoDelivered) * 220;
        _breachDock = _totalDamageSustained * 45;
        _jobPay = Math.Max(25,
            _basePay + _timePay + _cargoPay + _circuitPay + _directivePay +
            _allCargoPay + _fieldCredits -
            _missingCargoDock - _directiveDock - _breachDock);
        _settings.AwardCredits(_jobPay);

        var survivorAbandoned = _survivorObjective is { IsResolved: false };
        var localSurvivorAbandoned = survivorAbandoned && IsLocalSurvivorObjective;
        _survivorDifficultyPenaltyPending = survivorAbandoned && _activeRunSettings.DifficultyScaling;

        _resultLines.Clear();
        _survivorReportLineIndex = -1;
        _survivorStatusLineIndex = -1;
        _resultLines.Add("PLATE 31 / CYCLE RECORD");
        _resultLines.Add("TRANSFER COMPLETE");
        _resultLines.Add($"CYCLE {_wonTime.Minutes:00}:{_wonTime.Seconds:00}  RESP {_steps:000}");
        _resultLines.Add(
            $"ORDERS {completedDirectives:00}/{localDirectives.Count:00}  CARGO {_cargoDelivered:00}/{localCargoRequired:00}  SW {localSwitches.Count(item => item.Activated):00}/{localSwitches.Count:00}");
        _resultLines.Add($"FIELD CREDIT  +{_fieldCredits:0000}");
        _resultLines.Add($"TIME RATE     +{_timePay:0000}");
        _resultLines.Add($"OBJECTIVE RATE +{_cargoPay + _circuitPay + _directivePay + _allCargoPay:0000}");
        _resultLines.Add($"BREACH DOCK   -{_breachDock:0000}");
        _resultLines.Add($"MISSING DOCK  -{_missingCargoDock:0000}");
        _resultLines.Add($"CONTRACT DOCK -{_directiveDock:0000}");
        if (_survivorObjective is { } survivor && IsLocalSurvivorObjective)
        {
            _survivorStatusLineIndex = _resultLines.Count;
            if (localSurvivorAbandoned)
            {
                _survivorReportLineIndex = _survivorStatusLineIndex;
                _resultLines.Add($"You left {survivor.WorkerName} to die.");
            }
            else
                _resultLines.Add($"SURVIVOR      {survivor.WorkerName} RETURNED");
        }
        _jobPayResultLineIndex = _resultLines.Count;
        _resultLines.Add($"JOB PAY       +{_jobPay:0000}");
        _accountResultLineIndex = _resultLines.Count;
        _resultLines.Add($"ACCOUNT       {_settings.TotalCredits:000000}");
        _resultAge = -ResultPrinterLeadIn;
        _resultPaperHeight = 58;
        _resultSelection = 0;
        _resultAnimationTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    private void UpdateResultAnimation(float deltaTime)
    {
        if (_resultLines.Count == 0) return;
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var measuredDelta = _resultAnimationTimestamp <= 0
            ? deltaTime
            : (float)System.Diagnostics.Stopwatch.GetElapsedTime(
                _resultAnimationTimestamp, now).TotalSeconds;
        _resultAnimationTimestamp = now;
        // Rendering cost varies with maze size. The printer follows elapsed time
        // rather than a fixed 16 ms assumption, while the cap prevents a long
        // minimize/suspend from silently completing the whole record.
        deltaTime = Math.Clamp(measuredDelta, 0, .12f);
        var previouslyVisible = ResultVisibleCharacterCount(ResultCharacterBudget());
        _resultAge += deltaTime;
        var budget = ResultCharacterBudget();
        var newlyVisible = ResultVisibleCharacterCount(budget) - previouslyVisible;
        for (var character = 0; character < newlyVisible; character++)
            _audio.Play(AudioCue.Type);
        var startedLines = 0;
        foreach (var line in _resultLines)
        {
            if (budget <= 0) break;
            startedLines++;
            budget -= line.Length + 5;
        }
        var target = 56 + startedLines * 34 + (ResultTypingComplete ? 86 : 0);
        _resultPaperHeight += (target - _resultPaperHeight) * Math.Min(1, deltaTime * 9);
    }

    private int ResultCharacterBudget() => (int)(_resultAge * 43);

    private int ResultVisibleCharacterCount(int budget)
    {
        var count = 0;
        foreach (var line in _resultLines)
        {
            if (budget <= 0) break;
            var visible = Math.Min(line.Length, budget);
            count += visible;
            if (visible < line.Length) break;
            budget -= line.Length + 5;
        }
        return count;
    }

    private bool IsCellConcealed(Point cell)
    {
        var room = _maze?.GetRoomAt(cell);
        return room is not null && !_revealedRoomIds.Contains(room.Id);
    }

    private bool IsPositionConcealed(PointF position) => IsCellConcealed(new Point(
        Math.Clamp((int)MathF.Round(position.X), 0, (_maze?.Width ?? 1) - 1),
        Math.Clamp((int)MathF.Round(position.Y), 0, (_maze?.Height ?? 1) - 1)));

    private static string CargoCode(CargoKind kind, int serial)
    {
        var prefix = kind switch
        {
            CargoKind.SignalRelay => "RELAY",
            CargoKind.CryoCell => "CRYO",
            CargoKind.TissueArchive => "ARCH",
            CargoKind.SurveyCore => "CORE",
            CargoKind.BlackRecorder => "BLACK",
            _ => "RESIN"
        };
        return $"{prefix}-{serial % 100:00}";
    }

    private static string CargoName(CargoKind kind) => kind switch
    {
        CargoKind.SignalRelay => "SIGNAL RELAY",
        CargoKind.CryoCell => "CRYO CELL",
        CargoKind.TissueArchive => "TISSUE ARCHIVE",
        CargoKind.SurveyCore => "SURVEY CORE",
        CargoKind.BlackRecorder => "BLACK RECORDER",
        _ => "RESIN SAMPLE"
    };
}
