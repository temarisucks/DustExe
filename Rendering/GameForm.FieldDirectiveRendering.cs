using System.Drawing.Drawing2D;

namespace Dust;

internal sealed partial class GameForm
{
    private void DrawFieldDirectiveNodes(Graphics g)
    {
        if (_maze is null) return;
        foreach (var directive in _fieldDirectives)
        foreach (var entry in directive.Nodes.Select((node, index) => (node, index)))
        {
            if (IsCellConcealed(entry.node.Cell) ||
                !IsWorldCellInRenderRange(entry.node.Cell))
                continue;
            DrawFieldDirectiveNode(g, directive, entry.node, entry.index);
        }
    }

    private void DrawFieldDirectiveNode(
        Graphics g,
        FieldDirective directive,
        FieldDirectiveNode node,
        int nodeIndex)
    {
        var pose = GetFieldDirectivePose(node);
        var center = PointF.Empty;
        var active = directive.IsNodeActive(nodeIndex);
        var owned = IsObjectiveAssignedToLocal(directive.AssignedPlayerId);
        var available = directive.CanActivate(nodeIndex);
        var frame = owned
            ? ((int)MathF.Floor(_time * 5 + node.Phase * 1.7f)) & 3
            : PositiveHash(directive.Id * 17 + node.Number * 7) & 3;
        var stateColor = active
            ? Color.FromArgb(76, 135, 91)
            : owned && available
                ? C.Signal
                : owned
                    ? C.Oxide
                    : Color.FromArgb(91, 93, 75);

        using var cable = new Pen(Color.FromArgb(88, 52, 42), 4)
        {
            StartCap = LineCap.Square,
            EndCap = LineCap.Square
        };

        var graphicsState = g.Save();
        ClipToCargoRoom(g, node.RoomId);
        g.TranslateTransform(pose.Center.X, pose.Center.Y);
        g.RotateTransform(pose.Rotation);
        g.DrawLine(cable, center.X, center.Y + 17, center.X, center.Y + 44);

        switch (directive.Kind)
        {
            case FieldDirectiveKind.ArchiveDecrypt:
                DrawArchiveDirective(g, center, stateColor, active, frame);
                break;
            case FieldDirectiveKind.PressurePurge:
                DrawPressureDirective(g, center, stateColor, active, frame);
                break;
            case FieldDirectiveKind.SignalCalibrate:
                DrawSignalDirective(g, center, stateColor, active, available, frame);
                break;
            default:
                DrawSpecimenDirective(g, center, stateColor, active, frame);
                break;
        }

        var prefix = directive.Kind switch
        {
            FieldDirectiveKind.ArchiveDecrypt => "A",
            FieldDirectiveKind.PressurePurge => "P",
            FieldDirectiveKind.SignalCalibrate => "S",
            _ => "C"
        };
        var ownerMark = owned ? null : DirectiveOwnerMark(directive.AssignedPlayerId);
        DrawDirectiveStatusPlate(g, center, $"{prefix}{node.Number}", ownerMark,
            stateColor, active, owned);
        g.Restore(graphicsState);
    }

    private RoomFixturePose GetFieldDirectivePose(FieldDirectiveNode node)
    {
        var pose = GetRoomFixturePose(node.Cell, node.WallSide);
        var room = _maze?.Rooms.FirstOrDefault(candidate =>
            candidate.Id == node.RoomId);
        return room is not null && RoomWallSides(room, node.Cell).Count == 0
            ? pose with { Center = CellCenter(node.Cell) }
            : pose;
    }

    private string DirectiveOwnerMark(string? ownerPlayerId)
    {
        var owner = ObjectiveOwnerName(ownerPlayerId);
        var words = owner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1)
            return string.Concat(words.Take(2).Select(word => word[0]));
        return owner.Length <= 2 ? owner : owner[..2];
    }

    private static void DrawArchiveDirective(
        Graphics g,
        PointF center,
        Color stateColor,
        bool active,
        int frame)
    {
        using var shadow = new SolidBrush(Color.FromArgb(205, C.Void));
        using var caseFill = new SolidBrush(Color.FromArgb(110, 111, 88));
        using var caseLight = new SolidBrush(Color.FromArgb(157, 151, 112));
        using var cavity = new SolidBrush(Color.FromArgb(8, 14, 14));
        using var state = new SolidBrush(stateColor);
        using var teeth = new SolidBrush(Color.FromArgb(66, 72, 61));
        g.FillRectangle(shadow, center.X - 21 + 4, center.Y - 27 + 5, 42, 55);
        g.FillPolygon(caseFill, new PointF[]
        {
            new(center.X - 22, center.Y - 24), new(center.X - 16, center.Y - 29),
            new(center.X + 20, center.Y - 29), new(center.X + 24, center.Y - 22),
            new(center.X + 24, center.Y + 25), new(center.X - 22, center.Y + 25)
        });
        g.FillRectangle(caseLight, center.X - 17, center.Y - 23, 35, 42);
        g.FillRectangle(cavity, center.X - 13, center.Y - 18, 27, 31);
        for (var row = 0; row < 4; row++)
        {
            var width = active ? 19 : 7 + ((row + frame) % 3) * 5;
            g.FillRectangle(state, center.X - 10, center.Y - 14 + row * 7, width, 3);
        }
        for (var tooth = 0; tooth < 4; tooth++)
            g.FillRectangle(teeth, center.X - 14 + tooth * 9, center.Y + 18, 5, 5);
        if (!active)
            g.FillRectangle(state, center.X + 17, center.Y - 21 + frame * 5, 3, 5);
    }

    private static void DrawPressureDirective(
        Graphics g,
        PointF center,
        Color stateColor,
        bool active,
        int frame)
    {
        using var shadow = new SolidBrush(Color.FromArgb(195, C.Void));
        using var pipe = new Pen(Color.FromArgb(131, 126, 96), 8);
        using var pipeLight = new Pen(Color.FromArgb(184, 174, 129), 2);
        using var wheel = new Pen(stateColor, 4);
        using var hub = new SolidBrush(Color.FromArgb(28, 35, 32));
        g.FillRectangle(shadow, center.X - 26 + 4, center.Y - 17 + 5, 52, 37);
        g.DrawLine(pipe, center.X - 27, center.Y + 5, center.X + 27, center.Y + 5);
        g.DrawLine(pipeLight, center.X - 27, center.Y + 1, center.X + 27, center.Y + 1);
        g.FillRectangle(hub, center.X - 9, center.Y - 5, 18, 21);
        var angleOffset = (active ? 0 : frame) * MathF.PI / 8;
        for (var spoke = 0; spoke < 8; spoke++)
        {
            var angle = angleOffset + spoke * MathF.PI / 4;
            g.DrawLine(wheel, center,
                new PointF(center.X + MathF.Cos(angle) * 18,
                    center.Y + MathF.Sin(angle) * 18));
        }
        g.DrawEllipse(wheel, center.X - 18, center.Y - 18, 36, 36);
        using var centerCap = new SolidBrush(stateColor);
        g.FillRectangle(centerCap, center.X - 5, center.Y - 5, 10, 10);
        if (!active && frame is 0 or 1)
        {
            using var vapor = new SolidBrush(Color.FromArgb(95, C.Bone));
            g.FillRectangle(vapor, center.X + 24, center.Y - 13 - frame * 4, 7, 5);
            g.FillRectangle(vapor, center.X + 30, center.Y - 19 - frame * 5, 9, 5);
        }
    }

    private static void DrawSignalDirective(
        Graphics g,
        PointF center,
        Color stateColor,
        bool active,
        bool available,
        int frame)
    {
        using var shadow = new SolidBrush(Color.FromArgb(200, C.Void));
        using var baseFill = new SolidBrush(Color.FromArgb(99, 102, 82));
        using var dark = new SolidBrush(Color.FromArgb(17, 25, 24));
        using var state = new SolidBrush(stateColor);
        using var mast = new Pen(Color.FromArgb(176, 167, 125), 4);
        g.FillRectangle(shadow, center.X - 25 + 4, center.Y + 13, 50, 19);
        g.FillPolygon(baseFill, new PointF[]
        {
            new(center.X - 25, center.Y + 11), new(center.X + 25, center.Y + 11),
            new(center.X + 19, center.Y + 27), new(center.X - 19, center.Y + 27)
        });
        g.FillRectangle(dark, center.X - 14, center.Y + 16, 28, 6);
        g.FillRectangle(state, center.X - 10, center.Y + 18, active ? 20 : 4 + frame * 5, 2);
        for (var mastIndex = -1; mastIndex <= 1; mastIndex++)
        {
            var top = center.Y - 18 - (mastIndex == 0 ? 9 : 0);
            g.DrawLine(mast, center.X + mastIndex * 13, center.Y + 12,
                center.X + mastIndex * 13, top);
            g.FillRectangle(state, center.X + mastIndex * 13 - 4, top - 4, 8, 8);
        }
        if (available && !active)
        {
            using var wave = new Pen(Color.FromArgb(100 + frame * 20, stateColor), 2);
            g.DrawArc(wave, center.X - 30 - frame * 3, center.Y - 34 - frame * 2,
                60 + frame * 6, 42 + frame * 4, 205, 130);
        }
    }

    private static void DrawSpecimenDirective(
        Graphics g,
        PointF center,
        Color stateColor,
        bool active,
        int frame)
    {
        using var shadow = new SolidBrush(Color.FromArgb(205, C.Void));
        using var shell = new SolidBrush(Color.FromArgb(122, 119, 91));
        using var rim = new SolidBrush(Color.FromArgb(177, 166, 121));
        using var glass = new SolidBrush(Color.FromArgb(15, 31, 30));
        using var specimen = new SolidBrush(Color.FromArgb(122, 42, 42, 34));
        using var clamp = new SolidBrush(stateColor);
        g.FillRectangle(shadow, center.X - 20 + 5, center.Y - 29 + 5, 40, 59);
        g.FillRectangle(shell, center.X - 20, center.Y - 29, 40, 57);
        g.FillRectangle(rim, center.X - 15, center.Y - 24, 30, 47);
        g.FillRectangle(glass, center.X - 11, center.Y - 20, 22, 37);
        var twitch = active ? 0 : frame is 0 or 3 ? -2 : 2;
        g.FillRectangle(specimen, center.X - 6 + twitch, center.Y - 10, 12, 18);
        g.FillRectangle(specimen, center.X - 9 - twitch, center.Y - 4, 18, 6);
        g.FillRectangle(clamp, center.X - 18, center.Y - 18, active ? 14 : 7, 6);
        g.FillRectangle(clamp, center.X + (active ? 4 : 11), center.Y - 18, active ? 14 : 7, 6);
        g.FillRectangle(clamp, center.X - 18, center.Y + 9, active ? 14 : 7, 6);
        g.FillRectangle(clamp, center.X + (active ? 4 : 11), center.Y + 9, active ? 14 : 7, 6);
        if (!active)
        {
            using var bubble = new SolidBrush(Color.FromArgb(115, C.Sick));
            g.FillRectangle(bubble, center.X + 4, center.Y + 8 - frame * 6, 3, 3);
        }
    }

    private static void DrawDirectiveStatusPlate(
        Graphics g,
        PointF center,
        string code,
        string? ownerMark,
        Color stateColor,
        bool active,
        bool owned)
    {
        var plateWidth = owned ? 44 : 66;
        var plate = new RectangleF(center.X - plateWidth / 2, center.Y + 31, plateWidth, 20);
        using var fill = new SolidBrush(Color.FromArgb(226, C.Ink));
        using var edge = new Pen(stateColor, owned ? 2 : 1);
        g.FillRectangle(fill, plate);
        g.DrawRectangle(edge, plate.X, plate.Y, plate.Width, plate.Height);
        var label = active ? $"{code}+" : code;
        if (!owned && !string.IsNullOrWhiteSpace(ownerMark))
            label = $"{label}/{ownerMark}";
        LabFont.Draw(g, label, center.X, plate.Y + 4, 1,
            stateColor, LabTextAlign.Center, 0);
    }

}
