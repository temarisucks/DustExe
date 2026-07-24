namespace Dust;

internal sealed partial class GameForm
{
    private static readonly string[] SurvivorNames =
    [
        "Mara Voss", "Eli Mercer", "Nadia Price", "Jonah Reed",
        "Tessa Ward", "Owen Vale", "Iris Shaw", "Caleb Moss",
        "Lena Ortiz", "Simon Bell", "Rhea Cole", "Micah Dunn",
        "Vera Holt", "Noah Keene", "June Park", "Adrian Cross",
        "Mina Graves", "Theo Brooks", "Sasha Wynn", "Emmett Hale"
    ];

    private SurvivorObjective? _survivorObjective;
    private bool _survivorDifficultyPenaltyPending;
    private int _survivorDifficultyOffset;
    private int _survivorReportLineIndex = -1;
    private int _survivorStatusLineIndex = -1;

    private void SetupSurvivorObjective()
    {
        _survivorObjective = null;
        _survivorReportLineIndex = -1;
        _survivorStatusLineIndex = -1;
        if (_maze is null || _maze.Rooms.Count == 0) return;

        var occupied = _cargoItems.Select(item => item.Cell)
            .Concat(_creditPickups.Select(item => item.Cell))
            .Concat(_circuitSwitches.Select(item => item.Cell))
            .Concat(_roomProps.Select(item => item.Cell))
            .Concat(_roomSalvage.Select(item => item.Cell))
            .ToHashSet();
        if (_shopKiosk is not null) occupied.Add(_shopKiosk.Cell);

        var requesterRooms = _maze.Rooms
            .Where(room => _shopKiosk is null || room.Id != _shopKiosk.RoomId)
            .OrderBy(_ => _random.Next())
            .ToList();
        if (requesterRooms.Count == 0) requesterRooms = _maze.Rooms.OrderBy(_ => _random.Next()).ToList();

        CargoRoom? requesterRoom = null;
        Point requesterCell = Point.Empty;
        foreach (var room in requesterRooms)
        {
            var candidates = room.Cells
                .Where(cell => cell != room.DoorCell && !occupied.Contains(cell))
                .OrderByDescending(cell => Manhattan(cell, room.DoorCell))
                .ThenBy(_ => _random.Next())
                .ToList();
            if (candidates.Count == 0) continue;
            requesterRoom = room;
            requesterCell = candidates[0];
            break;
        }
        if (requesterRoom is null) return;
        occupied.Add(requesterCell);

        // The worker is placed on a connected corridor tile, far enough away to
        // demand exploration but never on the exit, start, another objective, or
        // a concealed room fixture.
        var workerCandidates = new List<Point>();
        for (var x = 0; x < _maze.Width; x++)
        for (var y = 0; y < _maze.Height; y++)
        {
            var cell = new Point(x, y);
            if (_maze.GetRoomAt(cell) is not null || occupied.Contains(cell) ||
                cell == _playerCell || cell == _exitCell) continue;
            if (Manhattan(cell, requesterCell) < 9 || Manhattan(cell, _playerCell) < 5) continue;
            workerCandidates.Add(cell);
        }
        if (workerCandidates.Count == 0) return;

        var workerCell = workerCandidates
            .OrderByDescending(cell => Manhattan(cell, requesterCell))
            .Take(Math.Min(24, workerCandidates.Count))
            .OrderBy(_ => _random.Next())
            .First();
        _survivorObjective = new SurvivorObjective
        {
            WorkerName = SurvivorNames[_random.Next(SurvivorNames.Length)],
            RequesterRoomId = requesterRoom.Id,
            RequesterCell = requesterCell,
            WorkerCell = workerCell,
            VisualPhase = (float)_random.NextDouble() * MathF.PI * 2,
            Stage = SurvivorObjectiveStage.Uncontacted
        };
    }

    private bool TryInteractSurvivor()
    {
        if (_mode != ScreenMode.Playing || _moveProgress < 1 || _hitEffect > 0 ||
            _survivorObjective is not { } objective) return false;
        if (!IsObjectiveAssignedToLocal(objective.AssignedPlayerId) &&
            (IsPlayerInSurvivorRange(objective.WorkerCell) ||
             IsPlayerInSurvivorRange(objective.RequesterCell)))
            return false;

        if (IsPlayerInSurvivorRange(objective.WorkerCell) &&
            objective.Stage is SurvivorObjectiveStage.Uncontacted or SurvivorObjectiveStage.Searching)
        {
            objective.Stage = SurvivorObjectiveStage.Escorting;
            objective.EscortPlayerId = IsOnlineGameplayActive ? _onlinePlayerId : null;
            // The drone scans the worker's occupied tile while attaching the
            // escort tether, so it counts as surveyed without requiring overlap.
            _visited.Add(objective.WorkerCell);
            _missionNotice = $"FOUND {objective.WorkerName.ToUpperInvariant()} / RETURN TO ROOM {objective.RequesterRoomId + 1:00}";
            _missionNoticeTimer = 3.4f;
            _audio.Play(AudioCue.Confirm);
            return true;
        }

        if (!IsPlayerInSurvivorRange(objective.RequesterCell) ||
            objective.Stage == SurvivorObjectiveStage.Rescued) return false;
        switch (objective.Stage)
        {
            case SurvivorObjectiveStage.Uncontacted:
                objective.Stage = SurvivorObjectiveStage.Searching;
                _missionNotice = $"PLEASE FIND {objective.WorkerName.ToUpperInvariant()} / THEY NEVER CAME BACK";
                _missionNoticeTimer = 4.2f;
                break;
            case SurvivorObjectiveStage.Searching:
                _missionNotice = $"{objective.WorkerName.ToUpperInvariant()} / LAST SIGNAL OUTSIDE STORAGE";
                _missionNoticeTimer = 3.2f;
                break;
            case SurvivorObjectiveStage.Escorting:
                if (IsOnlineGameplayActive &&
                    objective.EscortPlayerId != _onlinePlayerId)
                    return false;
                objective.Stage = SurvivorObjectiveStage.Rescued;
                objective.EscortPlayerId = null;
                _missionNotice = $"{objective.WorkerName.ToUpperInvariant()} RETURNED / RESCUE COMPLETE";
                _missionNoticeTimer = 3.8f;
                break;
            default:
                return false;
        }
        _audio.Play(AudioCue.Confirm);
        return true;
    }

    private string? SurvivorInteractionPrompt()
    {
        if (_survivorObjective is not { } objective) return null;
        if (!IsObjectiveAssignedToLocal(objective.AssignedPlayerId) &&
            (IsPlayerInSurvivorRange(objective.WorkerCell) ||
             IsPlayerInSurvivorRange(objective.RequesterCell)))
            return null;
        if (IsPlayerInSurvivorRange(objective.WorkerCell) &&
            objective.Stage is SurvivorObjectiveStage.Uncontacted or SurvivorObjectiveStage.Searching)
            return $"E  HELP {objective.WorkerName.ToUpperInvariant()}";
        if (!IsPlayerInSurvivorRange(objective.RequesterCell)) return null;
        return objective.Stage switch
        {
            SurvivorObjectiveStage.Uncontacted => "E  ANSWER DISTRESS REQUEST",
            SurvivorObjectiveStage.Searching => $"E  ASK ABOUT {objective.WorkerName.ToUpperInvariant()}",
            SurvivorObjectiveStage.Escorting
                when !IsOnlineGameplayActive || objective.EscortPlayerId == _onlinePlayerId =>
                $"E  RETURN {objective.WorkerName.ToUpperInvariant()}",
            _ => null
        };
    }

    private bool IsSurvivorBlockingCell(Point cell) =>
        _survivorObjective is { } objective &&
        (cell == objective.RequesterCell ||
         objective.Stage is not (SurvivorObjectiveStage.Escorting or SurvivorObjectiveStage.Rescued) &&
         cell == objective.WorkerCell);

    private bool IsSurvivorPlacementCell(Point cell) => IsSurvivorBlockingCell(cell);

    private bool IsPlayerInSurvivorRange(Point target)
    {
        if (_playerCell == target) return true;
        if (_maze is null) return false;
        foreach (var direction in AllDirections)
        {
            if (_maze.CanMove(_playerCell, direction) && _maze.Move(_playerCell, direction) == target)
                return true;
        }
        return false;
    }

    private string SurvivorTelemetryText()
    {
        if (_survivorObjective is not { } objective) return string.Empty;
        if (!IsObjectiveAssignedToLocal(objective.AssignedPlayerId))
            return $"TEAM FILE / {ObjectiveOwnerName(objective.AssignedPlayerId)}";
        return objective.Stage switch
        {
            SurvivorObjectiveStage.Uncontacted => "OPTIONAL SIGNAL / UNREAD",
            SurvivorObjectiveStage.Searching => $"LOCATE {objective.WorkerName.ToUpperInvariant()}",
            SurvivorObjectiveStage.Escorting => $"RETURN {objective.WorkerName.ToUpperInvariant()} / ROOM {objective.RequesterRoomId + 1:00}",
            _ => $"{objective.WorkerName.ToUpperInvariant()} / SAFE"
        };
    }
}
