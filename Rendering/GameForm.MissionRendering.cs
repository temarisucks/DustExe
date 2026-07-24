using System.Drawing.Drawing2D;

namespace Dust;

internal sealed partial class GameForm
{
    private void DrawRoomShrouds(Graphics g)
    {
        if (_maze is null) return;
        using var shroud = new SolidBrush(Color.FromArgb(248, 3, 7, 7));
        using var grain = new SolidBrush(Color.FromArgb(58, C.Steel));
        foreach (var room in _maze.Rooms)
        {
            if (_revealedRoomIds.Contains(room.Id)) continue;
            using var path = new GraphicsPath(FillMode.Winding);
            foreach (var cell in room.Cells)
            {
                var center = CellCenter(cell);
                path.AddRectangle(new RectangleF(center.X - _cellSize / 2 - 1,
                    center.Y - _cellSize / 2 - 1, _cellSize + 2, _cellSize + 2));
            }
            g.FillPath(shroud, path);

            var boundsTopLeft = CellCenter(new PointF(room.Bounds.Left, room.Bounds.Top));
            var boundsBottomRight = CellCenter(new PointF(room.Bounds.Right - 1, room.Bounds.Bottom - 1));
            var left = boundsTopLeft.X - _cellSize / 2;
            var top = boundsTopLeft.Y - _cellSize / 2;
            var right = boundsBottomRight.X + _cellSize / 2;
            var bottom = boundsBottomRight.Y + _cellSize / 2;
            var clipState = g.Save();
            g.SetClip(path, CombineMode.Intersect);
            for (var y = top + 14; y < bottom; y += 18)
                g.FillRectangle(grain, left + 12, y, Math.Max(0, right - left - 24), 2);
            LabFont.Draw(g, $"ROOM {room.Id + 1:00} / OPTICS CLOSED", (left + right) / 2,
                (top + bottom) / 2 - 7, 1, C.Steel, LabTextAlign.Center);
            g.Restore(clipState);
        }
    }

    private void DrawRoomDoorSignals(Graphics g)
    {
        if (_maze is null) return;
        foreach (var room in _maze.Rooms)
        {
            var inside = CellCenter(room.DoorCell);
            var outside = CellCenter(room.DoorApproachCell);
            var center = new PointF((inside.X + outside.X) / 2, (inside.Y + outside.Y) / 2);
            // A horizontal transition crosses a vertical wall, so its two leaves
            // retract up and down. The opposite transition retracts left/right.
            var crossesHorizontal = room.DoorCell.X != room.DoorApproachCell.X;
            var revealed = _revealedRoomIds.Contains(room.Id);
            var open = RoomDoorOpenProgress(room.Id);
            var span = Math.Min(62f, _cellSize * .76f);
            var thickness = Math.Max(14f, _cellSize * .17f);
            var leafSpan = span * .5f * (1 - open);
            using var aperture = new SolidBrush(Color.FromArgb(4, 7, 7));
            using var frame = new SolidBrush(Color.FromArgb(77, 85, 71));
            using var frameLight = new SolidBrush(Color.FromArgb(126, 128, 99));
            using var leaf = new SolidBrush(revealed ? Color.FromArgb(84, 91, 72) : Color.FromArgb(75, 67, 54));
            using var leafEdge = new Pen(revealed ? C.Signal : C.Oxide, 3);
            using var seam = new SolidBrush(Color.FromArgb(11, 17, 16));
            using var lamp = new SolidBrush(open >= .98f
                ? C.Signal
                : revealed ? Color.FromArgb(116, 84, 49) : C.Oxide);

            if (crossesHorizontal)
            {
                var throat = new RectangleF(center.X - thickness / 2, center.Y - span / 2,
                    thickness, span);
                g.FillRectangle(aperture, throat);
                g.FillRectangle(frame, center.X - thickness / 2 - 5, center.Y - span / 2 - 6,
                    thickness + 10, 7);
                g.FillRectangle(frame, center.X - thickness / 2 - 5, center.Y + span / 2 - 1,
                    thickness + 10, 7);
                g.FillRectangle(frameLight, center.X - thickness / 2 - 5, center.Y - span / 2 - 6,
                    4, 13);
                g.FillRectangle(frameLight, center.X + thickness / 2 + 1, center.Y + span / 2 - 7,
                    4, 13);

                if (leafSpan > .5f)
                {
                    var upper = new RectangleF(center.X - thickness / 2 + 2, center.Y - span / 2,
                        thickness - 4, leafSpan);
                    var lower = new RectangleF(center.X - thickness / 2 + 2, center.Y + span / 2 - leafSpan,
                        thickness - 4, leafSpan);
                    g.FillRectangle(leaf, upper);
                    g.FillRectangle(leaf, lower);
                    g.DrawRectangle(leafEdge, upper.X, upper.Y, upper.Width, upper.Height);
                    g.DrawRectangle(leafEdge, lower.X, lower.Y, lower.Width, lower.Height);
                    g.FillRectangle(seam, upper.X + 3, upper.Bottom - 4, upper.Width - 6, 3);
                    g.FillRectangle(seam, lower.X + 3, lower.Y + 1, lower.Width - 6, 3);
                }
                g.FillRectangle(lamp, center.X - 2, center.Y - span / 2 - 13, 5, 5);
            }
            else
            {
                var throat = new RectangleF(center.X - span / 2, center.Y - thickness / 2,
                    span, thickness);
                g.FillRectangle(aperture, throat);
                g.FillRectangle(frame, center.X - span / 2 - 6, center.Y - thickness / 2 - 5,
                    7, thickness + 10);
                g.FillRectangle(frame, center.X + span / 2 - 1, center.Y - thickness / 2 - 5,
                    7, thickness + 10);
                g.FillRectangle(frameLight, center.X - span / 2 - 6, center.Y - thickness / 2 - 5,
                    13, 4);
                g.FillRectangle(frameLight, center.X + span / 2 - 7, center.Y + thickness / 2 + 1,
                    13, 4);

                if (leafSpan > .5f)
                {
                    var left = new RectangleF(center.X - span / 2, center.Y - thickness / 2 + 2,
                        leafSpan, thickness - 4);
                    var right = new RectangleF(center.X + span / 2 - leafSpan, center.Y - thickness / 2 + 2,
                        leafSpan, thickness - 4);
                    g.FillRectangle(leaf, left);
                    g.FillRectangle(leaf, right);
                    g.DrawRectangle(leafEdge, left.X, left.Y, left.Width, left.Height);
                    g.DrawRectangle(leafEdge, right.X, right.Y, right.Width, right.Height);
                    g.FillRectangle(seam, left.Right - 4, left.Y + 3, 3, left.Height - 6);
                    g.FillRectangle(seam, right.X + 1, right.Y + 3, 3, right.Height - 6);
                }
                g.FillRectangle(lamp, center.X - span / 2 - 13, center.Y - 2, 5, 5);
            }
            if (!revealed)
                LabFont.Draw(g, $"{room.Id + 1:00}", outside.X, outside.Y - 25, 1, C.Oxide,
                    LabTextAlign.Center, 0);
        }

        if (_roomRevealPulse <= 0) return;
        var pulseCenter = CellCenter(_lastRevealedDoor);
        var extent = 28 + (1 - _roomRevealPulse) * 52;
        using var pulse = new Pen(Color.FromArgb((int)(_roomRevealPulse * 180), C.Signal), 4);
        g.DrawRectangle(pulse, pulseCenter.X - extent, pulseCenter.Y - extent, extent * 2, extent * 2);
    }

    private void DrawMissionObjects(Graphics g)
    {
        foreach (var pickup in _creditPickups)
        {
            if (pickup.Collected || IsCellConcealed(pickup.Cell)) continue;
            var center = CellCenter(pickup.VisualCell);
            var pulse = 1 + MathF.Sin(_time * 3.7f + pickup.Phase) * .12f;
            var size = 13 * pulse;
            using var shadow = new SolidBrush(Color.FromArgb(130, 0, 0, 0));
            using var edge = new SolidBrush(C.Signal);
            using var core = new SolidBrush(Color.FromArgb(194, 171, 94));
            g.FillRectangle(shadow, center.X - size + 4, center.Y - size / 2 + 5, size * 2, size);
            g.FillRectangle(edge, center.X - size, center.Y - size / 2, size * 2, size);
            g.FillRectangle(core, center.X - size + 4, center.Y - size / 2 + 3, size * 2 - 8, size - 6);
            LabFont.Draw(g, "CR", center.X, center.Y - 5, 1, C.Ink, LabTextAlign.Center, 0);
        }

        foreach (var item in _cargoItems)
        {
            if (item.Carried || item.CarrierPlayerId is not null ||
                item.Delivered || IsCellConcealed(item.Cell)) continue;
            // Cargo identity is surfaced by the proximity prompt. Keeping the
            // floor crate unlabelled avoids a persistent floating tag in the room.
            DrawCargoCrate(g, CellCenter(item.Cell), item, 1f, drawLabel: false);
        }
    }

    private void DrawCarriedCargoRack(Graphics g)
    {
        var carried = _cargoItems.Where(item =>
            (IsOnlineGameplayActive
                ? item.CarrierPlayerId == _onlinePlayerId
                : item.Carried) &&
            !item.Delivered).ToList();
        if (carried.Count == 0) return;
        var center = CellCenter(_visualCell);
        center.Y += DroneFloatOffset(_drone, _droneBank, _dronePitch) + _cellSize * .23f;
        var rackWidth = Math.Max(22, carried.Count * 18 + 10);
        using var shadow = new SolidBrush(Color.FromArgb(180, C.Void));
        using var rack = new SolidBrush(C.Steel);
        g.FillRectangle(shadow, center.X - rackWidth / 2 + 3, center.Y - 4 + 4, rackWidth, 11);
        g.FillRectangle(rack, center.X - rackWidth / 2, center.Y - 4, rackWidth, 9);
        for (var i = 0; i < carried.Count; i++)
        {
            var itemCenter = new PointF(center.X + (i - (carried.Count - 1) / 2f) * 18, center.Y + 5);
            DrawCargoCrate(g, itemCenter, carried[i], .42f, drawLabel: false);
        }
    }

    private void DrawCargoCrate(Graphics g, PointF center, CargoItem item, float scale, bool drawLabel)
    {
        var width = 52 * scale;
        var height = 38 * scale;
        var depth = 9 * scale;
        var color = item.Kind switch
        {
            CargoKind.SignalRelay => Color.FromArgb(83, 146, 135),
            CargoKind.CryoCell => Color.FromArgb(83, 132, 168),
            CargoKind.TissueArchive => Color.FromArgb(164, 91, 105),
            CargoKind.SurveyCore => Color.FromArgb(176, 144, 71),
            CargoKind.BlackRecorder => Color.FromArgb(91, 96, 91),
            _ => Color.FromArgb(137, 94, 73)
        };
        using var shadow = new SolidBrush(Color.FromArgb(190, C.Void));
        using var shell = new SolidBrush(Color.FromArgb(170, 163, 125));
        using var shellLight = new SolidBrush(Color.FromArgb(205, 194, 148));
        using var shellDark = new SolidBrush(Color.FromArgb(96, 99, 80));
        using var band = new SolidBrush(color);
        using var dark = new SolidBrush(C.Ink);
        var left = center.X - width / 2;
        var top = center.Y - height / 2;
        g.FillRectangle(shadow, left + 7 * scale, top + 8 * scale, width, height);

        // A dimensional sealed freight case: inset face, raised lid, corner armor,
        // locking straps, and a manifest beacon instead of a flat item card.
        g.FillPolygon(shellDark,
        [
            new PointF(left + width, top + depth),
            new PointF(left + width + depth, top),
            new PointF(left + width + depth, top + height - depth),
            new PointF(left + width, top + height)
        ]);
        g.FillPolygon(shellLight,
        [
            new PointF(left, top + depth),
            new PointF(left + depth, top),
            new PointF(left + width + depth, top),
            new PointF(left + width, top + depth)
        ]);
        g.FillRectangle(shell, left, top + depth, width, height - depth);
        g.FillRectangle(dark, left + 5 * scale, top + depth + 5 * scale,
            width - 10 * scale, height - depth - 10 * scale);
        g.FillRectangle(band, left + 8 * scale, center.Y - 4 * scale,
            width - 16 * scale, 9 * scale);
        g.FillRectangle(shellLight, center.X - 3 * scale, top + depth, 6 * scale, height - depth);
        for (var corner = 0; corner < 2; corner++)
        {
            var x = corner == 0 ? left : left + width - 7 * scale;
            g.FillRectangle(shellLight, x, top + depth, 7 * scale, 8 * scale);
            g.FillRectangle(shellLight, x, top + height - 8 * scale, 7 * scale, 8 * scale);
        }
        using var beacon = new SolidBrush(((int)(_time * 5 + item.Phase) & 1) == 0 ? C.Signal : C.Oxide);
        g.FillRectangle(beacon, left + width - 11 * scale, top + depth + 5 * scale, 4 * scale, 4 * scale);
        if (scale > .7f)
        {
            using var stencil = new SolidBrush(Color.FromArgb(48, 52, 43));
            for (var stripe = 0; stripe < 4; stripe++)
                g.FillRectangle(stencil, left + 8 * scale + stripe * 9 * scale,
                    top + height - 7 * scale, 5 * scale, 3 * scale);
        }
        if (!drawLabel) return;
        var plate = new RectangleF(center.X - 58, center.Y + height / 2 + 12, 116, 25);
        using var plateFill = new SolidBrush(Color.FromArgb(224, C.Ink));
        g.FillRectangle(plateFill, plate);
        LabFont.Draw(g, item.Code, center.X, plate.Y + 5, 1, C.Bone, LabTextAlign.Center, 0);
    }

    private void DrawMissionPrompt(Graphics g)
    {
        string? text = null;
        var circuitPrompt = CircuitSwitchPrompt();
        var survivorPrompt = SurvivorInteractionPrompt();
        var directivePrompt = FieldDirectivePrompt();
        var cargo = FindCargoInLatchRange();
        var teammatePrompt = TeammateObjectivePrompt();
        if (circuitPrompt is not null) text = circuitPrompt;
        else if (survivorPrompt is not null) text = survivorPrompt;
        else if (IsShopKioskInRange(_playerCell))
            text = "E  ENTER RECLAMATION WINDOW / SAFE";
        else if (directivePrompt is not null) text = directivePrompt;
        else if (cargo is not null)
            text = cargo.Required && !IsObjectiveAssignedToLocal(cargo.AssignedPlayerId)
                ? $"{CargoName(cargo.Kind)} / ASSIGNED {ObjectiveOwnerName(cargo.AssignedPlayerId)}"
                : cargo.Required
                    ? $"E / PICK UP {CargoName(cargo.Kind)} / {cargo.Code}"
                    : $"{CargoName(cargo.Kind)} / {cargo.Code} / NOT MANIFESTED";
        else if (teammatePrompt is not null) text = teammatePrompt;
        else if (_missionNoticeTimer > 0) text = _missionNotice;
        if (text is null) return;

        var width = Math.Min(650, LabFont.Measure(text, 2).Width + 50);
        var panel = new RectangleF(_mazeRect.X + (_mazeRect.Width - width) / 2,
            _mazeRect.Bottom - 64, width, 42);
        DrawCutPanel(g, panel, Color.FromArgb(232, C.Ink), C.Oxide, 8, 3);
        LabFont.Draw(g, text, panel.X + panel.Width / 2, panel.Y + 12, 1,
            circuitPrompt is not null || survivorPrompt is not null || directivePrompt is not null ||
            cargo?.Required == true ||
            IsShopKioskInRange(_playerCell) || _missionNoticeTimer > 0 ? C.Signal : C.Sick,
            LabTextAlign.Center);
    }
}
