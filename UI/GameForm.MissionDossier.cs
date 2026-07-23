namespace Dust;

internal sealed partial class GameForm
{
    private bool _missionDossierOpen;
    private DateTime _missionDossierOpenedAt;
    private RectangleF _missionDossierButton;
    private RectangleF _missionDossierFolderRect;
    private RectangleF _missionDossierCloseButton;
    private bool _hoverMissionDossier;
    private bool _hoverMissionDossierClose;

    private TimeSpan CurrentMissionElapsed()
    {
        var now = DateTime.Now;
        var elapsed = now - _startedAt;
        if (_missionDossierOpen && _missionDossierOpenedAt != default)
            elapsed -= now - _missionDossierOpenedAt;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    private void OpenMissionDossier()
    {
        if (_mode != ScreenMode.Playing || _missionDossierOpen) return;
        _missionDossierOpen = true;
        _missionDossierOpenedAt = DateTime.Now;
        ResetHover();
        _audio.Play(AudioCue.Confirm);
    }

    private void CloseMissionDossier(bool playSound = true)
    {
        if (!_missionDossierOpen) return;

        var paused = _missionDossierOpenedAt == default
            ? TimeSpan.Zero
            : DateTime.Now - _missionDossierOpenedAt;
        if (paused > TimeSpan.Zero)
        {
            _startedAt += paused;
            // Hit-window achievements use absolute timestamps. Keep their
            // one-minute windows aligned with the paused mission clock.
            for (var index = 0; index < _runHitTimes.Count; index++)
                _runHitTimes[index] = _runHitTimes[index].Add(paused);
        }

        _missionDossierOpen = false;
        _missionDossierOpenedAt = default;
        _missionDossierCloseButton = RectangleF.Empty;
        _missionDossierFolderRect = RectangleF.Empty;
        ResetHover();
        if (playSound) _audio.Play(AudioCue.Confirm);
    }

    private void ResetMissionDossier()
    {
        _missionDossierOpen = false;
        _missionDossierOpenedAt = default;
        _missionDossierButton = RectangleF.Empty;
        _missionDossierFolderRect = RectangleF.Empty;
        _missionDossierCloseButton = RectangleF.Empty;
        _hoverMissionDossier = false;
        _hoverMissionDossierClose = false;
    }

    private void DrawMissionDossierButton(Graphics g)
    {
        _missionDossierButton = new RectangleF(_mazeRect.X + 15, _mazeRect.Y + 82, 176, 40);
        var rect = _missionDossierButton;
        var hovered = _hoverMissionDossier;
        using var shadow = new SolidBrush(Color.FromArgb(205, Color.Black));
        using var fill = new SolidBrush(hovered ? Color.FromArgb(79, 73, 53) : C.Ink);
        using var edge = new Pen(hovered ? C.Signal : C.Steel, 3);
        g.FillRectangle(shadow, rect.X + 5, rect.Y + 5, rect.Width, rect.Height);
        g.FillRectangle(fill, rect);
        g.DrawRectangle(edge, rect.X, rect.Y, rect.Width, rect.Height);
        LabFont.Draw(g, "DOSSIER  [Q]", rect.X + rect.Width / 2, rect.Y + 13, 2,
            hovered ? C.Signal : C.Bone, LabTextAlign.Center, 0);
    }

    private void DrawMissionDossier(Graphics g)
    {
        if (!_missionDossierOpen) return;

        using var blackout = new SolidBrush(Color.FromArgb(218, 2, 5, 5));
        g.FillRectangle(blackout, _mazeRect);

        _missionDossierFolderRect = new RectangleF(
            _mazeRect.X + 95, _mazeRect.Y + 38, _mazeRect.Width - 190, _mazeRect.Height - 78);
        var folder = _missionDossierFolderRect;
        var sheet = new RectangleF(folder.X + 48, folder.Y + 55, folder.Width - 96, folder.Height - 102);
        _missionDossierCloseButton = new RectangleF(folder.Right - 176, folder.Y + 7, 154, 42);

        using var shadow = new SolidBrush(Color.FromArgb(205, Color.Black));
        using var folderBack = new SolidBrush(Color.FromArgb(137, 119, 76));
        using var folderFront = new SolidBrush(Color.FromArgb(181, 158, 99));
        using var folderEdge = new Pen(Color.FromArgb(79, 68, 45), 3);
        using var pageBehind = new SolidBrush(Color.FromArgb(184, 174, 134));
        using var paper = new SolidBrush(Color.FromArgb(214, 202, 157));
        using var paperRule = new Pen(Color.FromArgb(118, 107, 78), 1);
        using var ink = new SolidBrush(C.Ink);
        using var oldOxide = new SolidBrush(Color.FromArgb(74, 111, 48, 37));

        g.FillRectangle(shadow, folder.X + 14, folder.Y + 18, folder.Width, folder.Height);
        var folderBackShape = new PointF[]
        {
            new(folder.X, folder.Y + 40), new(folder.X + 52, folder.Y + 40),
            new(folder.X + 69, folder.Y), new(folder.X + 326, folder.Y),
            new(folder.X + 353, folder.Y + 40), new(folder.Right, folder.Y + 40),
            new(folder.Right, folder.Bottom), new(folder.X, folder.Bottom)
        };
        g.FillPolygon(folderBack, folderBackShape);
        g.DrawPolygon(folderEdge, folderBackShape);

        // Uneven duplicate sheets and exposed punch holes keep this a physical
        // file assembled by hand, rather than a computer-folder metaphor.
        g.FillRectangle(pageBehind, sheet.X + 13, sheet.Y - 10, sheet.Width - 4, sheet.Height + 4);
        g.FillRectangle(shadow, sheet.X + 8, sheet.Y + 10, sheet.Width, sheet.Height);
        g.FillRectangle(paper, sheet);
        g.DrawRectangle(paperRule, sheet.X, sheet.Y, sheet.Width, sheet.Height);
        for (var y = sheet.Y + 30; y < sheet.Bottom - 18; y += 39)
            g.DrawLine(paperRule, sheet.X + 28, y, sheet.Right - 24, y);
        for (var y = sheet.Y + 31; y < sheet.Bottom - 18; y += 52)
        {
            g.FillRectangle(ink, sheet.X + 9, y, 7, 7);
            g.FillRectangle(folderBack, sheet.X + 11, y + 2, 3, 3);
        }

        g.FillEllipse(oldOxide, sheet.Right - 152, sheet.Bottom - 103, 108, 42);
        g.FillEllipse(oldOxide, sheet.Right - 93, sheet.Bottom - 89, 34, 29);
        g.FillRectangle(oldOxide, sheet.Right - 50, sheet.Y + 112, 7, 62);

        DrawDossierHeader(g, sheet);
        DrawDossierCargoSection(g, new RectangleF(sheet.X + 38, sheet.Y + 92, 530, 286));
        DrawDossierSurvivorSection(g, new RectangleF(sheet.X + 602, sheet.Y + 92,
            sheet.Width - 638, 286));

        using var divider = new Pen(Color.FromArgb(93, 82, 59), 3);
        g.DrawLine(divider, sheet.X + 585, sheet.Y + 82, sheet.X + 585, sheet.Bottom - 78);
        var transferRule = _hasCircuitObjective
            ? "TRANSFER SEALED UNTIL BOTH STORAGE SWITCHES ARE CLOSED"
            : "TRANSFER IS PERMITTED WITH SHORT CARGO / COMPENSATION WILL BE DOCKED";
        LabFont.Draw(g, transferRule, sheet.X + 40, sheet.Bottom - 54, 1, C.Ink, tracking: 0);
        LabFont.Draw(g, "FIELD CLOCK HELD WHILE FILE IS OPEN", sheet.X + 40, sheet.Bottom - 29,
            1, C.Oxide, tracking: 0);

        // Folder pocket, string tie, and steel fastener sit over the paperwork.
        var pocketTop = folder.Bottom - 44;
        var pocket = new PointF[]
        {
            new(folder.X, pocketTop + 11), new(folder.X + folder.Width * .37f, pocketTop),
            new(folder.X + folder.Width * .72f, pocketTop + 8), new(folder.Right, pocketTop - 3),
            new(folder.Right, folder.Bottom), new(folder.X, folder.Bottom)
        };
        g.FillPolygon(folderFront, pocket);
        g.DrawPolygon(folderEdge, pocket);
        using var steel = new SolidBrush(Color.FromArgb(91, 94, 78));
        using var cord = new Pen(C.Oxide, 3);
        g.FillRectangle(steel, folder.X + 25, folder.Y + 24, 53, 9);
        g.FillRectangle(ink, folder.X + 34, folder.Y + 27, 35, 3);
        g.DrawLine(cord, sheet.Right - 51, sheet.Y + 30, sheet.Right - 51, sheet.Y + 78);
        using var washer = new SolidBrush(Color.FromArgb(118, 98, 62));
        g.FillEllipse(washer, sheet.Right - 60, sheet.Y + 21, 18, 18);
        g.FillEllipse(washer, sheet.Right - 60, sheet.Y + 70, 18, 18);
        g.FillEllipse(ink, sheet.Right - 54, sheet.Y + 27, 6, 6);
        g.FillEllipse(ink, sheet.Right - 54, sheet.Y + 76, 6, 6);

        DrawDossierCloseTab(g);
    }

    private void DrawDossierHeader(Graphics g, RectangleF sheet)
    {
        var required = _cargoItems.Count(item => item.Required);
        var secured = _cargoItems.Count(item =>
            item.Required &&
            (item.Carried || item.CarrierPlayerId is not null || item.Delivered));
        var elapsed = CurrentMissionElapsed();
        LabFont.Draw(g, "FIELD RECOVERY DOSSIER", sheet.X + 39, sheet.Y + 23, 2, C.Ink);
        LabFont.Draw(g, $"PLATE {_level:00} / ELAPSED {(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}",
            sheet.X + 41, sheet.Y + 56, 1, Color.FromArgb(68, 69, 57));
        var assignmentStatus = _hasCircuitObjective
            ? $"SW {ActivatedCircuitSwitches:00}/{RequiredCircuitSwitches:00}  CARGO {secured:00}/{required:00}"
            : $"CARGO {secured:00}/{required:00}";
        var assignmentComplete = secured == required && CircuitObjectiveComplete;
        LabFont.Draw(g, assignmentStatus, sheet.Right - 94, sheet.Y + 55,
            1, assignmentComplete ? Color.FromArgb(48, 78, 62) : C.Oxide,
            LabTextAlign.Right);

        var stamp = new RectangleF(sheet.X + 443, sheet.Y + 14, 154, 49);
        using var stampPen = new Pen(Color.FromArgb(130, C.Oxide), 4);
        g.DrawRectangle(stampPen, stamp.X, stamp.Y, stamp.Width, stamp.Height);
        LabFont.Draw(g, "ACTIVE", stamp.X + stamp.Width / 2, stamp.Y + 15, 2,
            Color.FromArgb(142, C.Oxide), LabTextAlign.Center, 0);
    }

    private void DrawDossierCargoSection(Graphics g, RectangleF area)
    {
        var required = _cargoItems.Where(item => item.Required).ToList();
        if (!_hasCircuitObjective)
        {
            LabFont.Draw(g, "A / MANIFESTED MATERIAL", area.X, area.Y, 1, C.Oxide);
            for (var index = 0; index < required.Count; index++)
                DrawDossierCargoRow(g, required[index], new RectangleF(
                    area.X, area.Y + 28 + index * 61, area.Width, 53), index + 1);
            return;
        }

        LabFont.Draw(g, "A / MANDATORY STORAGE CIRCUIT", area.X, area.Y, 1, C.Oxide);
        for (var index = 0; index < _circuitSwitches.Count; index++)
            DrawDossierCircuitRow(g, _circuitSwitches[index], new RectangleF(
                area.X, area.Y + 27 + index * 49, area.Width, 43));

        if (required.Count == 0)
        {
            LabFont.Draw(g, "CARGO REQUIREMENT REPLACED BY CIRCUIT ORDER", area.X + 5,
                area.Y + 142, 1, Color.FromArgb(72, 70, 53), tracking: 0);
            return;
        }

        var cargoTop = area.Y + 137;
        LabFont.Draw(g, "A2 / MANIFESTED MATERIAL", area.X, cargoTop, 1, C.Oxide);
        for (var index = 0; index < required.Count; index++)
            DrawDossierCargoRow(g, required[index], new RectangleF(
                area.X, cargoTop + 27 + index * 58, area.Width, 51), index + 1);
    }

    private void DrawDossierCircuitRow(Graphics g, CircuitSwitch circuitSwitch, RectangleF row)
    {
        var active = circuitSwitch.Activated;
        using var wash = new SolidBrush(Color.FromArgb(active ? 30 : 18,
            active ? Color.FromArgb(42, 75, 57) : C.Oxide));
        using var edge = new Pen(Color.FromArgb(92, 79, 56), 2);
        g.FillRectangle(wash, row);
        g.DrawLine(edge, row.X, row.Bottom, row.Right, row.Bottom);

        var check = new RectangleF(row.X + 5, row.Y + 7, 27, 27);
        g.DrawRectangle(edge, check.X, check.Y, check.Width, check.Height);
        if (active)
        {
            using var mark = new Pen(Color.FromArgb(54, 83, 61), 4);
            g.DrawLine(mark, check.X + 5, check.Y + 14, check.X + 11, check.Bottom - 5);
            g.DrawLine(mark, check.X + 11, check.Bottom - 5, check.Right - 4, check.Y + 5);
        }
        else
            LabFont.Draw(g, circuitSwitch.Number.ToString(), check.X + 13, check.Y + 8, 1,
                C.Oxide, LabTextAlign.Center, 0);

        LabFont.Draw(g, $"SWITCH {circuitSwitch.Number:00}", row.X + 44, row.Y + 8, 2,
            C.Ink, tracking: 0);
        var location = active
            ? "CIRCUIT CLOSED"
            : _revealedRoomIds.Contains(circuitSwitch.RoomId)
                ? $"ROOM {circuitSwitch.RoomId + 1:00} / OPTICS OPEN"
                : "SEARCH STORAGE ROOMS";
        LabFont.Draw(g, location, row.Right - 4, row.Y + 14, 1,
            active ? Color.FromArgb(48, 78, 62) : C.Oxide, LabTextAlign.Right, 0);
    }

    private void DrawDossierCargoRow(Graphics g, CargoItem item, RectangleF row, int number)
    {
        var secured = item.Carried || item.CarrierPlayerId is not null || item.Delivered;
        using var wash = new SolidBrush(Color.FromArgb(secured ? 30 : 18,
            secured ? Color.FromArgb(42, 75, 57) : C.Oxide));
        using var edge = new Pen(Color.FromArgb(92, 79, 56), 2);
        g.FillRectangle(wash, row);
        g.DrawLine(edge, row.X, row.Bottom, row.Right, row.Bottom);

        var check = new RectangleF(row.X + 5, row.Y + 8, 28, 28);
        g.DrawRectangle(edge, check.X, check.Y, check.Width, check.Height);
        if (secured)
        {
            using var mark = new Pen(Color.FromArgb(54, 83, 61), 4);
            g.DrawLine(mark, check.X + 5, check.Y + 15, check.X + 12, check.Bottom - 5);
            g.DrawLine(mark, check.X + 12, check.Bottom - 5, check.Right - 4, check.Y + 5);
        }
        else
            LabFont.Draw(g, number.ToString(), check.X + 14, check.Y + 9, 1, C.Oxide,
                LabTextAlign.Center, 0);

        LabFont.Draw(g, item.Code, row.X + 45, row.Y + 5, 2, C.Ink, tracking: 0);
        LabFont.Draw(g, CargoName(item.Kind), row.X + 210, row.Y + 9, 1,
            Color.FromArgb(62, 66, 54), tracking: 0);
        var location = item.Delivered
            ? "TRANSFERRED"
            : item.CarrierPlayerId is { } carrierId
                ? $"LATCHED / {OnlineCarrierName(carrierId)}"
            : item.Carried ? "LATCHED BENEATH UNIT"
            : _revealedRoomIds.Contains(item.RoomId)
                ? $"ROOM {item.RoomId + 1:00} / OPTICS OPEN"
                : "SEARCH STORAGE SECTORS";
        LabFont.Draw(g, location, row.Right - 4, row.Y + 36, 1,
            secured ? Color.FromArgb(48, 78, 62) : C.Oxide, LabTextAlign.Right, 0);
    }

    private string OnlineCarrierName(string playerId)
    {
        if (playerId == _onlinePlayerId)
            return (_onlineUsername ?? "YOUR UNIT").ToUpperInvariant();
        return _onlinePlayers.TryGetValue(playerId, out var player)
            ? player.Username.ToUpperInvariant()
            : "TEAM UNIT";
    }

    private void DrawDossierSurvivorSection(Graphics g, RectangleF area)
    {
        LabFont.Draw(g, "B / PERSONNEL RECOVERY", area.X, area.Y, 1, C.Oxide);
        using var edge = new Pen(Color.FromArgb(92, 79, 56), 2);
        g.DrawRectangle(edge, area.X, area.Y + 28, area.Width, 228);

        if (_survivorObjective is not { } survivor)
        {
            LabFont.Draw(g, "NO SUPPLEMENTAL", area.X + area.Width / 2, area.Y + 92,
                1, C.Steel, LabTextAlign.Center);
            LabFont.Draw(g, "PERSONNEL DIRECTIVE", area.X + area.Width / 2, area.Y + 120,
                1, C.Steel, LabTextAlign.Center);
            return;
        }

        var resolved = survivor.Stage == SurvivorObjectiveStage.Rescued;
        var name = survivor.Stage == SurvivorObjectiveStage.Uncontacted
            ? "IDENTITY SEALED"
            : survivor.WorkerName.ToUpperInvariant();
        var status = survivor.Stage switch
        {
            SurvivorObjectiveStage.Uncontacted => "DISTRESS FILE / UNREAD",
            SurvivorObjectiveStage.Searching => "WORKER MISSING",
            SurvivorObjectiveStage.Escorting => "WORKER IN TOW",
            _ => "WORKER RETURNED"
        };
        var directive = survivor.Stage switch
        {
            SurvivorObjectiveStage.Uncontacted => "ENTER STORAGE ROOMS",
            SurvivorObjectiveStage.Searching => "FOLLOW LAST OUTSIDE SIGNAL",
            SurvivorObjectiveStage.Escorting => $"RETURN TO ROOM {survivor.RequesterRoomId + 1:00}",
            _ => $"ROOM {survivor.RequesterRoomId + 1:00} / SAFE"
        };
        var caution = survivor.Stage switch
        {
            SurvivorObjectiveStage.Uncontacted => "OPTIONAL CONTRACT",
            SurvivorObjectiveStage.Searching => $"REQUEST FILED / ROOM {survivor.RequesterRoomId + 1:00}",
            SurvivorObjectiveStage.Escorting => "DO NOT BREAK CONTACT",
            _ => "CONTRACT COMPLETE"
        };
        var statusColor = resolved ? Color.FromArgb(48, 78, 62) :
            survivor.Stage == SurvivorObjectiveStage.Escorting ? C.Signal : C.Oxide;

        DrawDossierPeopleMark(g, new PointF(area.X + 39, area.Y + 77), statusColor, resolved);
        LabFont.Draw(g, name, area.X + 74, area.Y + 49, 2, C.Ink, tracking: 0);
        LabFont.Draw(g, status, area.X + 22, area.Y + 117, 1, statusColor, tracking: 0);
        LabFont.Draw(g, directive, area.X + 22, area.Y + 151, 1, C.Ink, tracking: 0);
        LabFont.Draw(g, caution, area.X + 22, area.Y + 186, 1,
            resolved ? statusColor : Color.FromArgb(80, 73, 54), tracking: 0);

        var stamp = new RectangleF(area.Right - 135, area.Bottom - 69, 115, 40);
        using var stampPen = new Pen(Color.FromArgb(145, statusColor), 3);
        g.DrawRectangle(stampPen, stamp.X, stamp.Y, stamp.Width, stamp.Height);
        LabFont.Draw(g, resolved ? "SAFE" : "OPEN", stamp.X + stamp.Width / 2,
            stamp.Y + 12, 2, Color.FromArgb(150, statusColor), LabTextAlign.Center, 0);
    }

    private static void DrawDossierPeopleMark(Graphics g, PointF center, Color color, bool paired)
    {
        using var mark = new SolidBrush(color);
        void Person(float x, float y)
        {
            g.FillRectangle(mark, x - 5, y - 18, 10, 10);
            g.FillRectangle(mark, x - 7, y - 5, 14, 20);
            g.FillRectangle(mark, x - 11, y - 1, 4, 16);
            g.FillRectangle(mark, x + 7, y - 1, 4, 16);
            g.FillRectangle(mark, x - 6, y + 15, 5, 12);
            g.FillRectangle(mark, x + 1, y + 15, 5, 12);
        }
        Person(center.X - (paired ? 10 : 0), center.Y);
        if (paired) Person(center.X + 10, center.Y + 2);
    }

    private void DrawDossierCloseTab(Graphics g)
    {
        var rect = _missionDossierCloseButton;
        var hovered = _hoverMissionDossierClose;
        using var shadow = new SolidBrush(Color.FromArgb(170, Color.Black));
        using var tab = new SolidBrush(hovered ? Color.FromArgb(204, 181, 118) : C.Oxide);
        using var edge = new Pen(hovered ? C.Signal : Color.FromArgb(81, 50, 39), 3);
        g.FillRectangle(shadow, rect.X + 6, rect.Y + 7, rect.Width, rect.Height);
        g.FillPolygon(tab, CutPanelPoints(rect, 8));
        g.DrawPolygon(edge, CutPanelPoints(rect, 8));
        LabFont.Draw(g, "Q / CLOSE", rect.X + rect.Width / 2, rect.Y + 13, 2,
            hovered ? C.Ink : C.Bone, LabTextAlign.Center, 0);
    }
}
