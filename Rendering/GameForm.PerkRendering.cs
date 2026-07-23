namespace Dust;

internal sealed partial class GameForm
{
    private void DrawRetracerTrail(Graphics g)
    {
        if (!_settings.HasEquippedPerk(PerkId.Retracer) || _retraceSegments.Count == 0) return;

        using var bed = new Pen(Color.FromArgb(105, 3, 9, 8), Math.Max(7, _cellSize * .13f));
        using var trace = new Pen(Color.FromArgb(118, _playerColor), Math.Max(3, _cellSize * .055f));
        using var node = new SolidBrush(Color.FromArgb(135, _playerFrameColor));
        foreach (var segment in _retraceSegments)
        {
            var from = CellCenter(segment.From);
            var to = CellCenter(segment.To);
            g.DrawLine(bed, from, to);
            g.DrawLine(trace, from, to);
            g.FillRectangle(node, to.X - 3, to.Y - 3, 6, 6);
        }
    }

    private void DrawPlayerPerkWorldEffects(Graphics g)
    {
        if (_hitEffect > 0) return;

        if (IsHollowKillerPulseActive)
            DrawHollowKillerPulse(g);
        if (!IsCamouflaged && !IsGhostFormActive) return;
        var center = CellCenter(_visualCell);
        center.Y += DroneFloatOffset(_drone, _droneBank, _dronePitch);

        if (IsCamouflaged)
        {
            var reach = _cellSize * (.32f + MathF.Sin(_time * 8) * .015f);
            using var ghost = new Pen(Color.FromArgb(105, C.Sick), 2);
            for (var line = -2; line <= 2; line++)
            {
                var jitter = ((line * 17 + (int)(_time * 19)) % 5) * 3;
                g.DrawLine(ghost, center.X - reach + jitter, center.Y + line * 9,
                    center.X + reach - jitter * .5f, center.Y + line * 9);
            }
            LabFont.Draw(g, "VEIL", center.X, center.Y - reach - 23, 1,
                Color.FromArgb(145, C.Sick), LabTextAlign.Center, 0);
            return;
        }

        var pulse = .5f + .5f * MathF.Sin(_time * 10);
        using var phaseOuter = new Pen(Color.FromArgb((int)(88 + pulse * 62), C.Signal), 3);
        using var phaseInner = new Pen(Color.FromArgb(112, _playerColor), 2);
        var outer = _cellSize * (.38f + pulse * .045f);
        g.DrawRectangle(phaseOuter, center.X - outer, center.Y - outer, outer * 2, outer * 2);
        g.DrawRectangle(phaseInner, center.X - outer * .73f, center.Y - outer * .73f,
            outer * 1.46f, outer * 1.46f);
        for (var index = 0; index < 4; index++)
        {
            var angle = _time * 2.7f + index * MathF.PI / 2;
            var x = center.X + MathF.Cos(angle) * outer;
            var y = center.Y + MathF.Sin(angle) * outer;
            using var mote = new SolidBrush(index % 2 == 0 ? C.Signal : _playerColor);
            g.FillRectangle(mote, x - 3, y - 3, 6, 6);
        }
    }

    private void DrawPerkTelemetry(Graphics g)
    {
        if (!HasSpacePerk) return;

        var ghostSelected = _settings.HasEquippedPerk(PerkId.GhostForm);
        var killerSelected = _settings.HasEquippedPerk(PerkId.HollowKiller);
        var title = killerSelected ? "VOID PULSE" : ghostSelected ? "PHASE COIL" : "OPTIC VEIL";
        var active = killerSelected ? IsHollowKillerPulseActive :
            ghostSelected ? IsGhostFormActive : IsCamouflaged;
        var remaining = killerSelected ? _hollowKillerPulse :
            ghostSelected ? _ghostFormTimer : _camouflageTimer;
        var cooldown = killerSelected ? _hollowKillerCooldown :
            ghostSelected ? _ghostFormCooldown : _camouflageCooldown;
        var status = active
            ? killerSelected ? "PURGE" : $"OPEN {remaining:0.0}"
            : cooldown > 0 ? $"RECYCLE {cooldown:00.0}" : "SPACE / READY";
        var panel = new RectangleF(_mazeRect.X + 16, _mazeRect.Y + 136, 276, 47);
        DrawCutPanel(g, panel, Color.FromArgb(218, C.Ink),
            active ? C.Signal : Color.FromArgb(135, C.Steel), 8, 2);
        LabFont.Draw(g, title, panel.X + 12, panel.Y + 9, 1, active ? C.Signal : C.Sick);
        LabFont.Draw(g, status, panel.Right - 12, panel.Y + 9, 1,
            active ? C.Bone : C.Steel, LabTextAlign.Right);
        var bar = new RectangleF(panel.X + 12, panel.Bottom - 12, panel.Width - 24, 4);
        using var bed = new SolidBrush(Color.FromArgb(68, C.Steel));
        using var fill = new SolidBrush(active ? C.Signal : C.Oxide);
        g.FillRectangle(bed, bar);
        var fraction = active
            ? remaining / (killerSelected ? HollowKillerPulseDuration :
                ghostSelected ? GhostFormDuration : CamouflageDuration)
            : cooldown <= 0 ? 1 : 1 - cooldown / (killerSelected ? HollowKillerRecharge :
                ghostSelected ? GhostFormRecharge : CamouflageRecharge);
        g.FillRectangle(fill, bar.X, bar.Y, bar.Width * Math.Clamp(fraction, 0, 1), bar.Height);
    }

    private void DrawHollowKillerPulse(Graphics g)
    {
        var center = CellCenter(_hollowKillerCenter);
        var progress = 1 - _hollowKillerPulse / HollowKillerPulseDuration;
        var radius = _cellSize * HollowKillerRadius * (.12f + progress * .88f);
        var alpha = (int)(190 * (1 - progress));
        var points = new PointF[8];
        for (var index = 0; index < points.Length; index++)
        {
            var angle = MathF.PI / 8 + index * MathF.PI / 4;
            var stepped = index % 2 == 0 ? radius : radius * .91f;
            points[index] = new PointF(center.X + MathF.Cos(angle) * stepped,
                center.Y + MathF.Sin(angle) * stepped);
        }

        using var outer = new Pen(Color.FromArgb(Math.Clamp(alpha, 0, 255), C.Signal), 5);
        using var inner = new Pen(Color.FromArgb(Math.Clamp(alpha / 2, 0, 255), C.Bone), 2);
        using var core = new SolidBrush(Color.FromArgb(Math.Clamp(alpha / 3, 0, 255), C.Oxide));
        g.DrawPolygon(outer, points);
        g.DrawRectangle(inner, center.X - radius * .66f, center.Y - radius * .66f,
            radius * 1.32f, radius * 1.32f);
        var coreSize = Math.Max(4, _cellSize * .13f * (1 - progress));
        g.FillRectangle(core, center.X - coreSize, center.Y - coreSize, coreSize * 2, coreSize * 2);
    }

    private void DrawMiniMap(Graphics g)
    {
        if (!_settings.HasEquippedPerk(PerkId.MiniMap) || _maze is null) return;

        var panel = new RectangleF(_mazeRect.Right - 276, _mazeRect.Bottom - 218, 248, 158);
        DrawCutPanel(g, panel, Color.FromArgb(232, C.Ink), Color.FromArgb(145, C.Steel), 10, 3);
        LabFont.Draw(g, "LOCAL TRACE", panel.X + 12, panel.Y + 9, 1, C.Signal);
        var completion = _maze.Width * _maze.Height == 0
            ? 0
            : _visited.Count * 100 / (_maze.Width * _maze.Height);
        LabFont.Draw(g, $"{completion:00}%", panel.Right - 12, panel.Y + 9, 1,
            C.Sick, LabTextAlign.Right);

        var mapArea = new RectangleF(panel.X + 12, panel.Y + 32, panel.Width - 24, panel.Height - 44);
        var scale = Math.Min(mapArea.Width / _maze.Width, mapArea.Height / _maze.Height);
        var origin = new PointF(
            mapArea.X + (mapArea.Width - _maze.Width * scale) / 2,
            mapArea.Y + (mapArea.Height - _maze.Height * scale) / 2);
        using var path = new Pen(Color.FromArgb(150, C.Sick), Math.Max(1, scale * .55f));
        using var room = new SolidBrush(Color.FromArgb(130, C.Oxide));
        using var node = new SolidBrush(Color.FromArgb(205, C.Bone));
        using var player = new SolidBrush(C.Signal);

        foreach (var cell in _visited)
        {
            var center = new PointF(origin.X + (cell.X + .5f) * scale,
                origin.Y + (cell.Y + .5f) * scale);
            if (_maze.GetRoomAt(cell) is not null)
                g.FillRectangle(room, center.X - scale * .45f, center.Y - scale * .45f,
                    Math.Max(1, scale * .9f), Math.Max(1, scale * .9f));
            else
                g.FillRectangle(node, center.X - Math.Max(1, scale * .2f),
                    center.Y - Math.Max(1, scale * .2f), Math.Max(2, scale * .4f), Math.Max(2, scale * .4f));

            for (var directionValue = (int)Direction.Right;
                 directionValue <= (int)Direction.Down;
                 directionValue++)
            {
                var direction = (Direction)directionValue;
                if (!_maze.CanMove(cell, direction)) continue;
                var next = _maze.Move(cell, direction);
                if (!_visited.Contains(next)) continue;
                var nextCenter = new PointF(origin.X + (next.X + .5f) * scale,
                    origin.Y + (next.Y + .5f) * scale);
                g.DrawLine(path, center, nextCenter);
            }
        }

        var playerCenter = new PointF(origin.X + (_visualCell.X + .5f) * scale,
            origin.Y + (_visualCell.Y + .5f) * scale);
        var marker = Math.Max(5, scale * 1.45f);
        g.FillRectangle(player, playerCenter.X - marker / 2, playerCenter.Y - marker / 2, marker, marker);
    }
}
