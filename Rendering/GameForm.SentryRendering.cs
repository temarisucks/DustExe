using System.Drawing.Drawing2D;

namespace Dust;

internal sealed partial class GameForm
{
    private void DrawSentryVision(Graphics g)
    {
        if (_maze is null || _cellSize <= 0) return;
        using var scanningFields = new GraphicsPath(FillMode.Winding);
        using var firingFields = new GraphicsPath(FillMode.Winding);

        foreach (var sentry in _sentries)
        {
            if (sentry.Phase != SentryPhase.Scanning) continue;
            if (IsCellConcealed(sentry.Cell)) continue;
            var origin = new PointF(sentry.Cell.X, sentry.Cell.Y);
            var screenOrigin = CellCenter(origin);
            var viewDistance = SentryViewDistanceFor(sentry);
            var fieldOfView = SentryFieldOfViewFor(sentry);
            var renderFacing = SentryRenderFacing(sentry);
            var reach = viewDistance * _cellSize;
            if (!RectangleF.Inflate(_mazeRect, reach, reach).Contains(screenOrigin)) continue;

            const float innerRadius = .34f;
            var rayOffsets = BuildSentryVisionRayOffsets(
                origin, renderFacing, viewDistance, fieldOfView, sentry.Empowered);
            var outer = new PointF[rayOffsets.Count];
            var inner = new PointF[rayOffsets.Count];
            for (var index = 0; index < rayOffsets.Count; index++)
            {
                var offset = rayOffsets[index];
                var angle = renderFacing + offset;
                var distance = RaycastVisionDistance(
                    origin, angle, viewDistance, ignoreWalls: sentry.Empowered);
                distance = Math.Max(innerRadius, distance);
                outer[index] = SentrySnapToFeed(CellCenter(new PointF(
                    origin.X + MathF.Cos(angle) * distance,
                    origin.Y + MathF.Sin(angle) * distance)));
                inner[index] = SentrySnapToFeed(CellCenter(new PointF(
                    origin.X + MathF.Cos(angle) * innerRadius,
                    origin.Y + MathF.Sin(angle) * innerRadius)));
            }

            var field = outer.Concat(inner.Reverse()).ToArray();
            (sentry.HasSight ? firingFields : scanningFields).AddPolygon(field);

            var centerDistance = RaycastVisionDistance(
                origin, renderFacing, viewDistance, ignoreWalls: sentry.Empowered);
            var beamEnd = CellCenter(new PointF(
                origin.X + MathF.Cos(renderFacing) * centerDistance,
                origin.Y + MathF.Sin(renderFacing) * centerDistance));
            using var scanLine = new Pen(Color.FromArgb(
                sentry.HasSight ? 82 : 54,
                sentry.HasSight ? C.Red : C.Sick), 2);
            g.DrawLine(scanLine, screenOrigin, SentrySnapToFeed(beamEnd));
        }

        using var scanningExposure = new SolidBrush(Color.FromArgb(22, C.Sick));
        using var firingExposure = new SolidBrush(Color.FromArgb(34, C.Red));
        if (scanningFields.PointCount > 0) g.FillPath(scanningExposure, scanningFields);
        if (firingFields.PointCount > 0) g.FillPath(firingExposure, firingFields);
    }

    private List<float> BuildSentryVisionRayOffsets(
        PointF origin,
        float facingAngle,
        float viewDistance,
        float fieldOfView,
        bool ignoreWalls)
    {
        const int rayCount = 32;
        const float edgeEpsilon = .0015f;
        var halfField = fieldOfView / 2;
        var offsets = new List<float>(rayCount + 49);
        for (var index = 0; index <= rayCount; index++)
            offsets.Add(-halfField + fieldOfView * index / rayCount);

        if (_maze is not null && !ignoreWalls)
        {
            var minGridX = Math.Max(0, (int)MathF.Floor(origin.X - viewDistance + .5f));
            var maxGridX = Math.Min(_maze.Width, (int)MathF.Ceiling(origin.X + viewDistance + .5f));
            var minGridY = Math.Max(0, (int)MathF.Floor(origin.Y - viewDistance + .5f));
            var maxGridY = Math.Min(_maze.Height, (int)MathF.Ceiling(origin.Y + viewDistance + .5f));
            var maximumSquared = (viewDistance + .05f) * (viewDistance + .05f);
            for (var gridX = minGridX; gridX <= maxGridX; gridX++)
            for (var gridY = minGridY; gridY <= maxGridY; gridY++)
            {
                var dx = gridX - .5f - origin.X;
                var dy = gridY - .5f - origin.Y;
                var distanceSquared = dx * dx + dy * dy;
                if (distanceSquared < .01f || distanceSquared > maximumSquared) continue;
                var relative = NormalizeAngle(MathF.Atan2(dy, dx) - facingAngle);
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

    private void DrawSentries(Graphics g)
    {
        if (_maze is null || _cellSize <= 0) return;
        foreach (var sentry in _sentries)
        {
            if (IsCellConcealed(sentry.Cell)) continue;
            if (sentry.Phase == SentryPhase.Buried)
            {
                DrawBuriedSentryTransit(g, sentry);
                continue;
            }

            var center = CellCenter(sentry.Cell);
            var fullRadius = _cellSize * .31f;
            if (!RectangleF.Inflate(_mazeRect, fullRadius * 2, fullRadius * 2).Contains(center))
                continue;

            var depth = SentryBurrowDepth(sentry);
            DrawSentryAperture(g, center, fullRadius, .35f + depth * .65f,
                sentry.Phase == SentryPhase.Emerging);
            if (depth >= .985f) continue;

            var bodyAlpha = Math.Clamp((int)((1 - depth) * 255), 0, 255);
            var radius = fullRadius * (1 - depth * .68f);
            var bodyCenter = new PointF(center.X, center.Y + depth * fullRadius * .18f);
            DrawSentryBody(g, sentry, bodyCenter, radius, bodyAlpha);
        }

        DrawSentryProjectiles(g);
    }

    private void DrawSentryBody(Graphics g, Sentry sentry, PointF center, float radius, int alpha)
    {
        if (alpha <= 4 || radius <= 2) return;
        var signalBase = sentry.Empowered
            ? EnemyEmpowermentColor(sentry.AnimationPhase)
            : sentry.HasSight ? C.Red : C.Sick;
        var signal = Color.FromArgb(alpha, signalBase);
        var dark = Color.FromArgb(alpha, 3, 7, 7);
        var ceramic = Color.FromArgb(alpha, C.Bone);

        // Four sunk restraints make the sentry read as installed machinery rather
        // than another roaming Hollow. The center remains visibly empty.
        using var restraintKey = new SolidBrush(dark);
        using var restraintFace = new SolidBrush(Color.FromArgb(alpha, C.Steel));
        for (var index = 0; index < 4; index++)
        {
            var angle = index * MathF.PI / 2;
            var direction = new PointF(MathF.Cos(angle), MathF.Sin(angle));
            var tangent = new PointF(-direction.Y, direction.X);
            var anchor = new PointF(
                center.X + direction.X * radius * .91f,
                center.Y + direction.Y * radius * .91f);
            var halfAlong = radius * .22f;
            var halfAcross = radius * .13f;
            var plate = new PointF[]
            {
                new(anchor.X + direction.X * halfAlong + tangent.X * halfAcross,
                    anchor.Y + direction.Y * halfAlong + tangent.Y * halfAcross),
                new(anchor.X + direction.X * halfAlong - tangent.X * halfAcross,
                    anchor.Y + direction.Y * halfAlong - tangent.Y * halfAcross),
                new(anchor.X - direction.X * halfAlong - tangent.X * halfAcross,
                    anchor.Y - direction.Y * halfAlong - tangent.Y * halfAcross),
                new(anchor.X - direction.X * halfAlong + tangent.X * halfAcross,
                    anchor.Y - direction.Y * halfAlong + tangent.Y * halfAcross)
            };
            g.FillPolygon(restraintKey, plate);
            var inset = SentryInsetPolygon(plate, .70f);
            g.FillPolygon(restraintFace, inset);
        }

        var outer = SentryPolygon(center, 8, radius * .78f, MathF.PI / 8);
        var innerRotation = -_time * sentry.RotationDirection * .84f + sentry.AnimationPhase;
        var inner = SentryPolygon(center, 4, radius * .45f, MathF.PI / 4 + innerRotation);
        DrawSentryRing(g, outer, signal, alpha, 10, 4);
        DrawSentryRing(g, inner, Color.FromArgb(Math.Min(alpha, 205), ceramic), alpha, 8, 3);

        var voidRadius = radius * .21f;
        using var voidBrush = new SolidBrush(dark);
        g.FillPolygon(voidBrush, SentryPolygon(center, 8, voidRadius, MathF.PI / 8));

        // The offset sight-bar is the readable facing indicator and muzzle.
        var renderFacing = SentryRenderFacing(sentry);
        var directionX = MathF.Cos(renderFacing);
        var directionY = MathF.Sin(renderFacing);
        var tangentX = -directionY;
        var tangentY = directionX;
        var rear = radius * .12f;
        var front = radius * .70f;
        var halfWidth = Math.Max(2, radius * .075f);
        var sightBar = new PointF[]
        {
            new(center.X - directionX * rear + tangentX * halfWidth,
                center.Y - directionY * rear + tangentY * halfWidth),
            new(center.X + directionX * front + tangentX * halfWidth,
                center.Y + directionY * front + tangentY * halfWidth),
            new(center.X + directionX * front - tangentX * halfWidth,
                center.Y + directionY * front - tangentY * halfWidth),
            new(center.X - directionX * rear - tangentX * halfWidth,
                center.Y - directionY * rear - tangentY * halfWidth)
        };
        using var sightKey = new Pen(dark, 7) { LineJoin = LineJoin.Miter };
        using var sightSignal = new Pen(signal, 3) { LineJoin = LineJoin.Miter };
        g.DrawPolygon(sightKey, sightBar);
        g.DrawPolygon(sightSignal, sightBar);

        if (sentry.MuzzleFlash <= 0) return;
        var flash = sentry.MuzzleFlash / .16f;
        var muzzleCenter = new PointF(
            center.X + directionX * radius * .88f,
            center.Y + directionY * radius * .88f);
        var flashLength = radius * (.28f + flash * .38f);
        var flashWidth = radius * .18f * flash;
        var flare = new PointF[]
        {
            new(muzzleCenter.X + directionX * flashLength, muzzleCenter.Y + directionY * flashLength),
            new(muzzleCenter.X + tangentX * flashWidth, muzzleCenter.Y + tangentY * flashWidth),
            new(muzzleCenter.X - tangentX * flashWidth, muzzleCenter.Y - tangentY * flashWidth)
        };
        using var flareKey = new SolidBrush(Color.FromArgb((int)(220 * flash), C.Ink));
        using var flareSignal = new SolidBrush(Color.FromArgb((int)(255 * flash), C.Signal));
        g.FillPolygon(flareKey, flare);
        g.FillPolygon(flareSignal, SentryInsetPolygon(flare, .58f));
    }

    private void DrawBuriedSentryTransit(Graphics g, Sentry sentry)
    {
        var progress = Math.Clamp(sentry.PhaseTimer / SentryBuriedDuration, 0, 1);
        var radius = _cellSize * .31f;
        if (progress < .62f)
        {
            var oldCenter = CellCenter(sentry.PreviousCell);
            DrawSentryAperture(g, oldCenter, radius, 1 - progress / .62f, emerging: false);
        }
        if (progress > .32f)
        {
            var newCenter = CellCenter(sentry.Cell);
            DrawSentryAperture(g, newCenter, radius, (progress - .32f) / .68f, emerging: true);
        }
    }

    private void DrawSentryAperture(Graphics g, PointF center, float radius, float openness, bool emerging)
    {
        openness = Math.Clamp(openness, 0, 1);
        if (openness <= .02f) return;
        var apertureRadius = radius * (.25f + openness * .64f);
        var outer = SentryPolygon(center, 8, apertureRadius, MathF.PI / 8);
        var inner = SentryPolygon(center, 8, apertureRadius * .72f, MathF.PI / 8);
        using var stain = new SolidBrush(Color.FromArgb((int)(125 * openness), C.Oxide));
        using var pit = new SolidBrush(Color.FromArgb((int)(235 * openness), 2, 5, 5));
        g.FillPolygon(stain, outer);
        g.FillPolygon(pit, inner);

        var pulse = .45f + .55f * MathF.Sin((_time * 11f + (emerging ? 1.7f : 0)));
        using var rim = new Pen(Color.FromArgb(
            (int)((50 + pulse * 65) * openness), emerging ? C.Signal : C.Steel), 2);
        g.DrawPolygon(rim, outer);
        using var slit = new SolidBrush(Color.FromArgb((int)(95 * openness), C.Signal));
        g.FillRectangle(slit, center.X - apertureRadius * .45f, center.Y - 1,
            apertureRadius * .9f, 2);
    }

    private void DrawSentryProjectiles(Graphics g)
    {
        foreach (var projectile in _sentryProjectiles)
        {
            var renderPosition = EnemyProjectileRenderPosition(projectile);
            if (IsPositionConcealed(renderPosition)) continue;
            var head = CellCenter(renderPosition);
            var velocityLength = MathF.Sqrt(
                projectile.Velocity.X * projectile.Velocity.X +
                projectile.Velocity.Y * projectile.Velocity.Y);
            if (velocityLength <= .001f) continue;
            var directionX = projectile.Velocity.X / velocityLength;
            var directionY = projectile.Velocity.Y / velocityLength;
            var tail = new PointF(
                head.X - directionX * _cellSize * .48f,
                head.Y - directionY * _cellSize * .48f);
            var flicker = ((projectile.Serial + (int)(_time * 60)) & 1) == 0;
            using var keyline = new Pen(Color.FromArgb(225, C.Ink), 10)
            {
                StartCap = LineCap.Square,
                EndCap = LineCap.Square
            };
            var projectileColor = projectile.Kind switch
            {
                EnemyProjectileKind.Triangle => flicker ? C.Red : C.Signal,
                EnemyProjectileKind.Star => EnemyEmpowermentColor(projectile.Serial * .37f),
                _ => flicker ? C.Signal : C.Red
            };
            using var signal = new Pen(projectileColor, 4)
            {
                StartCap = LineCap.Square,
                EndCap = LineCap.Square
            };
            g.DrawLine(keyline, tail, head);
            g.DrawLine(signal, tail, head);

            var headRadius = Math.Max(4, _cellSize * .075f);
            using var headKey = new SolidBrush(C.Ink);
            using var headSignal = new SolidBrush(
                projectile.Kind == EnemyProjectileKind.Star
                    ? projectileColor
                    : flicker ? C.Bone : C.Signal);
            var headShape = projectile.Kind switch
            {
                EnemyProjectileKind.Triangle => SentryPolygon(
                    head, 3, headRadius * 1.18f,
                    MathF.Atan2(directionY, directionX)),
                EnemyProjectileKind.Star => ProjectileStarPoints(
                    head, headRadius * 1.35f,
                    _time * 2.4f + projectile.Serial),
                _ => SentryPolygon(head, 4, headRadius, 0)
            };
            g.FillPolygon(headKey, headShape);
            g.FillPolygon(headSignal, SentryInsetPolygon(headShape, .56f));
        }
    }

    private static PointF[] ProjectileStarPoints(PointF center, float radius, float rotation)
    {
        var points = new PointF[10];
        for (var index = 0; index < points.Length; index++)
        {
            var pointRadius = index % 2 == 0 ? radius : radius * .43f;
            var angle = rotation + index * MathF.PI / 5;
            points[index] = new PointF(
                center.X + MathF.Cos(angle) * pointRadius,
                center.Y + MathF.Sin(angle) * pointRadius);
        }
        return points;
    }

    private static float SentryBurrowDepth(Sentry sentry)
    {
        var linear = sentry.Phase switch
        {
            SentryPhase.Submerging => Math.Clamp(sentry.PhaseTimer / SentrySubmergeDuration, 0, 1),
            SentryPhase.Buried => 1,
            SentryPhase.Emerging => 1 - Math.Clamp(sentry.PhaseTimer / SentryEmergeDuration, 0, 1),
            _ => 0
        };
        return linear * linear * (3 - 2 * linear);
    }

    private static void DrawSentryRing(Graphics g, PointF[] points, Color signal,
        int alpha, float keylineWidth, float signalWidth)
    {
        using var keyline = new Pen(Color.FromArgb(Math.Min(235, alpha), 3, 7, 7), keylineWidth)
        {
            LineJoin = LineJoin.Miter
        };
        using var marker = new Pen(signal, signalWidth)
        {
            LineJoin = LineJoin.Miter
        };
        g.DrawPolygon(keyline, points);
        g.DrawPolygon(marker, points);
    }

    private static PointF[] SentryPolygon(PointF center, int sides, float radius, float rotation)
    {
        var points = new PointF[sides];
        for (var index = 0; index < sides; index++)
        {
            var angle = rotation + index * MathF.PI * 2 / sides;
            points[index] = new PointF(
                MathF.Round((center.X + MathF.Cos(angle) * radius) / 2) * 2,
                MathF.Round((center.Y + MathF.Sin(angle) * radius) / 2) * 2);
        }
        return points;
    }

    private static PointF[] SentryInsetPolygon(PointF[] points, float scale)
    {
        var center = new PointF(
            points.Average(point => point.X),
            points.Average(point => point.Y));
        return points.Select(point => new PointF(
            center.X + (point.X - center.X) * scale,
            center.Y + (point.Y - center.Y) * scale)).ToArray();
    }

    private static PointF SentrySnapToFeed(PointF point) =>
        new(MathF.Round(point.X / 2) * 2, MathF.Round(point.Y / 2) * 2);
}
