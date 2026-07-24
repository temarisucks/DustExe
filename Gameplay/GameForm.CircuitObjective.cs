namespace Dust;

internal sealed partial class GameForm
{
    private const int RequiredCircuitSwitches = 2;
    private readonly List<CircuitSwitch> _circuitSwitches = [];
    private bool _hasCircuitObjective;
    private int _circuitPay;

    private int ActivatedCircuitSwitches => _circuitSwitches.Count(item => item.Activated);
    private bool CircuitObjectiveComplete => !_hasCircuitObjective ||
                                             _circuitSwitches.Count == RequiredCircuitSwitches &&
                                             ActivatedCircuitSwitches == RequiredCircuitSwitches;

    private bool TrySetupCircuitObjective(IReadOnlyList<CargoRoom> rooms)
    {
        _circuitSwitches.Clear();
        _hasCircuitObjective = false;
        if (rooms.Count < RequiredCircuitSwitches || _random.Next(2) != 0) return false;

        var occupied = _cargoItems.Select(item => item.Cell).ToHashSet();
        foreach (var room in rooms.OrderBy(_ => _random.Next()))
        {
            var candidates = room.Cells
                .Where(cell => cell != room.DoorCell && !occupied.Contains(cell) &&
                               Manhattan(cell, room.DoorCell) > 1 && IsRoomPerimeterCell(room, cell))
                .OrderByDescending(cell => Manhattan(cell, room.DoorCell))
                .ThenBy(_ => _random.Next())
                .ToList();
            if (candidates.Count == 0) continue;

            var cell = candidates[0];
            _circuitSwitches.Add(new CircuitSwitch
            {
                Number = _circuitSwitches.Count + 1,
                RoomId = room.Id,
                Cell = cell,
                Phase = (float)_random.NextDouble() * MathF.PI * 2
            });
            occupied.Add(cell);
            if (_circuitSwitches.Count == RequiredCircuitSwitches) break;
        }

        if (_circuitSwitches.Count != RequiredCircuitSwitches)
        {
            _circuitSwitches.Clear();
            return false;
        }

        _hasCircuitObjective = true;
        return true;
    }

    private CircuitSwitch? FindCircuitSwitchInRange() => !_hasCircuitObjective || _maze is null
        ? null
        : _circuitSwitches
            .Where(item => !item.Activated && CanInteractWithMissionCell(item.Cell))
            .OrderByDescending(item => IsObjectiveAssignedToLocal(item.AssignedPlayerId))
            .ThenBy(item => Manhattan(_playerCell, item.Cell))
            .ThenBy(item => item.Number)
            .FirstOrDefault();

    private bool CanInteractWithMissionCell(Point target)
    {
        if (_playerCell == target) return true;
        if (_maze is null) return false;
        foreach (var direction in AllDirections)
            if (_maze.CanMove(_playerCell, direction) && _maze.Move(_playerCell, direction) == target)
                return true;
        return false;
    }

    private bool TryActivateCircuitSwitch()
    {
        if (_mode != ScreenMode.Playing || _moveProgress < 1 || _hitEffect > 0) return false;
        var circuitSwitch = FindCircuitSwitchInRange();
        if (circuitSwitch is null) return false;
        if (!IsObjectiveAssignedToLocal(circuitSwitch.AssignedPlayerId))
            return false;

        circuitSwitch.Activated = true;
        var active = ActivatedCircuitSwitches;
        _missionNotice = active == RequiredCircuitSwitches
            ? "STORAGE CIRCUIT RESTORED / TRANSFER RELEASED"
            : $"SWITCH {circuitSwitch.Number:00} CLOSED / CIRCUIT {active:00}/{RequiredCircuitSwitches:00}";
        _missionNoticeTimer = active == RequiredCircuitSwitches ? 3.8f : 2.8f;
        _audio.Play(AudioCue.Confirm);
        return true;
    }

    private string? CircuitSwitchPrompt()
    {
        var circuitSwitch = FindCircuitSwitchInRange();
        return circuitSwitch is null
            ? null
            : IsObjectiveAssignedToLocal(circuitSwitch.AssignedPlayerId)
                ? $"E / FLIP STORAGE SWITCH {circuitSwitch.Number:00} / MANDATORY"
                : null;
    }

    private void NotifyCircuitTransferLock()
    {
        _missionNotice = $"TRANSFER LOCKED / STORAGE CIRCUIT {ActivatedCircuitSwitches:00}/{RequiredCircuitSwitches:00}";
        _missionNoticeTimer = 3.2f;
        _audio.Play(AudioCue.Select);
    }

    private bool IsCircuitSwitchCell(Point cell) =>
        _circuitSwitches.Any(circuitSwitch => circuitSwitch.Cell == cell);
}
