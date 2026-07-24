namespace Dust;

internal sealed partial class GameForm
{
    private readonly List<FieldDirective> _fieldDirectives = [];
    private readonly List<ObjectiveRunPlayer> _objectiveRunPlayers = [];
    private int _directivePay;
    private int _directiveDock;

    private IEnumerable<FieldDirective> LocalFieldDirectives =>
        _fieldDirectives.Where(item => IsObjectiveAssignedToLocal(item.AssignedPlayerId));

    private int LocalDirectiveCount => LocalFieldDirectives.Count();
    private int LocalCompletedDirectiveCount => LocalFieldDirectives.Count(item => item.IsComplete);

    private void CaptureOnlineObjectiveRoster(OnlineLobbyState state)
    {
        _objectiveRunPlayers.Clear();
        var runPlayers = state.RunStartPlayers.Count > 0
            ? state.RunStartPlayers
            : state.Players.Select(player => new OnlineRunPlayer(
                player.PlayerId, player.Username, player.JoinOrder));
        _objectiveRunPlayers.AddRange(runPlayers
            .OrderBy(player => player.JoinOrder)
            .ThenBy(player => player.PlayerId, StringComparer.Ordinal)
            .Select(player => new ObjectiveRunPlayer(
                player.PlayerId, player.Username, player.JoinOrder)));
    }

    private void SetupFieldDirectives()
    {
        _fieldDirectives.Clear();
        _directivePay = 0;
        _directiveDock = 0;
        if (_maze is null || _maze.Rooms.Count == 0) return;

        var owners = ObjectiveAssignmentOwners();
        var directivesPerOwner = _activeRunSettings.MapSize switch
        {
            RunMapSize.Small => 2,
            RunMapSize.Large => 4,
            _ => 3
        };
        var occupied = _cargoItems.Select(item => item.Cell)
            .Concat(_creditPickups.Select(item => item.Cell))
            .Concat(_circuitSwitches.Select(item => item.Cell))
            .Concat(_roomProps.Select(item => item.Cell))
            .Concat(_roomSalvage.Select(item => item.Cell))
            .ToHashSet();
        if (_shopKiosk is not null)
        {
            // Keep the counter and its one-tile interaction apron free of
            // personal fixtures so E always means "enter shop" beside it.
            occupied.Add(_shopKiosk.Cell);
            foreach (var roomCell in _maze.Rooms
                         .SelectMany(room => room.Cells)
                         .Where(cell => Manhattan(cell, _shopKiosk.Cell) == 1))
                occupied.Add(roomCell);
        }
        if (_survivorObjective is { } survivor)
        {
            occupied.Add(survivor.RequesterCell);
            occupied.Add(survivor.WorkerCell);
        }

        var roomCandidates = _maze.Rooms.ToDictionary(
            room => room.Id,
            room =>
            {
                var perimeter = room.Cells
                    .Where(cell => cell != room.DoorCell &&
                                   Manhattan(cell, room.DoorCell) > 1 &&
                                   IsRoomPerimeterCell(room, cell) &&
                                   !occupied.Contains(cell))
                    .OrderByDescending(cell => RoomWallSides(room, cell).Count == 1)
                    .ThenBy(_ => _random.Next())
                    .ToList();
                var interior = room.Cells
                    .Where(cell => cell != room.DoorCell &&
                                   Manhattan(cell, room.DoorCell) > 1 &&
                                   !occupied.Contains(cell) &&
                                   !perimeter.Contains(cell))
                    .OrderBy(_ => _random.Next())
                    .ToList();
                perimeter.AddRange(interior);
                var reserve = room.Cells
                    .Where(cell => cell != room.DoorCell &&
                                   !occupied.Contains(cell) &&
                                   !perimeter.Contains(cell))
                    .OrderBy(_ => _random.Next())
                    .ToList();
                perimeter.AddRange(reserve);
                return perimeter;
            });
        var kindOffset = _random.Next(4);
        var globalTask = 0;

        // Allocate one round to every unit before beginning the next. If an
        // unusually cramped seed exhausts fixture cells, no late-join unit is
        // silently starved of its entire personal contract load.
        for (var taskIndex = 0; taskIndex < directivesPerOwner; taskIndex++)
        for (var ownerSlot = 0; ownerSlot < owners.Count; ownerSlot++)
        {
            var kind = (FieldDirectiveKind)
                ((kindOffset + ownerSlot + taskIndex) % 4);
            var nodeCount = FieldDirectiveNodeCount(kind);
            var nodes = new List<FieldDirectiveNode>(nodeCount);
            var selectedCells = new List<(int RoomId, Point Cell)>(nodeCount);
            var usedRooms = new HashSet<int>();

            for (var nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                var preferredRoom = (globalTask + nodeIndex * 2 + ownerSlot) %
                                    _maze.Rooms.Count;
                if (!TryTakeDirectiveCell(
                        roomCandidates, preferredRoom, usedRooms,
                        out var roomId, out var cell))
                    break;
                occupied.Add(cell);
                usedRooms.Add(roomId);
                selectedCells.Add((roomId, cell));
                nodes.Add(new FieldDirectiveNode
                {
                    Number = nodeIndex + 1,
                    RoomId = roomId,
                    Cell = cell,
                    WallSide = SelectRoomWallSide(
                        _maze.Rooms.First(room => room.Id == roomId),
                        cell,
                        globalTask * 31 + nodeIndex * 7 + ownerSlot),
                    Phase = (float)_random.NextDouble() * MathF.PI * 2
                });
            }

            if (nodes.Count == nodeCount)
            {
                _fieldDirectives.Add(new FieldDirective
                {
                    Id = ownerSlot * 100 + taskIndex,
                    Kind = kind,
                    AssignedPlayerId = owners[ownerSlot],
                    Nodes = nodes
                });
            }
            else
            {
                // A partial task is never exposed. Return its reserved fixture
                // cells so a later, smaller order can still be generated.
                foreach (var selected in selectedCells)
                {
                    occupied.Remove(selected.Cell);
                    roomCandidates[selected.RoomId].Add(selected.Cell);
                }
            }
            globalTask++;
        }
    }

    private bool TryTakeDirectiveCell(
        IReadOnlyDictionary<int, List<Point>> roomCandidates,
        int preferredRoom,
        ISet<int> usedRooms,
        out int roomId,
        out Point cell)
    {
        if (_maze is null)
        {
            roomId = -1;
            cell = Point.Empty;
            return false;
        }

        // Multi-node orders are deliberately spread across rooms whenever
        // possible, making them exploration tasks instead of button clusters.
        for (var pass = 0; pass < 2; pass++)
        for (var offset = 0; offset < _maze.Rooms.Count; offset++)
        {
            var room = _maze.Rooms[(preferredRoom + offset) % _maze.Rooms.Count];
            if (pass == 0 && usedRooms.Contains(room.Id)) continue;
            if (!roomCandidates.TryGetValue(room.Id, out var candidates) ||
                candidates.Count == 0)
                continue;
            // Candidate lists are tiered: sealed perimeter first, then interior,
            // then near-door reserve cells. Consume from the front so fixtures
            // use a real wall whenever the room has one available.
            cell = candidates[0];
            candidates.RemoveAt(0);
            roomId = room.Id;
            return true;
        }

        roomId = -1;
        cell = Point.Empty;
        return false;
    }

    private void AssignMissionObjectiveOwners()
    {
        var owners = ObjectiveAssignmentOwners();
        if (owners.Count == 1 && owners[0] is null)
        {
            foreach (var cargo in _cargoItems) cargo.AssignedPlayerId = null;
            foreach (var circuitSwitch in _circuitSwitches)
                circuitSwitch.AssignedPlayerId = null;
            if (_survivorObjective is not null)
                _survivorObjective.AssignedPlayerId = null;
            return;
        }

        var assignmentIndex = 0;
        foreach (var cargo in _cargoItems.Where(item => item.Required)
                     .OrderBy(item => item.RoomId)
                     .ThenBy(item => item.Code, StringComparer.Ordinal))
            cargo.AssignedPlayerId = owners[assignmentIndex++ % owners.Count];
        foreach (var circuitSwitch in _circuitSwitches.OrderBy(item => item.Number))
            circuitSwitch.AssignedPlayerId = owners[assignmentIndex++ % owners.Count];
        if (_survivorObjective is not null)
            _survivorObjective.AssignedPlayerId =
                owners[assignmentIndex % owners.Count];
    }

    private IReadOnlyList<string?> ObjectiveAssignmentOwners()
    {
        if (!IsOnlineGameplayActive || _objectiveRunPlayers.Count == 0)
            return new string?[] { null };
        return _objectiveRunPlayers.Select(player => (string?)player.PlayerId).ToArray();
    }

    private bool IsObjectiveAssignedToLocal(string? ownerPlayerId) =>
        !IsOnlineGameplayActive ||
        string.IsNullOrWhiteSpace(ownerPlayerId) ||
        string.Equals(ownerPlayerId, _onlinePlayerId, StringComparison.Ordinal);

    private static bool IsObjectiveAssignedToPlayer(
        string? ownerPlayerId,
        string playerId) =>
        string.IsNullOrWhiteSpace(ownerPlayerId) ||
        string.Equals(ownerPlayerId, playerId, StringComparison.Ordinal);

    private string ObjectiveOwnerName(string? ownerPlayerId)
    {
        if (string.IsNullOrWhiteSpace(ownerPlayerId)) return "TEAM";
        if (string.Equals(ownerPlayerId, _onlinePlayerId, StringComparison.Ordinal))
            return (_onlineUsername ?? "YOUR UNIT").ToUpperInvariant();
        return _objectiveRunPlayers.FirstOrDefault(player =>
                   player.PlayerId == ownerPlayerId)?.Username.ToUpperInvariant()
               ?? OnlineCarrierName(ownerPlayerId);
    }

    private IEnumerable<CargoItem> LocalRequiredCargoItems =>
        _cargoItems.Where(item => item.Required &&
                                  IsObjectiveAssignedToLocal(item.AssignedPlayerId));

    private IEnumerable<CircuitSwitch> LocalCircuitSwitches =>
        _circuitSwitches.Where(item =>
            IsObjectiveAssignedToLocal(item.AssignedPlayerId));

    private bool IsLocalSurvivorObjective =>
        _survivorObjective is { } survivor &&
        IsObjectiveAssignedToLocal(survivor.AssignedPlayerId);

    private FieldDirectiveTarget? FindFieldDirectiveTargetInRange(Point playerCell)
    {
        if (_maze is null) return null;
        return _fieldDirectives
            .SelectMany(directive => directive.Nodes.Select((node, index) =>
                new FieldDirectiveTarget(directive, node, index)))
            .Where(target => !target.Directive.IsNodeActive(target.NodeIndex) &&
                             CanInteractWithMissionCell(playerCell, target.Node.Cell))
            .OrderByDescending(target =>
                IsObjectiveAssignedToLocal(target.Directive.AssignedPlayerId))
            .ThenBy(target => Manhattan(playerCell, target.Node.Cell))
            .ThenBy(target => target.Directive.Id)
            .ThenBy(target => target.NodeIndex)
            .Cast<FieldDirectiveTarget?>()
            .FirstOrDefault();
    }

    private bool CanInteractWithMissionCell(Point playerCell, Point target)
    {
        if (playerCell == target) return true;
        if (_maze is null) return false;
        foreach (var direction in AllDirections)
            if (_maze.CanMove(playerCell, direction) &&
                _maze.Move(playerCell, direction) == target)
                return true;
        return false;
    }

    private bool TryActivateFieldDirective()
    {
        var target = FindFieldDirectiveTargetInRange(_playerCell);
        if (target is null) return false;
        var value = target.Value;
        if (!IsObjectiveAssignedToLocal(value.Directive.AssignedPlayerId))
            return false;
        if (!value.Directive.CanActivate(value.NodeIndex))
        {
            _missionNotice =
                $"SEQUENCE LOCK / CALIBRATE NODE {value.Directive.ActivatedCount + 1:00}";
            _missionNoticeTimer = 2.5f;
            _audio.Play(AudioCue.Select);
            return true;
        }

        value.Directive.Activate(value.NodeIndex);
        _missionNotice = value.Directive.IsComplete
            ? $"{FieldDirectiveName(value.Directive.Kind)} / CONTRACT CLOSED"
            : $"{FieldDirectiveVerb(value.Directive.Kind)} {value.Node.Number:00}/{value.Directive.Nodes.Count:00}";
        _missionNoticeTimer = value.Directive.IsComplete ? 3.2f : 2.4f;
        _audio.Play(AudioCue.Confirm);
        return true;
    }

    private bool TryActivateOnlineFieldDirective(OnlineRemotePlayer player)
    {
        var target = _fieldDirectives
            .SelectMany(directive => directive.Nodes.Select((node, index) =>
                new FieldDirectiveTarget(directive, node, index)))
            .Where(value => !value.Directive.IsNodeActive(value.NodeIndex) &&
                            CanOnlineInteract(player.Cell, value.Node.Cell))
            .OrderByDescending(value => IsObjectiveAssignedToPlayer(
                value.Directive.AssignedPlayerId, player.PlayerId))
            .ThenBy(value => Manhattan(player.Cell, value.Node.Cell))
            .ThenBy(value => value.Directive.Id)
            .ThenBy(value => value.NodeIndex)
            .Cast<FieldDirectiveTarget?>()
            .FirstOrDefault();
        if (target is null) return false;
        var value = target.Value;
        if (!IsObjectiveAssignedToPlayer(
                value.Directive.AssignedPlayerId, player.PlayerId))
            return false;
        if (value.Directive.CanActivate(value.NodeIndex))
            value.Directive.Activate(value.NodeIndex);
        return true;
    }

    private string? FieldDirectivePrompt()
    {
        var target = FindFieldDirectiveTargetInRange(_playerCell);
        if (target is null) return null;
        var value = target.Value;
        if (!IsObjectiveAssignedToLocal(value.Directive.AssignedPlayerId))
            return null;
        if (!value.Directive.CanActivate(value.NodeIndex))
            return $"NODE {value.Node.Number:00} SEALED / CALIBRATE {value.Directive.ActivatedCount + 1:00} FIRST";
        return $"E / {FieldDirectiveAction(value.Directive.Kind)} {value.Node.Number:00}/{value.Directive.Nodes.Count:00}";
    }

    private string? TeammateObjectivePrompt()
    {
        var circuitSwitch = _circuitSwitches
            .Where(item => !item.Activated &&
                           !IsObjectiveAssignedToLocal(item.AssignedPlayerId) &&
                           CanInteractWithMissionCell(item.Cell))
            .OrderBy(item => Manhattan(_playerCell, item.Cell))
            .FirstOrDefault();
        if (circuitSwitch is not null)
            return $"SWITCH {circuitSwitch.Number:00} / ASSIGNED {ObjectiveOwnerName(circuitSwitch.AssignedPlayerId)}";

        if (_survivorObjective is { } survivor &&
            !IsObjectiveAssignedToLocal(survivor.AssignedPlayerId) &&
            (IsPlayerInSurvivorRange(survivor.WorkerCell) ||
             IsPlayerInSurvivorRange(survivor.RequesterCell)))
            return $"PERSONNEL ORDER / ASSIGNED {ObjectiveOwnerName(survivor.AssignedPlayerId)}";

        var directiveTarget = FindFieldDirectiveTargetInRange(_playerCell);
        if (directiveTarget is { } target &&
            !IsObjectiveAssignedToLocal(target.Directive.AssignedPlayerId))
            return $"{FieldDirectiveName(target.Directive.Kind)} / ASSIGNED {ObjectiveOwnerName(target.Directive.AssignedPlayerId)}";
        return null;
    }

    private void ReassignObjectivesFromUnavailableOwners(
        IReadOnlySet<string> availablePlayerIds)
    {
        if (!IsOnlineSimulationHost ||
            _mode is not (ScreenMode.Playing or ScreenMode.Shop) ||
            availablePlayerIds.Count == 0)
            return;
        if (_survivorObjective is
            {
                Stage: SurvivorObjectiveStage.Escorting,
                EscortPlayerId: { } escortId
            } escorted &&
            !availablePlayerIds.Contains(escortId))
        {
            escorted.Stage = SurvivorObjectiveStage.Searching;
            escorted.EscortPlayerId = null;
        }
        var candidates = _objectiveRunPlayers
            .Where(player => availablePlayerIds.Contains(player.PlayerId))
            .OrderBy(player => player.JoinOrder)
            .ThenBy(player => player.PlayerId, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 0) return;

        string NextOwner()
        {
            return candidates
                .OrderBy(player => IncompleteObjectiveLoad(player.PlayerId))
                .ThenBy(player => player.JoinOrder)
                .First().PlayerId;
        }

        foreach (var cargo in _cargoItems.Where(item =>
                     item.Required && !item.Delivered &&
                     item.AssignedPlayerId is { } owner &&
                     !availablePlayerIds.Contains(owner)))
            cargo.AssignedPlayerId = NextOwner();
        foreach (var circuitSwitch in _circuitSwitches.Where(item =>
                     !item.Activated &&
                     item.AssignedPlayerId is { } owner &&
                     !availablePlayerIds.Contains(owner)))
            circuitSwitch.AssignedPlayerId = NextOwner();
        foreach (var directive in _fieldDirectives.Where(item =>
                     !item.IsComplete &&
                     item.AssignedPlayerId is { } owner &&
                     !availablePlayerIds.Contains(owner)))
            directive.AssignedPlayerId = NextOwner();
        if (_survivorObjective is { IsResolved: false, AssignedPlayerId: { } survivorOwner } &&
            !availablePlayerIds.Contains(survivorOwner))
            _survivorObjective.AssignedPlayerId = NextOwner();
    }

    private IReadOnlySet<string> AvailableObjectivePlayerIds()
    {
        var available = (_onlineLobby?.Players ?? [])
            .Select(player => player.PlayerId)
            .ToHashSet(StringComparer.Ordinal);
        if (_onlineLocalDefeated && _onlinePlayerId is { } localId)
            available.Remove(localId);
        foreach (var player in _onlinePlayers.Values.Where(player => player.Defeated))
            available.Remove(player.PlayerId);
        return available;
    }

    private int IncompleteObjectiveLoad(string playerId) =>
        _cargoItems.Count(item => item.Required && !item.Delivered &&
                                  item.AssignedPlayerId == playerId) +
        _circuitSwitches.Count(item => !item.Activated &&
                                       item.AssignedPlayerId == playerId) +
        _fieldDirectives.Count(item => !item.IsComplete &&
                                       item.AssignedPlayerId == playerId) +
        (_survivorObjective is { IsResolved: false } survivor &&
         survivor.AssignedPlayerId == playerId ? 1 : 0);

    private static int FieldDirectiveNodeCount(FieldDirectiveKind kind) => kind switch
    {
        FieldDirectiveKind.ArchiveDecrypt => 1,
        FieldDirectiveKind.SignalCalibrate => 3,
        _ => 2
    };

    private static string FieldDirectiveName(FieldDirectiveKind kind) => kind switch
    {
        FieldDirectiveKind.ArchiveDecrypt => "ARCHIVE DECRYPT",
        FieldDirectiveKind.PressurePurge => "PRESSURE PURGE",
        FieldDirectiveKind.SignalCalibrate => "SIGNAL CALIBRATION",
        _ => "SPECIMEN SEAL"
    };

    private static string FieldDirectiveVerb(FieldDirectiveKind kind) => kind switch
    {
        FieldDirectiveKind.ArchiveDecrypt => "ARCHIVE DECRYPTED",
        FieldDirectiveKind.PressurePurge => "PRESSURE VENTED",
        FieldDirectiveKind.SignalCalibrate => "SIGNAL NODE TUNED",
        _ => "CONTAINMENT CLAMPED"
    };

    private static string FieldDirectiveAction(FieldDirectiveKind kind) => kind switch
    {
        FieldDirectiveKind.ArchiveDecrypt => "DECRYPT ARCHIVE",
        FieldDirectiveKind.PressurePurge => "PURGE VALVE",
        FieldDirectiveKind.SignalCalibrate => "CALIBRATE SIGNAL",
        _ => "SEAL SPECIMEN"
    };

    private readonly record struct FieldDirectiveTarget(
        FieldDirective Directive,
        FieldDirectiveNode Node,
        int NodeIndex);
}
