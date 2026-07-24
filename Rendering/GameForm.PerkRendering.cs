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
        Span<PerkId> equipped = stackalloc PerkId[ProgressionCatalog.Perks.Length];
        var equippedCount = 0;
        foreach (var definition in ProgressionCatalog.Perks)
            if (_settings.HasEquippedPerk(definition.Id))
                equipped[equippedCount++] = definition.Id;
        if (equippedCount == 0) return;

        // Six sockets is the largest valid profile (five passive perks and one
        // Space-channel perk). Keeping every state inside its own badge avoids
        // long equipment names fighting a changing status readout.
        const float iconSize = 38;
        const float gap = 6;
        const float padding = 8;
        var panelWidth = padding * 2 + equippedCount * iconSize +
                         Math.Max(0, equippedCount - 1) * gap;
        var panel = new RectangleF(_mazeRect.X + 16, _mazeRect.Y + 136, panelWidth, 54);
        DrawCutPanel(g, panel, Color.FromArgb(218, C.Ink),
            Color.FromArgb(135, C.Steel), 8, 2);

        for (var index = 0; index < equippedCount; index++)
        {
            var perk = equipped[index];
            var badge = new RectangleF(
                panel.X + padding + index * (iconSize + gap),
                panel.Y + padding, iconSize, iconSize);
            DrawPerkTelemetryBadge(g, badge, perk);
        }
    }

    private void DrawPerkTelemetryBadge(Graphics g, RectangleF badge, PerkId perk)
    {
        var spaceChannel = perk is PerkId.Camouflage or PerkId.GhostForm or PerkId.HollowKiller;
        var active = perk switch
        {
            PerkId.Camouflage => IsCamouflaged,
            PerkId.GhostForm => IsGhostFormActive,
            PerkId.HollowKiller => IsHollowKillerPulseActive,
            _ => false
        };
        var cooldown = perk switch
        {
            PerkId.Camouflage => _camouflageCooldown,
            PerkId.GhostForm => _ghostFormCooldown,
            PerkId.HollowKiller => _hollowKillerCooldown,
            _ => 0
        };
        var maximumCooldown = perk switch
        {
            PerkId.Camouflage => CamouflageRecharge,
            PerkId.GhostForm => GhostFormRecharge,
            PerkId.HollowKiller => HollowKillerRecharge,
            _ => 0
        };
        var cooling = spaceChannel && !active && cooldown > .01f;
        var ready = spaceChannel && !active && !cooling;
        var edgeColor = active
            ? C.Signal
            : cooling
                ? Color.FromArgb(170, C.Oxide)
                : ready
                    ? C.Sick
                    : Color.FromArgb(130, C.Steel);

        DrawCutPanel(g, badge, Color.FromArgb(238, 7, 12, 12), edgeColor, 5, active ? 3 : 2);

        var glyph = RectangleF.Inflate(badge, -6, -6);
        DrawPerkGlyph(g, perk, glyph, active ? C.Signal :
            cooling ? Color.FromArgb(130, C.Steel) : C.Bone);

        if (cooling)
        {
            var remainingFraction = Math.Clamp(cooldown / maximumCooldown, 0, 1);
            var readyFraction = 1 - remainingFraction;
            var mask = new RectangleF(
                badge.X + 3, badge.Y + 3, badge.Width - 6,
                (badge.Height - 9) * remainingFraction);
            using var shade = new SolidBrush(Color.FromArgb(192, 2, 7, 7));
            using var scan = new SolidBrush(Color.FromArgb(92, C.Oxide));
            g.FillRectangle(shade, mask);
            for (var y = mask.Y + 3; y < mask.Bottom; y += 6)
                g.FillRectangle(scan, mask.X + 2, y, Math.Max(0, mask.Width - 4), 1);

            // Five discrete lamps fill from left to right as the channel
            // recharges. This remains legible even at the smallest window size.
            const int segments = 5;
            var segmentGap = 2f;
            var segmentWidth = (badge.Width - 8 - segmentGap * (segments - 1)) / segments;
            var litSegments = (int)MathF.Ceiling(readyFraction * segments - .001f);
            using var emptyLamp = new SolidBrush(Color.FromArgb(86, C.Steel));
            using var chargedLamp = new SolidBrush(C.Oxide);
            for (var segment = 0; segment < segments; segment++)
            {
                var lamp = new RectangleF(
                    badge.X + 4 + segment * (segmentWidth + segmentGap),
                    badge.Bottom - 5, segmentWidth, 2);
                g.FillRectangle(segment < litSegments ? chargedLamp : emptyLamp, lamp);
            }
            return;
        }

        if (active)
        {
            var scannerX = badge.X + 5 + (_time * 31 % Math.Max(1, badge.Width - 10));
            using var scanner = new SolidBrush(Color.FromArgb(155, C.Bone));
            g.FillRectangle(scanner, scannerX, badge.Y + 3, 2, badge.Height - 6);
            return;
        }

        if (!ready) return;
        var pulse = (int)(155 + 80 * (.5f + .5f * MathF.Sin(_time * 6)));
        using var readyLamp = new SolidBrush(Color.FromArgb(pulse, C.Signal));
        g.FillRectangle(readyLamp, badge.X + 3, badge.Y + 3, 4, 4);
        g.FillRectangle(readyLamp, badge.Right - 7, badge.Y + 3, 4, 4);
        g.FillRectangle(readyLamp, badge.X + 3, badge.Bottom - 7, 4, 4);
        g.FillRectangle(readyLamp, badge.Right - 7, badge.Bottom - 7, 4, 4);
    }

    private static void DrawPerkGlyph(Graphics g, PerkId perk, RectangleF rect, Color color)
    {
        using var brush = new SolidBrush(color);
        using var pen = new Pen(color, 3);
        var center = new PointF(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var left = rect.X + 2;
        var top = rect.Y + 2;
        var right = rect.Right - 2;
        var bottom = rect.Bottom - 2;

        switch (perk)
        {
            case PerkId.Durable:
            {
                var point0 = new PointF(center.X, top);
                var point1 = new PointF(right, top + 5);
                var point2 = new PointF(right - 2, center.Y + 5);
                var point3 = new PointF(center.X, bottom);
                var point4 = new PointF(left + 2, center.Y + 5);
                var point5 = new PointF(left, top + 5);
                g.DrawLine(pen, point0, point1);
                g.DrawLine(pen, point1, point2);
                g.DrawLine(pen, point2, point3);
                g.DrawLine(pen, point3, point4);
                g.DrawLine(pen, point4, point5);
                g.DrawLine(pen, point5, point0);
                g.FillRectangle(brush, center.X - 2, top + 6, 4, 11);
                g.FillRectangle(brush, center.X - 6, top + 10, 12, 4);
                break;
            }
            case PerkId.MoneyMagnet:
                g.FillRectangle(brush, left, top, 6, rect.Height - 8);
                g.FillRectangle(brush, right - 4, top, 6, rect.Height - 8);
                g.FillRectangle(brush, left + 4, bottom - 6, rect.Width - 8, 6);
                using (var pole = new SolidBrush(C.Oxide))
                {
                    g.FillRectangle(pole, left, top, 6, 5);
                    g.FillRectangle(pole, right - 4, top, 6, 5);
                }
                break;
            case PerkId.Hop:
            {
                var first0 = new PointF(left, center.Y - 7);
                var first1 = new PointF(center.X - 2, top);
                var first2 = new PointF(center.X + 3, top + 5);
                var first3 = new PointF(left + 7, center.Y - 7);
                g.DrawLine(pen, first0, first1);
                g.DrawLine(pen, first1, first2);
                g.DrawLine(pen, first2, first3);
                g.DrawLine(pen, first0.X + 5, first0.Y + 12, first1.X + 5, first1.Y + 12);
                g.DrawLine(pen, first1.X + 5, first1.Y + 12, first2.X + 5, first2.Y + 12);
                g.DrawLine(pen, first2.X + 5, first2.Y + 12, first3.X + 5, first3.Y + 12);
                g.FillRectangle(brush, right - 7, bottom - 4, 7, 4);
                break;
            }
            case PerkId.Camouflage:
            {
                var point0 = new PointF(left, center.Y);
                var point1 = new PointF(center.X - 5, top + 4);
                var point2 = new PointF(center.X + 5, top + 4);
                var point3 = new PointF(right, center.Y);
                var point4 = new PointF(center.X + 5, bottom - 4);
                var point5 = new PointF(center.X - 5, bottom - 4);
                g.DrawLine(pen, point0, point1);
                g.DrawLine(pen, point1, point2);
                g.DrawLine(pen, point2, point3);
                g.DrawLine(pen, point3, point4);
                g.DrawLine(pen, point4, point5);
                g.DrawLine(pen, point5, point0);
                g.FillRectangle(brush, center.X - 3, center.Y - 3, 6, 6);
                using (var cut = new Pen(Color.FromArgb(238, 7, 12, 12), 4))
                    g.DrawLine(cut, left + 2, bottom - 1, right - 2, top + 1);
                break;
            }
            case PerkId.MiniMap:
                for (var x = 0; x < 3; x++)
                for (var y = 0; y < 3; y++)
                {
                    var tile = new RectangleF(left + x * 8, top + y * 8, 5, 5);
                    if (x == 1 && y == 1) g.FillRectangle(brush, tile);
                    else g.DrawRectangle(pen, tile.X, tile.Y, tile.Width, tile.Height);
                }
                break;
            case PerkId.GhostForm:
                g.FillRectangle(brush, left, top, 4, rect.Height);
                g.FillRectangle(brush, right - 2, top, 4, rect.Height);
                g.FillRectangle(brush, left + 7, center.Y - 2, rect.Width - 14, 4);
                g.FillRectangle(brush, center.X - 2, center.Y - 7, 4, 14);
                using (var phase = new Pen(Color.FromArgb(145, color), 2))
                {
                    g.DrawLine(phase, center.X - 6, top + 2, center.X + 6, bottom - 2);
                    g.DrawLine(phase, center.X - 6, bottom - 2, center.X + 6, top + 2);
                }
                break;
            case PerkId.Retracer:
            {
                var node0 = new PointF(left + 1, bottom - 2);
                var node1 = new PointF(left + 8, center.Y + 2);
                var node2 = new PointF(right - 8, center.Y + 2);
                var node3 = new PointF(right - 1, top + 2);
                g.DrawLine(pen, node0, node1);
                g.DrawLine(pen, node1, node2);
                g.DrawLine(pen, node2, node3);
                g.FillRectangle(brush, node0.X - 2, node0.Y - 2, 5, 5);
                g.FillRectangle(brush, node1.X - 2, node1.Y - 2, 5, 5);
                g.FillRectangle(brush, node2.X - 2, node2.Y - 2, 5, 5);
                g.FillRectangle(brush, node3.X - 2, node3.Y - 2, 5, 5);
                break;
            }
            case PerkId.HollowKiller:
            {
                var point0 = new PointF(center.X, top);
                var point1 = new PointF(right, center.Y);
                var point2 = new PointF(center.X, bottom);
                var point3 = new PointF(left, center.Y);
                g.DrawLine(pen, point0, point1);
                g.DrawLine(pen, point1, point2);
                g.DrawLine(pen, point2, point3);
                g.DrawLine(pen, point3, point0);
                g.FillRectangle(brush, center.X - 2, top + 4, 4, rect.Height - 8);
                g.FillRectangle(brush, left + 4, center.Y - 2, rect.Width - 8, 4);
                using var core = new SolidBrush(C.Signal);
                g.FillRectangle(core, center.X - 3, center.Y - 3, 6, 6);
                break;
            }
        }
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
