using System.Drawing.Drawing2D;

namespace Dust;

internal sealed partial class GameForm
{
    private void DrawHollows(Graphics g)
    {
        foreach (var hollow in _hollows)
        {
            if (IsPositionConcealed(hollow.VisualCell)) continue;
            var center = CellCenter(hollow.VisualCell);
            var radius = Math.Max(14, (int)(_cellSize * .29f) - 5);
            var signal = hollow.State switch
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
                default:
                    DrawHexHollow(g, hollow, center, radius, signal);
                    break;
            }
        }
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
            if (IsPositionConcealed(hollow.VisualCell)) continue;
            var origin = CellCenter(hollow.VisualCell);
            var viewDistance = HollowViewRange(hollow, hollow.HasSight);
            var fieldOfView = HollowFieldOfView(hollow, hollow.HasSight);
            var reach = viewDistance * _cellSize;
            if (!RectangleF.Inflate(_mazeRect, reach, reach).Contains(origin)) continue;

            var rayOffsets = BuildVisionRayOffsets(hollow, fieldOfView, viewDistance);
            var outer = new List<PointF>(rayOffsets.Count);
            var inner = new List<PointF>(rayOffsets.Count);
            foreach (var offset in rayOffsets)
            {
                var angle = hollow.FacingAngle + offset;
                var distance = RaycastVisionDistance(
                    hollow.VisualCell, angle, viewDistance, hollow.Type == HollowType.Hex);
                distance = Math.Max(innerRadius, distance);
                outer.Add(SnapToFeed(CellCenter(new PointF(
                    hollow.VisualCell.X + MathF.Cos(angle) * distance,
                    hollow.VisualCell.Y + MathF.Sin(angle) * distance))));
                inner.Add(SnapToFeed(CellCenter(new PointF(
                    hollow.VisualCell.X + MathF.Cos(angle) * innerRadius,
                    hollow.VisualCell.Y + MathF.Sin(angle) * innerRadius))));
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

    private List<float> BuildVisionRayOffsets(Hollow hollow, float fieldOfView, float viewDistance)
    {
        var rayCount = hollow.Type switch
        {
            HollowType.Square => 24,
            HollowType.Diamond => 40,
            _ => 18
        };
        var halfField = fieldOfView / 2;
        var offsets = new List<float>(rayCount + 48);
        for (var i = 0; i <= rayCount; i++)
            offsets.Add(-halfField + fieldOfView * i / rayCount);

        if (_maze is not null && hollow.Type != HollowType.Hex)
        {
            const float edgeEpsilon = .0015f;
            var minGridX = Math.Max(0, (int)MathF.Floor(hollow.VisualCell.X - viewDistance + .5f));
            var maxGridX = Math.Min(_maze.Width, (int)MathF.Ceiling(hollow.VisualCell.X + viewDistance + .5f));
            var minGridY = Math.Max(0, (int)MathF.Floor(hollow.VisualCell.Y - viewDistance + .5f));
            var maxGridY = Math.Min(_maze.Height, (int)MathF.Ceiling(hollow.VisualCell.Y + viewDistance + .5f));
            var maximumSquared = (viewDistance + .05f) * (viewDistance + .05f);
            for (var gridX = minGridX; gridX <= maxGridX; gridX++)
            for (var gridY = minGridY; gridY <= maxGridY; gridY++)
            {
                var dx = gridX - .5f - hollow.VisualCell.X;
                var dy = gridY - .5f - hollow.VisualCell.Y;
                var distanceSquared = dx * dx + dy * dy;
                if (distanceSquared < .01f || distanceSquared > maximumSquared) continue;
                var relative = NormalizeAngle(MathF.Atan2(dy, dx) - hollow.FacingAngle);
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
