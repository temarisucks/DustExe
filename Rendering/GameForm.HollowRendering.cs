using System.Drawing.Drawing2D;

namespace Dust;

internal sealed partial class GameForm
{
    private void DrawHollows(Graphics g)
    {
        foreach (var hollow in _hollows)
        {
            var renderCell = HollowRenderCell(hollow);
            if (IsPositionConcealed(renderCell)) continue;
            var center = CellCenter(renderCell);
            var radius = Math.Max(14, (int)(_cellSize * .29f) - 5);
            var signal = hollow.Empowered
                ? EnemyEmpowermentColor(hollow.AnimationPhase)
                : hollow.State switch
            {
                HollowState.Chase => C.Red,
                HollowState.Search => C.Signal,
                _ => C.Bone
            };

            switch (hollow.Type)
            {
                case HollowType.Square:
                    DrawSquareHollow(g, hollow, center, radius, signal);
                    break;
                case HollowType.Diamond:
                    DrawDiamondHollow(g, hollow, center, radius, signal);
                    break;
                case HollowType.Hex:
                    DrawHexHollow(g, hollow, center, radius, signal);
                    break;
                case HollowType.Triangle:
                    DrawTriangleHollow(g, hollow, center, radius, signal);
                    break;
                case HollowType.Camera:
                    DrawCameraHollow(g, hollow, center, radius, signal,
                        HollowRenderFacing(hollow));
                    break;
                case HollowType.Star:
                    DrawStarHollow(g, hollow, center, radius, signal);
                    break;
            }

            if (hollow.TeleportFlash > 0)
            {
                var progress = 1 - hollow.TeleportFlash / .42f;
                using var jumpRing = new Pen(
                    Color.FromArgb((int)(180 * (1 - progress)), signal), 4);
                var jumpRadius = radius * (.7f + progress * 1.7f);
                g.DrawEllipse(jumpRing, center.X - jumpRadius, center.Y - jumpRadius,
                    jumpRadius * 2, jumpRadius * 2);
            }
        }
    }

    private Color EnemyEmpowermentColor(float phase)
    {
        var channel = ((int)MathF.Floor((_time + phase * .13f) * 7.5f) % 3 + 3) % 3;
        return channel switch
        {
            0 => Color.FromArgb(235, 65, 62),
            1 => Color.FromArgb(73, 224, 113),
            _ => Color.FromArgb(73, 139, 244)
        };
    }

    private void DrawSquareHollow(Graphics g, Hollow hollow, PointF center, float radius, Color signal)
    {
        // Two nested cages keep the middle visibly empty while their opposing motion
        // makes this slow Hollow feel like a piece of rotating test equipment.
        var outerStep = MathF.Round((_time * .72f + hollow.AnimationPhase) / (MathF.PI / 16)) * (MathF.PI / 16);
        var innerStep = MathF.Round((-_time * 1.08f + hollow.AnimationPhase * .61f) / (MathF.PI / 16)) * (MathF.PI / 16);
        DrawHollowRing(g, RegularPolygonPoints(center, 4, radius, MathF.PI / 4 + outerStep), signal, 10, 4);
        DrawHollowRing(g, RegularPolygonPoints(center, 4, radius * .58f, MathF.PI / 4 + innerStep),
            Color.FromArgb(205, signal), 8, 3);
    }

    private void DrawDiamondHollow(Graphics g, Hollow hollow, PointF center, float radius, Color signal)
    {
        var diamondRadius = radius * .60f;
        DrawHollowRing(g, RegularPolygonPoints(center, 4, diamondRadius, -MathF.PI / 2), signal, 10, 4);

        var orbit = _time * .78f + hollow.AnimationPhase;
        for (var i = 0; i < 4; i++)
        {
            var angle = orbit + i * MathF.PI / 2;
            var pulse = .45f + .55f * ((MathF.Sin(_time * 4.2f + hollow.AnimationPhase + i * .8f) + 1) / 2);
            var triangleSize = radius * (.16f + pulse * .06f);
            var orbitRadius = radius * .92f;
            var direction = new PointF(MathF.Cos(angle), MathF.Sin(angle));
            var tangent = new PointF(-direction.Y, direction.X);
            var baseCenter = new PointF(
                center.X + direction.X * (orbitRadius - triangleSize * .55f),
                center.Y + direction.Y * (orbitRadius - triangleSize * .55f));
            var triangle = new PointF[]
            {
                new(center.X + direction.X * (orbitRadius + triangleSize),
                    center.Y + direction.Y * (orbitRadius + triangleSize)),
                new(baseCenter.X + tangent.X * triangleSize * .72f,
                    baseCenter.Y + tangent.Y * triangleSize * .72f),
                new(baseCenter.X - tangent.X * triangleSize * .72f,
                    baseCenter.Y - tangent.Y * triangleSize * .72f)
            };
            DrawHollowTriangle(g, triangle, Color.FromArgb((int)(150 + pulse * 105), signal));
        }
    }

    private void DrawHexHollow(Graphics g, Hollow hollow, PointF center, float radius, Color signal)
    {
        var points = RegularPolygonPoints(center, 6, radius, -MathF.PI / 2);
        var cycle = (_time + hollow.AnimationPhase * .73f) % 3.7f;
        var glitching = _hitEffect <= 0 && (cycle < .17f || cycle is > 2.31f and < 2.38f);
        if (!glitching)
        {
            DrawHollowRing(g, points, signal, 10, 4);
            return;
        }

        DrawHollowRing(g, points, Color.FromArgb(85, signal), 8, 3);
        var bandHeight = Math.Max(4, (int)(radius / 3));
        var band = 0;
        for (var y = center.Y - radius - 2; y < center.Y + radius + 3; y += bandHeight)
        {
            var state = g.Save();
            g.SetClip(new RectangleF(center.X - radius - 8, y, radius * 2 + 16, bandHeight - 1), CombineMode.Intersect);
            var offset = (((band * 7 + (int)(_time * 90) + (int)(hollow.AnimationPhase * 13)) % 5) - 2) * 2;
            DrawHollowRing(g, OffsetPoints(points, offset, 0), signal, 10, 4);
            g.Restore(state);
            band++;
        }
    }

    private void DrawTriangleHollow(
        Graphics g,
        Hollow hollow,
        PointF center,
        float radius,
        Color signal)
    {
        if (!hollow.TriangleSplit)
        {
            var outerStep = MathF.Round(
                (_time * 1.16f + hollow.AnimationPhase) / (MathF.PI / 18)) *
                (MathF.PI / 18);
            var innerStep = MathF.Round(
                (-_time * 1.72f + hollow.AnimationPhase * .63f) / (MathF.PI / 18)) *
                (MathF.PI / 18);
            DrawHollowRing(g,
                RegularPolygonPoints(center, 3, radius, -MathF.PI / 2 + outerStep),
                signal, 10, 4);
            DrawHollowRing(g,
                RegularPolygonPoints(center, 3, radius * .55f,
                    -MathF.PI / 2 + innerStep),
                Color.FromArgb(205, signal), 8, 3);
            return;
        }

        var orbitRadius = _cellSize * .30f;
        var renderOrbit = hollow.TriangleOrbitAngle;
        if (IsOnlineGameplayActive && !IsOnlineSimulationHost &&
            hollow.PresentationReady)
            renderOrbit = NormalizeAngle(
                renderOrbit + hollow.PresentationSnapshotAge * 2.75f);
        for (var index = 0; index < 3; index++)
        {
            var orbit = renderOrbit + index * MathF.PI * 2 / 3;
            var member = new PointF(
                center.X + MathF.Cos(orbit) * orbitRadius,
                center.Y + MathF.Sin(orbit) * orbitRadius);
            var rotation = -MathF.PI / 2 + _time * (2.1f + index * .16f) +
                           hollow.AnimationPhase;
            DrawHollowRing(g,
                RegularPolygonPoints(member, 3, radius * .62f, rotation),
                signal, 9, 3);
        }
    }

    private void DrawCameraHollow(
        Graphics g,
        Hollow hollow,
        PointF center,
        float radius,
        Color signal,
        float renderFacing)
    {
        var state = g.Save();
        g.TranslateTransform(center.X, center.Y);
        g.RotateTransform(renderFacing * 180 / MathF.PI);
        var body = new RectangleF(-radius * .65f, -radius * .48f,
            radius * 1.08f, radius * .96f);
        using var voidBrush = new SolidBrush(Color.FromArgb(225, 3, 7, 7));
        using var shell = new Pen(signal, 4) { LineJoin = LineJoin.Miter };
        using var mount = new Pen(Color.FromArgb(185, C.Steel), 7)
        {
            StartCap = LineCap.Square,
            EndCap = LineCap.Square
        };
        g.DrawLine(mount, -radius * .98f, 0, -radius * .58f, 0);
        g.FillRectangle(voidBrush, body);
        g.DrawRectangle(shell, body.X, body.Y, body.Width, body.Height);
        var lensCenter = new PointF(radius * .47f, 0);
        g.FillEllipse(voidBrush, lensCenter.X - radius * .30f,
            lensCenter.Y - radius * .30f, radius * .60f, radius * .60f);
        g.DrawEllipse(shell, lensCenter.X - radius * .30f,
            lensCenter.Y - radius * .30f, radius * .60f, radius * .60f);
        using var lens = new SolidBrush(Color.FromArgb(210, signal));
        g.FillEllipse(lens, lensCenter.X - radius * .11f,
            lensCenter.Y - radius * .11f, radius * .22f, radius * .22f);
        g.Restore(state);
    }

    private void DrawStarHollow(
        Graphics g,
        Hollow hollow,
        PointF center,
        float radius,
        Color signal)
    {
        var auraPulse = .5f + .5f * MathF.Sin(_time * 2.6f + hollow.AnimationPhase);
        using (var aura = new Pen(
                   Color.FromArgb((int)(42 + auraPulse * 38), signal), 3))
        {
            var auraRadius = radius * (1.35f + auraPulse * .12f);
            g.DrawEllipse(aura, center.X - auraRadius, center.Y - auraRadius,
                auraRadius * 2, auraRadius * 2);
        }
        var rotation = -MathF.PI / 2 + _time * .78f + hollow.AnimationPhase;
        var points = new PointF[10];
        for (var index = 0; index < points.Length; index++)
        {
            var pointRadius = index % 2 == 0 ? radius : radius * .43f;
            var angle = rotation + index * MathF.PI / 5;
            points[index] = new PointF(
                center.X + MathF.Cos(angle) * pointRadius,
                center.Y + MathF.Sin(angle) * pointRadius);
        }
        DrawHollowRing(g, points, signal, 10, 4);
        var core = RegularPolygonPoints(center, 5, radius * .28f, -rotation);
        DrawHollowRing(g, core, Color.FromArgb(190, signal), 7, 3);
    }

    private static void DrawHollowRing(Graphics g, PointF[] points, Color signal, float keylineWidth, float signalWidth)
    {
        using var voidKeyline = new Pen(Color.FromArgb(Math.Min(235, (int)signal.A), 3, 7, 7), keylineWidth)
        {
            LineJoin = LineJoin.Miter
        };
        using var porcelainLine = new Pen(signal, signalWidth)
        {
            LineJoin = LineJoin.Miter
        };
        g.DrawPolygon(voidKeyline, points);
        g.DrawPolygon(porcelainLine, points);
    }

    private static void DrawHollowTriangle(Graphics g, PointF[] points, Color signal)
    {
        using var keyline = new SolidBrush(Color.FromArgb(Math.Min(230, (int)signal.A), 3, 7, 7));
        using var marker = new SolidBrush(signal);
        g.FillPolygon(keyline, points);
        var center = new PointF(points.Average(point => point.X), points.Average(point => point.Y));
        var inner = points.Select(point => new PointF(
            center.X + (point.X - center.X) * .72f,
            center.Y + (point.Y - center.Y) * .72f)).ToArray();
        g.FillPolygon(marker, inner);
    }

    private static PointF[] OffsetPoints(PointF[] points, float x, float y)
    {
        var offset = new PointF[points.Length];
        for (var i = 0; i < points.Length; i++) offset[i] = new PointF(points[i].X + x, points[i].Y + y);
        return offset;
    }

    private void DrawHollowVisionCones(Graphics g)
    {
        const float innerRadius = .36f;
        using var roamingFields = new GraphicsPath(FillMode.Winding);
        using var searchingFields = new GraphicsPath(FillMode.Winding);
        using var chasingFields = new GraphicsPath(FillMode.Winding);
        foreach (var hollow in _hollows)
        {
            var renderCell = HollowRenderCell(hollow);
            var renderFacing = HollowRenderFacing(hollow);
            if (IsPositionConcealed(renderCell)) continue;
            var origin = CellCenter(renderCell);
            var viewDistance = HollowViewRange(hollow, hollow.HasSight);
            var fieldOfView = HollowFieldOfView(hollow, hollow.HasSight);
            var reach = viewDistance * _cellSize;
            if (!RectangleF.Inflate(_mazeRect, reach, reach).Contains(origin)) continue;

            var rayOffsets = BuildVisionRayOffsets(
                hollow, renderCell, renderFacing, fieldOfView, viewDistance);
            var outer = new List<PointF>(rayOffsets.Count);
            var inner = new List<PointF>(rayOffsets.Count);
            foreach (var offset in rayOffsets)
            {
                var angle = renderFacing + offset;
                var distance = RaycastVisionDistance(
                    renderCell, angle, viewDistance, HollowIgnoresVisionWalls(hollow));
                distance = Math.Max(innerRadius, distance);
                outer.Add(SnapToFeed(CellCenter(new PointF(
                    renderCell.X + MathF.Cos(angle) * distance,
                    renderCell.Y + MathF.Sin(angle) * distance))));
                inner.Add(SnapToFeed(CellCenter(new PointF(
                    renderCell.X + MathF.Cos(angle) * innerRadius,
                    renderCell.Y + MathF.Sin(angle) * innerRadius))));
            }

            var field = outer.Concat(inner.AsEnumerable().Reverse()).ToArray();
            var path = hollow.State switch
            {
                HollowState.Chase => chasingFields,
                HollowState.Search => searchingFields,
                _ => roamingFields
            };
            path.AddPolygon(field);
        }

        using var roamingExposure = new SolidBrush(Color.FromArgb(20, C.Sick));
        using var searchingExposure = new SolidBrush(Color.FromArgb(22, C.Signal));
        using var chasingExposure = new SolidBrush(Color.FromArgb(26, C.Oxide));
        if (roamingFields.PointCount > 0) g.FillPath(roamingExposure, roamingFields);
        if (searchingFields.PointCount > 0) g.FillPath(searchingExposure, searchingFields);
        if (chasingFields.PointCount > 0) g.FillPath(chasingExposure, chasingFields);
    }

    private List<float> BuildVisionRayOffsets(
        Hollow hollow,
        PointF renderCell,
        float renderFacing,
        float fieldOfView,
        float viewDistance)
    {
        var rayCount = hollow.Type switch
        {
            HollowType.Square => 24,
            HollowType.Diamond => 40,
            HollowType.Camera => 36,
            HollowType.Triangle => 32,
            HollowType.Star => 34,
            _ => 18
        };
        var halfField = fieldOfView / 2;
        var offsets = new List<float>(rayCount + 48);
        for (var i = 0; i <= rayCount; i++)
            offsets.Add(-halfField + fieldOfView * i / rayCount);

        if (_maze is not null && !HollowIgnoresVisionWalls(hollow))
        {
            const float edgeEpsilon = .0015f;
            var minGridX = Math.Max(0, (int)MathF.Floor(renderCell.X - viewDistance + .5f));
            var maxGridX = Math.Min(_maze.Width, (int)MathF.Ceiling(renderCell.X + viewDistance + .5f));
            var minGridY = Math.Max(0, (int)MathF.Floor(renderCell.Y - viewDistance + .5f));
            var maxGridY = Math.Min(_maze.Height, (int)MathF.Ceiling(renderCell.Y + viewDistance + .5f));
            var maximumSquared = (viewDistance + .05f) * (viewDistance + .05f);
            for (var gridX = minGridX; gridX <= maxGridX; gridX++)
            for (var gridY = minGridY; gridY <= maxGridY; gridY++)
            {
                var dx = gridX - .5f - renderCell.X;
                var dy = gridY - .5f - renderCell.Y;
                var distanceSquared = dx * dx + dy * dy;
                if (distanceSquared < .01f || distanceSquared > maximumSquared) continue;
                var relative = NormalizeAngle(MathF.Atan2(dy, dx) - renderFacing);
                if (Math.Abs(relative) > halfField + edgeEpsilon) continue;
                offsets.Add(Math.Clamp(relative - edgeEpsilon, -halfField, halfField));
                offsets.Add(Math.Clamp(relative, -halfField, halfField));
                offsets.Add(Math.Clamp(relative + edgeEpsilon, -halfField, halfField));
            }
        }

        offsets.Sort();
        var unique = new List<float>(offsets.Count);
        foreach (var offset in offsets)
            if (unique.Count == 0 || offset - unique[^1] > .0002f) unique.Add(offset);
        return unique;
    }

    private static PointF SnapToFeed(PointF point) =>
        new(MathF.Round(point.X / 2) * 2, MathF.Round(point.Y / 2) * 2);

    private static PointF[] RegularPolygonPoints(PointF center, int sides, float radius, float rotation)
    {
        var points = new PointF[sides];
        for (var i = 0; i < sides; i++)
        {
            var angle = rotation + i * MathF.PI * 2 / sides;
            points[i] = new PointF(
                MathF.Round((center.X + MathF.Cos(angle) * radius) / 2) * 2,
                MathF.Round((center.Y + MathF.Sin(angle) * radius) / 2) * 2);
        }
        return points;
    }
}
