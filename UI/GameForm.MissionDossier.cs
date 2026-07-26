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
        if (!IsOnlineGameplayActive && _missionDossierOpen &&
            _missionDossierOpenedAt != default)
            elapsed -= now - _missionDossierOpenedAt;
        if (!IsOnlineGameplayActive && _inventoryOpen &&
            _inventoryOpenedAt != default)
            elapsed -= now - _inventoryOpenedAt;
        if (OfflinePauseFreezesGame && _offlinePauseOpenedAt != default)
            elapsed -= now - _offlinePauseOpenedAt;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    private void OpenMissionDossier(bool playSound = true)
    {
        if (_mode != ScreenMode.Playing || _missionDossierOpen) return;
        CloseInventory(playSound: false);
        _missionDossierOpen = true;
        _missionDossierOpenedAt = DateTime.Now;
        ResetHover();
        if (playSound) _audio.Play(AudioCue.Confirm);
    }

    private void CloseMissionDossier(bool playSound = true)
    {
        if (!_missionDossierOpen) return;

        var paused = _missionDossierOpenedAt == default
            ? TimeSpan.Zero
            : DateTime.Now - _missionDossierOpenedAt;
        if (!IsOnlineGameplayActive && paused > TimeSpan.Zero)
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
        // A physical evidence-folder glyph keeps the control compact and
        // language-free. It lives directly below the telemetry plate instead
        // of reading like another HUD panel.
        _missionDossierButton = new RectangleF(_mazeRect.X + 15, _mazeRect.Y + 81, 48, 48);
        var rect = _missionDossierButton;
        var hovered = _hoverMissionDossier;
        using var shadow = new SolidBrush(Color.FromArgb(205, Color.Black));
        g.FillPolygon(shadow, CutPanelPoints(
            new RectangleF(rect.X + 5, rect.Y + 5, rect.Width, rect.Height), 6));
        DrawCutPanel(g, rect,
            hovered ? Color.FromArgb(69, 62, 44) : Color.FromArgb(224, C.Ink),
            hovered ? C.Signal : C.Steel, 6, hovered ? 3 : 2);

        // Two small mounting marks make the icon feel like a dedicated
        // hardware key without increasing its clickable footprint.
        using var mount = new SolidBrush(hovered ? C.Bone : C.Oxide);
        g.FillRectangle(mount, rect.X + 5, rect.Y + 8, 3, 3);
        g.FillRectangle(mount, rect.Right - 8, rect.Bottom - 11, 3, 3);

        DrawMissionDossierIcon(g, rect, hovered);
    }

    private static void DrawMissionDossierIcon(Graphics g, RectangleF button, bool active)
    {
        var x = button.X + 9;
        var y = button.Y + 7;
        var accent = active ? C.Signal : Color.FromArgb(181, 158, 99);

        // A loose report page protrudes from a worn, tabbed field folder.
        using var pageShadow = new SolidBrush(Color.FromArgb(180, Color.Black));
        using var page = new SolidBrush(active ? C.Bone : Color.FromArgb(194, 187, 143));
        using var pageInk = new SolidBrush(active ? C.Oxide : C.Steel);
        g.FillRectangle(pageShadow, x + 10, y + 2, 19, 25);
        g.FillRectangle(page, x + 8, y, 19, 25);
        g.FillRectangle(pageInk, x + 11, y + 6, 12, 2);
        g.FillRectangle(pageInk, x + 11, y + 11, 9, 2);
        g.FillRectangle(pageInk, x + 11, y + 16, 12, 2);

        using var folderBack = new SolidBrush(Color.FromArgb(112, 94, 58));
        using var folderFront = new SolidBrush(accent);
        using var folderEdge = new Pen(active ? C.Bone : Color.FromArgb(79, 68, 45), 2)
        {
            LineJoin = System.Drawing.Drawing2D.LineJoin.Miter
        };
        var back = new PointF[]
        {
            new(x + 2, y + 12), new(x + 11, y + 12), new(x + 14, y + 8),
            new(x + 23, y + 8), new(x + 26, y + 12), new(x + 31, y + 12),
            new(x + 31, y + 31), new(x + 2, y + 31)
        };
        g.FillPolygon(folderBack, back);
        var front = new PointF[]
        {
            new(x, y + 17), new(x + 13, y + 17), new(x + 16, y + 14),
            new(x + 33, y + 14), new(x + 30, y + 32), new(x + 3, y + 32)
        };
        g.FillPolygon(folderFront, front);
        g.DrawPolygon(folderEdge, front);

        // A dark thumb notch is enough to read at native half-resolution.
        using var notch = new SolidBrush(Color.FromArgb(155, C.Ink));
        g.FillRectangle(notch, x + 13, y + 19, 8, 3);

        if (!active) return;
        using var focus = new SolidBrush(C.Signal);
        g.FillRectangle(focus, button.X + 2, button.Y + 15, 3, 10);
        g.FillRectangle(focus, button.Right - 5, button.Y + 23, 3, 10);
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
        var rightColumn = new RectangleF(sheet.X + 602, sheet.Y + 92,
            sheet.Width - 638, 286);
        DrawDossierDirectiveSection(g, new RectangleF(
            rightColumn.X, rightColumn.Y, rightColumn.Width, 190));
        DrawDossierSurvivorCompact(g, new RectangleF(
            rightColumn.X, rightColumn.Y + 194, rightColumn.Width, 92));

        using var divider = new Pen(Color.FromArgb(93, 82, 59), 3);
        g.DrawLine(divider, sheet.X + 585, sheet.Y + 82, sheet.X + 585, sheet.Bottom - 78);
        var transferRule = _hasCircuitObjective
            ? "TRANSFER SEALED UNTIL BOTH STORAGE SWITCHES ARE CLOSED"
            : "TRANSFER IS PERMITTED WITH SHORT CARGO / COMPENSATION WILL BE DOCKED";
        LabFont.Draw(g, transferRule, sheet.X + 40, sheet.Bottom - 54, 1, C.Ink, tracking: 0);
        LabFont.Draw(g,
            IsOnlineGameplayActive
                ? "NETWORK LIVE / FIELD CLOCK CONTINUES"
                : "FIELD CLOCK HELD WHILE FILE IS OPEN",
            sheet.X + 40, sheet.Bottom - 29, 1, C.Oxide, tracking: 0);

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
        var localCargo = LocalRequiredCargoItems.ToList();
        var localSwitches = LocalCircuitSwitches.ToList();
        var localDirectives = LocalFieldDirectives.ToList();
        var required = localCargo.Count;
        var secured = localCargo.Count(item =>
            (item.Carried || item.CarrierPlayerId is not null || item.Delivered));
        var elapsed = CurrentMissionElapsed();
        LabFont.Draw(g, "FIELD RECOVERY DOSSIER", sheet.X + 39, sheet.Y + 23, 2, C.Ink);
        var displayedCallsign = (_onlineUsername ?? "DRONE").ToUpperInvariant();
        if (displayedCallsign.Length > 8)
            displayedCallsign = $"{displayedCallsign[..6]}..";
        var callsign = IsOnlineGameplayActive
            ? $" / UNIT {displayedCallsign}"
            : string.Empty;
        LabFont.Draw(g, $"PLATE {_level:00}{callsign} / {(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}",
            sheet.X + 41, sheet.Y + 56, 1, Color.FromArgb(68, 69, 57));
        var assignmentStatus =
            $"ORD {localDirectives.Count(item => item.IsComplete):00}/{localDirectives.Count:00}  " +
            $"SW {localSwitches.Count(item => item.Activated):00}/{localSwitches.Count:00}  " +
            $"CG {secured:00}/{required:00}";
        var assignmentComplete = secured == required &&
                                 localSwitches.All(item => item.Activated) &&
                                 localDirectives.All(item => item.IsComplete);
        LabFont.Draw(g, assignmentStatus, sheet.Right - 94, sheet.Y + 71,
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
        var required = LocalRequiredCargoItems.ToList();
        var localSwitches = LocalCircuitSwitches.ToList();
        if (localSwitches.Count == 0)
        {
            LabFont.Draw(g, "A / ASSIGNED MANIFEST", area.X, area.Y, 1, C.Oxide);
            if (required.Count == 0)
            {
                LabFont.Draw(g, "NO MATERIAL OR CIRCUIT ORDER / SEE FIELD CONTRACTS",
                    area.X + 5, area.Y + 47, 1, Color.FromArgb(72, 70, 53), tracking: 0);
                return;
            }
            for (var index = 0; index < required.Count; index++)
                DrawDossierCargoRow(g, required[index], new RectangleF(
                    area.X, area.Y + 28 + index * 61, area.Width, 53), index + 1);
            return;
        }

        LabFont.Draw(g, "A / YOUR MANDATORY CIRCUIT ORDER", area.X, area.Y, 1, C.Oxide);
        for (var index = 0; index < localSwitches.Count; index++)
            DrawDossierCircuitRow(g, localSwitches[index], new RectangleF(
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

    private void DrawDossierDirectiveSection(Graphics g, RectangleF area)
    {
        var groups = LocalFieldDirectives
            .GroupBy(item => item.Kind)
            .OrderBy(group => group.Key)
            .ToList();
        LabFont.Draw(g, "B / PERSONAL FIELD CONTRACTS", area.X, area.Y, 1, C.Oxide);
        if (groups.Count == 0)
        {
            using var emptyEdge = new Pen(Color.FromArgb(92, 79, 56), 2);
            g.DrawRectangle(emptyEdge, area.X, area.Y + 27, area.Width, 57);
            LabFont.Draw(g, "NO FIELD CONTRACTS", area.X + area.Width / 2,
                area.Y + 47, 1, C.Steel, LabTextAlign.Center);
            return;
        }

        // Reassigned orders can leave one survivor carrying several contracts.
        // Grouping them by procedure keeps the physical file legible without
        // hiding any progress or shrinking rows below the pixel font.
        var rowHeight = Math.Min(40, (int)((area.Height - 27) / groups.Count));
        for (var index = 0; index < groups.Count; index++)
            DrawDossierDirectiveGroupRow(g, groups[index].ToList(), new RectangleF(
                area.X, area.Y + 27 + index * rowHeight, area.Width, rowHeight));
    }

    private void DrawDossierDirectiveGroupRow(
        Graphics g,
        IReadOnlyList<FieldDirective> directives,
        RectangleF row)
    {
        var complete = directives.All(directive => directive.IsComplete);
        var color = complete ? Color.FromArgb(48, 78, 62) : C.Oxide;
        using var wash = new SolidBrush(Color.FromArgb(complete ? 30 : 18, color));
        using var edge = new Pen(Color.FromArgb(92, 79, 56), 2);
        g.FillRectangle(wash, row);
        g.DrawLine(edge, row.X, row.Bottom, row.Right, row.Bottom);

        var check = new RectangleF(row.X + 4, row.Y + 6, 24, 24);
        g.DrawRectangle(edge, check.X, check.Y, check.Width, check.Height);
        if (complete)
        {
            using var mark = new Pen(color, 4);
            g.DrawLine(mark, check.X + 4, check.Y + 12, check.X + 10, check.Bottom - 4);
            g.DrawLine(mark, check.X + 10, check.Bottom - 4, check.Right - 3, check.Y + 4);
        }
        else
            LabFont.Draw(g, directives.Count.ToString(), check.X + 12,
                check.Y + 7, 1, color, LabTextAlign.Center, 0);

        var kind = directives[0].Kind;
        var shortName = kind switch
        {
            FieldDirectiveKind.ArchiveDecrypt => "ARCHIVE DECRYPT",
            FieldDirectiveKind.PressurePurge => "PRESSURE PURGE",
            FieldDirectiveKind.SignalCalibrate => "SIGNAL CAL.",
            _ => "SPECIMEN SEAL"
        };
        LabFont.Draw(g, shortName, row.X + 37, row.Y + 3,
            1, C.Ink, tracking: 0);
        var activated = directives.Sum(directive => directive.ActivatedCount);
        var nodes = directives.Sum(directive => directive.Nodes.Count);
        var nextNode = directives
            .SelectMany(directive => directive.Nodes.Select((node, index) =>
                (directive, node, index)))
            .FirstOrDefault(entry => !entry.directive.IsNodeActive(entry.index));
        var location = complete
            ? "CLOSED"
            : nextNode.node is not null && _revealedRoomIds.Contains(nextNode.node.RoomId)
                ? $"RM {nextNode.node.RoomId + 1:00}"
                : "SEARCH";
        var orderLabel = directives.Count == 1 ? "ORDER" : "ORD";
        LabFont.Draw(g, $"{directives.Count} {orderLabel} / {activated:00}/{nodes:00} / {location}",
            row.X + 37, row.Y + 20, 1, color, tracking: 0);
    }

    private void DrawDossierSurvivorCompact(Graphics g, RectangleF area)
    {
        LabFont.Draw(g, "C / PERSONNEL", area.X, area.Y, 1, C.Oxide);
        using var edge = new Pen(Color.FromArgb(92, 79, 56), 2);
        g.DrawRectangle(edge, area.X, area.Y + 25, area.Width, area.Height - 25);
        if (_survivorObjective is not { } survivor || !IsLocalSurvivorObjective)
        {
            LabFont.Draw(g, IsOnlineGameplayActive ? "NO PERSONAL RECOVERY FILE" : "NO SUPPLEMENTAL FILE",
                area.X + area.Width / 2, area.Y + 47, 1, C.Steel,
                LabTextAlign.Center, 0);
            return;
        }

        var complete = survivor.IsResolved;
        var statusColor = complete ? Color.FromArgb(48, 78, 62) :
            survivor.Stage == SurvivorObjectiveStage.Escorting ? C.Signal : C.Oxide;
        var name = survivor.Stage == SurvivorObjectiveStage.Uncontacted
            ? "IDENTITY SEALED"
            : survivor.WorkerName.ToUpperInvariant();
        var status = survivor.Stage switch
        {
            SurvivorObjectiveStage.Uncontacted => "ANSWER DISTRESS FILE",
            SurvivorObjectiveStage.Searching => "MISSING / FIND WORKER",
            SurvivorObjectiveStage.Escorting => $"RETURN / ROOM {survivor.RequesterRoomId + 1:00}",
            _ => "RETURNED / SAFE"
        };
        LabFont.Draw(g, name, area.X + 12, area.Y + 36, 1, C.Ink, tracking: 0);
        LabFont.Draw(g, status, area.X + 12, area.Y + 59, 1, statusColor, tracking: 0);
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
        LabFont.Draw(g, "CLOSE", rect.X + rect.Width / 2, rect.Y + 13, 2,
            hovered ? C.Ink : C.Bone, LabTextAlign.Center, 0);
    }
}
