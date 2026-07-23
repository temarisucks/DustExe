using System.Drawing.Drawing2D;

namespace Dust;

internal sealed partial class GameForm
{
    private void DrawDrone(Graphics g, DroneModel model, Color coreColor, Color frameColor, PointF center, float radius,
        int alpha, bool drawShadow, bool drawBrackets, float bank = 0, float pitch = 0,
        bool showDamage = false, int? damageOverride = null, int? maximumHealthOverride = null)
    {
        var r = Math.Max(9, (int)radius);
        bank = Math.Clamp(bank, -1, 1);
        pitch = Math.Clamp(pitch, -1, 1);
        if (drawShadow) DrawDroneGroundShadow(g, center, r, bank, pitch, alpha);
        center.Y += DroneFloatOffset(model, bank, pitch);

        var maximumHealth = Math.Max(1, maximumHealthOverride ?? GetMaximumHealth());
        var damage = showDamage
            ? Math.Clamp(damageOverride ?? _damageTaken, 0, maximumHealth)
            : 0;
        var damageRatio = damage / (float)maximumHealth;
        if (damage > 0)
        {
            coreColor = DamageTint(coreColor, C.Ink, damageRatio * .66f);
            frameColor = DamageTint(frameColor, C.Oxide, damageRatio * .72f);
        }

        using var silhouette = BuildDroneLayer(model, DroneLayer.Silhouette, center, r, bank, pitch);
        using var frame = BuildDroneLayer(model, DroneLayer.Frame, center, r, bank, pitch);
        using var coating = BuildDroneLayer(model, DroneLayer.Coating, center, r, bank, pitch);
        using var silhouetteBrush = new SolidBrush(ScaleAlpha(C.Ink, alpha));
        using var frameBrush = new SolidBrush(ScaleAlpha(frameColor, alpha));
        using var coatingBrush = new SolidBrush(ScaleAlpha(coreColor, alpha));
        g.FillPath(silhouetteBrush, silhouette);
        g.FillPath(frameBrush, frame);
        g.FillPath(coatingBrush, coating);

        if (damage > 0)
            DrawDroneDamage(g, model, center, r, bank, pitch, alpha, damage);

        if (drawBrackets && alpha > 150)
            DrawTrackingBrackets(g, center, r + Math.Max(2, r / 6) * 2,
                damage >= maximumHealth - 1 ? C.Red : C.Signal);
    }

    private void DrawDroneDamage(Graphics g, DroneModel model, PointF center, float radius,
        float bank, float pitch, int alpha, int damage)
    {
        PointF[] wounds =
        [
            new(-.42f, -.30f), new(.37f, .25f), new(.04f, -.08f),
            new(-.27f, .42f), new(.43f, -.27f)
        ];
        PointF[] crackEnds =
        [
            new(-.05f, .04f), new(.05f, -.15f), new(.31f, .18f),
            new(-.02f, .14f), new(.09f, -.02f)
        ];

        using var silhouette = BuildDroneLayer(model, DroneLayer.Silhouette, center, radius, bank, pitch);
        var clipped = g.Save();
        g.SetClip(silhouette, CombineMode.Intersect);
        using var dead = new SolidBrush(ScaleAlpha(Color.FromArgb(5, 9, 9), alpha));
        using var exposed = new SolidBrush(ScaleAlpha(Color.FromArgb(117, 57, 44), alpha));
        using var fracture = new Pen(ScaleAlpha(Color.FromArgb(226, 178, 112), alpha), 2);
        for (var i = 0; i < damage; i++)
        {
            var wound = wounds[i % wounds.Length];
            var end = crackEnds[i % crackEnds.Length];
            var p = TransformDronePoint(wound, center, radius, bank, pitch);
            var p1 = TransformDronePoint(new PointF(wound.X + end.X * .46f, wound.Y + end.Y * .46f),
                center, radius, bank, pitch);
            var p2 = TransformDronePoint(new PointF(wound.X + end.X, wound.Y + end.Y),
                center, radius, bank, pitch);
            var chip = Math.Max(5, (int)(radius * .25f));
            g.FillRectangle(dead, MathF.Round(p.X - chip / 2f), MathF.Round(p.Y - chip / 2f), chip, chip);
            g.FillRectangle(exposed, MathF.Round(p.X - chip / 2f), MathF.Round(p.Y - 1), chip, 2);
            g.DrawLines(fracture, [p, p1, p2]);
        }
        g.Restore(clipped);

        // The damaged frame occasionally spits a tiny, deterministic electrical arc.
        // This reads as a fault in the airframe rather than decorative drone detail.
        if (damage >= 2 && ((int)(_time * 9) + damage) % 5 < 2)
        {
            var source = TransformDronePoint(wounds[(damage - 1) % wounds.Length], center, radius, bank, pitch);
            using var spark = new SolidBrush(ScaleAlpha(C.Signal, alpha));
            using var hot = new SolidBrush(ScaleAlpha(C.Bone, alpha));
            var direction = ((int)(_time * 18) & 1) == 0 ? 1 : -1;
            g.FillRectangle(spark, source.X + direction * 4, source.Y - 7, 3, 3);
            g.FillRectangle(hot, source.X + direction * 8, source.Y - 12, 3, 3);
        }
    }

    private static Color DamageTint(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(from.A,
            (int)(from.R + (to.R - from.R) * amount),
            (int)(from.G + (to.G - from.G) * amount),
            (int)(from.B + (to.B - from.B) * amount));
    }

    private float DroneFloatOffset(DroneModel model, float bank = 0, float pitch = 0)
    {
        var idle = 1f - Math.Clamp(Math.Abs(bank) + Math.Abs(pitch), 0, 1);
        return MathF.Sin(_time * 2.45f + (int)model * .83f) * 5.2f * idle;
    }

    private static GraphicsPath BuildDroneLayer(DroneModel model, DroneLayer layer, PointF center,
        float radius, float bank, float pitch)
    {
        var path = new GraphicsPath(FillMode.Winding);
        switch (model)
        {
            case DroneModel.Mite:
                BuildMiteLayer(path, layer, center, radius, bank, pitch);
                break;
            case DroneModel.Kite:
                BuildKiteLayer(path, layer, center, radius, bank, pitch);
                break;
            case DroneModel.Triad:
                BuildTriadLayer(path, layer, center, radius, bank, pitch);
                break;
            case DroneModel.Cicada:
                BuildCicadaLayer(path, layer, center, radius, bank, pitch);
                break;
            case DroneModel.Cradle:
                BuildCradleLayer(path, layer, center, radius, bank, pitch);
                break;
            default:
                BuildMiteLayer(path, layer, center, radius, bank, pitch);
                break;
        }
        return path;
    }

    private static void BuildMiteLayer(GraphicsPath path, DroneLayer layer, PointF center,
        float radius, float bank, float pitch)
    {
        if (layer == DroneLayer.Silhouette)
        {
            AddDroneBar(path, new PointF(-.72f, 0), new PointF(.72f, 0), .36f, center, radius, bank, pitch);
            AddDroneRegular(path, new PointF(-.72f, 0), 8, .30f, MathF.PI / 8, center, radius, bank, pitch);
            AddDroneRegular(path, new PointF(.72f, 0), 8, .30f, MathF.PI / 8, center, radius, bank, pitch);
            AddDroneCutBox(path, PointF.Empty, .68f, .82f, .16f, center, radius, bank, pitch);
        }
        else if (layer == DroneLayer.Frame)
        {
            AddDroneBar(path, new PointF(-.70f, 0), new PointF(.70f, 0), .18f, center, radius, bank, pitch);
            AddDroneRegular(path, new PointF(-.72f, 0), 8, .22f, MathF.PI / 8, center, radius, bank, pitch);
            AddDroneRegular(path, new PointF(.72f, 0), 8, .22f, MathF.PI / 8, center, radius, bank, pitch);
            AddDroneCutBox(path, PointF.Empty, .54f, .68f, .12f, center, radius, bank, pitch);
        }
        else
        {
            AddDroneCutBox(path, PointF.Empty, .42f, .56f, .10f, center, radius, bank, pitch);
        }
    }

    private static void BuildKiteLayer(GraphicsPath path, DroneLayer layer, PointF center,
        float radius, float bank, float pitch)
    {
        var scale = layer == DroneLayer.Silhouette ? 1f : layer == DroneLayer.Frame ? .82f : .55f;
        PointF[] points = layer == DroneLayer.Coating
            ?
            [
                new(0, -.72f), new(.25f, -.30f), new(.20f, .50f),
                new(0, .70f), new(-.20f, .50f), new(-.25f, -.30f)
            ]
            :
            [
                new(0, -1), new(.20f, -.52f), new(.88f, -.18f), new(.96f, .08f),
                new(.30f, .30f), new(.18f, .78f), new(0, .94f), new(-.18f, .78f),
                new(-.30f, .30f), new(-.96f, .08f), new(-.88f, -.18f), new(-.20f, -.52f)
            ];
        if (layer != DroneLayer.Coating)
            for (var i = 0; i < points.Length; i++) points[i] = new PointF(points[i].X * scale, points[i].Y * scale);
        AddDronePolygon(path, points, center, radius, bank, pitch);
    }

    private static void BuildTriadLayer(GraphicsPath path, DroneLayer layer, PointF center,
        float radius, float bank, float pitch)
    {
        var top = new PointF(0, -.67f);
        var lowerRight = new PointF(.61f, .39f);
        var lowerLeft = new PointF(-.61f, .39f);
        if (layer == DroneLayer.Silhouette)
        {
            AddDroneBar(path, PointF.Empty, top, .32f, center, radius, bank, pitch);
            AddDroneBar(path, PointF.Empty, lowerRight, .32f, center, radius, bank, pitch);
            AddDroneBar(path, PointF.Empty, lowerLeft, .32f, center, radius, bank, pitch);
            AddDroneRegular(path, top, 6, .29f, MathF.PI / 6, center, radius, bank, pitch);
            AddDroneRegular(path, lowerRight, 6, .29f, MathF.PI / 6, center, radius, bank, pitch);
            AddDroneRegular(path, lowerLeft, 6, .29f, MathF.PI / 6, center, radius, bank, pitch);
            AddDroneRegular(path, PointF.Empty, 6, .47f, -MathF.PI / 2, center, radius, bank, pitch);
        }
        else if (layer == DroneLayer.Frame)
        {
            AddDroneBar(path, PointF.Empty, top, .17f, center, radius, bank, pitch);
            AddDroneBar(path, PointF.Empty, lowerRight, .17f, center, radius, bank, pitch);
            AddDroneBar(path, PointF.Empty, lowerLeft, .17f, center, radius, bank, pitch);
            AddDroneRegular(path, top, 6, .21f, MathF.PI / 6, center, radius, bank, pitch);
            AddDroneRegular(path, lowerRight, 6, .21f, MathF.PI / 6, center, radius, bank, pitch);
            AddDroneRegular(path, lowerLeft, 6, .21f, MathF.PI / 6, center, radius, bank, pitch);
            AddDroneRegular(path, PointF.Empty, 6, .37f, -MathF.PI / 2, center, radius, bank, pitch);
        }
        else
        {
            AddDroneRegular(path, PointF.Empty, 6, .30f, -MathF.PI / 2, center, radius, bank, pitch);
        }
    }

    private static void BuildCicadaLayer(GraphicsPath path, DroneLayer layer, PointF center,
        float radius, float bank, float pitch)
    {
        var leftPod = new PointF(-.72f, .18f);
        var rightPod = new PointF(.72f, .18f);
        if (layer == DroneLayer.Silhouette)
        {
            // A long instrument body with two swept lift assemblies. Its narrow
            // profile stays recognizable at both field and menu scale.
            AddDroneBar(path, new PointF(-.08f, -.26f), leftPod, .31f,
                center, radius, bank, pitch);
            AddDroneBar(path, new PointF(.08f, -.26f), rightPod, .31f,
                center, radius, bank, pitch);
            AddDroneRegular(path, leftPod, 8, .29f, MathF.PI / 8,
                center, radius, bank, pitch);
            AddDroneRegular(path, rightPod, 8, .29f, MathF.PI / 8,
                center, radius, bank, pitch);
            AddDroneCutBox(path, new PointF(0, -.03f), .47f, 1.62f, .13f,
                center, radius, bank, pitch);
            AddDroneBar(path, new PointF(-.39f, .62f), new PointF(.39f, .62f), .19f,
                center, radius, bank, pitch);
        }
        else if (layer == DroneLayer.Frame)
        {
            AddDroneBar(path, new PointF(-.06f, -.24f), leftPod, .17f,
                center, radius, bank, pitch);
            AddDroneBar(path, new PointF(.06f, -.24f), rightPod, .17f,
                center, radius, bank, pitch);
            AddDroneRegular(path, leftPod, 8, .21f, MathF.PI / 8,
                center, radius, bank, pitch);
            AddDroneRegular(path, rightPod, 8, .21f, MathF.PI / 8,
                center, radius, bank, pitch);
            AddDroneCutBox(path, new PointF(0, -.03f), .34f, 1.39f, .10f,
                center, radius, bank, pitch);
            AddDroneBar(path, new PointF(-.35f, .62f), new PointF(.35f, .62f), .10f,
                center, radius, bank, pitch);
        }
        else
        {
            AddDroneCutBox(path, new PointF(0, -.11f), .23f, .94f, .07f,
                center, radius, bank, pitch);
        }
    }

    private static void BuildCradleLayer(GraphicsPath path, DroneLayer layer, PointF center,
        float radius, float bank, float pitch)
    {
        PointF[] pods =
        [
            new(-.61f, -.50f), new(.61f, -.50f),
            new(.61f, .50f), new(-.61f, .50f)
        ];

        if (layer == DroneLayer.Silhouette)
        {
            foreach (var pod in pods)
            {
                AddDroneBar(path, PointF.Empty, pod, .30f, center, radius, bank, pitch);
                AddDroneRegular(path, pod, 8, .27f, MathF.PI / 8, center, radius, bank, pitch);
            }
            AddDroneCutBox(path, PointF.Empty, .70f, .70f, .14f,
                center, radius, bank, pitch);
        }
        else if (layer == DroneLayer.Frame)
        {
            foreach (var pod in pods)
            {
                AddDroneBar(path, PointF.Empty, pod, .16f, center, radius, bank, pitch);
                AddDroneRegular(path, pod, 8, .19f, MathF.PI / 8, center, radius, bank, pitch);
            }
            AddDroneCutBox(path, PointF.Empty, .55f, .55f, .11f,
                center, radius, bank, pitch);
        }
        else
        {
            AddDroneCutBox(path, PointF.Empty, .38f, .38f, .08f,
                center, radius, bank, pitch);
        }
    }

    private static void AddDroneBar(GraphicsPath path, PointF from, PointF to, float width,
        PointF center, float radius, float bank, float pitch)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= .0001f) return;
        var px = -dy / length * width / 2;
        var py = dx / length * width / 2;
        AddDronePolygon(path,
        [
            new(from.X + px, from.Y + py), new(to.X + px, to.Y + py),
            new(to.X - px, to.Y - py), new(from.X - px, from.Y - py)
        ], center, radius, bank, pitch);
    }

    private static void AddDroneRegular(GraphicsPath path, PointF localCenter, int sides, float localRadius,
        float rotation, PointF center, float radius, float bank, float pitch)
    {
        var points = new PointF[sides];
        for (var i = 0; i < sides; i++)
        {
            var angle = rotation + i * MathF.PI * 2 / sides;
            points[i] = new PointF(localCenter.X + MathF.Cos(angle) * localRadius,
                localCenter.Y + MathF.Sin(angle) * localRadius);
        }
        AddDronePolygon(path, points, center, radius, bank, pitch);
    }

    private static void AddDroneCutBox(GraphicsPath path, PointF localCenter, float width, float height,
        float cut, PointF center, float radius, float bank, float pitch)
    {
        var halfWidth = width / 2;
        var halfHeight = height / 2;
        AddDronePolygon(path,
        [
            new(localCenter.X - halfWidth + cut, localCenter.Y - halfHeight),
            new(localCenter.X + halfWidth - cut, localCenter.Y - halfHeight),
            new(localCenter.X + halfWidth, localCenter.Y - halfHeight + cut),
            new(localCenter.X + halfWidth, localCenter.Y + halfHeight - cut),
            new(localCenter.X + halfWidth - cut, localCenter.Y + halfHeight),
            new(localCenter.X - halfWidth + cut, localCenter.Y + halfHeight),
            new(localCenter.X - halfWidth, localCenter.Y + halfHeight - cut),
            new(localCenter.X - halfWidth, localCenter.Y - halfHeight + cut)
        ], center, radius, bank, pitch);
    }

    private static void AddDronePolygon(GraphicsPath path, PointF[] localPoints, PointF center,
        float radius, float bank, float pitch)
    {
        var points = new PointF[localPoints.Length];
        for (var i = 0; i < localPoints.Length; i++)
            points[i] = TransformDronePoint(localPoints[i], center, radius, bank, pitch);
        path.AddPolygon(points);
    }

    private static PointF TransformDronePoint(PointF point, PointF center, float radius, float bank, float pitch)
    {
        var x = point.X * (1 - Math.Abs(bank) * .12f) + point.Y * pitch * .14f;
        var y = point.Y * (1 - Math.Abs(pitch) * .12f) + point.X * bank * .14f;
        var localX = MathF.Round((x * radius + bank * 2.2f) / 2) * 2;
        var localY = MathF.Round((y * radius + pitch * 2.2f) / 2) * 2;
        return new PointF(center.X + localX, center.Y + localY);
    }

    private static void DrawDroneGroundShadow(Graphics g, PointF center, float radius,
        float bank, float pitch, int renderAlpha)
    {
        var width = radius * (1.38f - Math.Abs(bank) * .08f);
        var height = Math.Max(3, radius * .16f);
        using var shadow = new SolidBrush(Color.FromArgb(Math.Clamp(renderAlpha * 72 / 255, 0, 72), 0, 0, 0));
        g.FillRectangle(shadow,
            MathF.Round(center.X - width / 2 - bank * 2),
            MathF.Round(center.Y + radius * .58f - pitch * 2),
            MathF.Round(width), MathF.Round(height));
    }

    private static Color ScaleAlpha(Color color, int renderAlpha)
    {
        var alpha = Math.Clamp(color.A * Math.Clamp(renderAlpha, 0, 255) / 255, 0, 255);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static void DrawTrackingBrackets(Graphics g, PointF center, float radius, Color color)
    {
        using var brush = new SolidBrush(color);
        const int thickness = 3;
        const int length = 10;
        var left = center.X - radius;
        var top = center.Y - radius;
        var right = center.X + radius;
        var bottom = center.Y + radius;
        g.FillRectangle(brush, left, top, length, thickness); g.FillRectangle(brush, left, top, thickness, length);
        g.FillRectangle(brush, right - length, top, length, thickness); g.FillRectangle(brush, right - thickness, top, thickness, length);
        g.FillRectangle(brush, left, bottom - thickness, length, thickness); g.FillRectangle(brush, left, bottom - length, thickness, length);
        g.FillRectangle(brush, right - length, bottom - thickness, length, thickness); g.FillRectangle(brush, right - thickness, bottom - length, thickness, length);
    }
}
