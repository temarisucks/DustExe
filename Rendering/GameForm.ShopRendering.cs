using System.Drawing.Drawing2D;

namespace Dust;

internal sealed partial class GameForm
{
    private void DrawCargoRoomContents(Graphics g)
    {
        // Storage dressing is deliberately split into a floor pass and a fixture
        // pass. The first pass makes every object feel installed in the room; the
        // clear center of each footprint also communicates that the dressing is
        // scenery, not hidden collision.
        foreach (var prop in _roomProps)
        {
            if (IsCellConcealed(prop.Cell) || !IsWorldCellInRenderRange(prop.Cell)) continue;
            DrawRoomPropFloorMark(g, prop);
        }
        if (_shopKiosk is not null && !IsCellConcealed(_shopKiosk.Cell) &&
            IsWorldCellInRenderRange(_shopKiosk.Cell, 3f))
            DrawShopKioskFloor(g, _shopKiosk);

        foreach (var prop in _roomProps)
        {
            if (IsCellConcealed(prop.Cell) || !IsWorldCellInRenderRange(prop.Cell)) continue;
            DrawRoomProp(g, prop);
        }
        foreach (var salvage in _roomSalvage)
        {
            if (salvage.Collected || salvage.Sold || IsCellConcealed(salvage.Cell) ||
                !IsWorldCellInRenderRange(salvage.Cell))
                continue;
            DrawRoomSalvage(g, salvage);
        }
        if (_shopKiosk is not null && !IsCellConcealed(_shopKiosk.Cell) &&
            IsWorldCellInRenderRange(_shopKiosk.Cell, 3f))
            DrawShopKiosk(g, _shopKiosk);
    }

    private readonly record struct RoomPropPose(PointF Center, float Rotation);

    private RoomPropPose GetRoomPropPose(RoomProp prop)
    {
        var center = CellCenter(prop.Cell);
        var room = _maze?.GetRoomAt(prop.Cell);
        if (room is null) return new RoomPropPose(center, 0);

        var outward = new List<Point>(4);
        if (!room.Contains(new Point(prop.Cell.X, prop.Cell.Y - 1))) outward.Add(new Point(0, -1));
        if (!room.Contains(new Point(prop.Cell.X + 1, prop.Cell.Y))) outward.Add(new Point(1, 0));
        if (!room.Contains(new Point(prop.Cell.X, prop.Cell.Y + 1))) outward.Add(new Point(0, 1));
        if (!room.Contains(new Point(prop.Cell.X - 1, prop.Cell.Y))) outward.Add(new Point(-1, 0));
        if (outward.Count == 0) return new RoomPropPose(center, 0);

        var anchor = outward[prop.Variant % outward.Count];
        var offset = _cellSize * .43f;
        center = new PointF(center.X + anchor.X * offset, center.Y + anchor.Y * offset);
        var rotation = anchor switch
        {
            { X: 1 } => 90f,
            { Y: 1 } => 180f,
            { X: -1 } => 270f,
            _ => 0f
        };
        return new RoomPropPose(center, rotation);
    }

    private int RoomPropFrame(RoomProp prop, float rate, int frameCount)
    {
        var offset = PositiveHash(prop.RoomId * 41 + prop.Cell.X * 13 + prop.Cell.Y * 7 +
                                  prop.Variant * 19) % Math.Max(1, frameCount);
        return PositiveHash((int)MathF.Floor(_time * rate) + offset) % Math.Max(1, frameCount);
    }

    private void DrawRoomPropFloorMark(Graphics g, RoomProp prop)
    {
        var pose = GetRoomPropPose(prop);
        var state = g.Save();
        g.TranslateTransform(pose.Center.X, pose.Center.Y);
        g.RotateTransform(pose.Rotation);

        using var oldStain = new SolidBrush(Color.FromArgb(54, 10, 15, 14));
        using var footprint = new SolidBrush(Color.FromArgb(100, 21, 28, 25));
        using var rail = new SolidBrush(Color.FromArgb(73, 94, 84, 64));
        using var oxide = new SolidBrush(Color.FromArgb(61, C.Oxide));
        g.FillPolygon(oldStain,
        [
            new PointF(-34, -15), new PointF(27, -19), new PointF(37, 6),
            new PointF(22, 24), new PointF(-31, 19), new PointF(-39, 3)
        ]);
        g.FillRectangle(footprint, -29, -13, 58, 27);
        g.FillRectangle(rail, -29, -14, 7, 29);
        g.FillRectangle(rail, 22, -14, 7, 29);
        for (var x = -16; x <= 16; x += 16)
            g.FillRectangle(oxide, x, 17, 8, 3);

        if (prop.Kind == RoomPropKind.WorkLight)
        {
            var sweep = RoomPropFrame(prop, 1.5f, 5) - 2;
            using var beam = new SolidBrush(Color.FromArgb(27, C.Signal));
            using var beamCore = new SolidBrush(Color.FromArgb(15, C.Bone));
            g.FillPolygon(beam,
            [
                new PointF(-8, 0), new PointF(8, 0),
                new PointF(45 + sweep * 8, 85), new PointF(-35 + sweep * 8, 85)
            ]);
            g.FillPolygon(beamCore,
            [
                new PointF(-3, 1), new PointF(4, 1),
                new PointF(17 + sweep * 6, 72), new PointF(-12 + sweep * 6, 72)
            ]);
        }
        else if (prop.Kind == RoomPropKind.CableReel)
        {
            var crawl = RoomPropFrame(prop, 3f, 6);
            using var cable = new Pen(Color.FromArgb(102, 74, 47), 4)
            {
                StartCap = LineCap.Square,
                EndCap = LineCap.Square,
                LineJoin = LineJoin.Miter
            };
            g.DrawLines(cable,
            [
                new PointF(20, 4), new PointF(37, 11), new PointF(33, 25),
                new PointF(51, 34 + (crawl & 1) * 3), new PointF(58, 48)
            ]);
        }
        g.Restore(state);
    }

    private void DrawRoomProp(Graphics g, RoomProp prop)
    {
        var pose = GetRoomPropPose(prop);
        var state = g.Save();
        g.TranslateTransform(pose.Center.X, pose.Center.Y);
        g.RotateTransform(pose.Rotation);
        switch (prop.Kind)
        {
            case RoomPropKind.CargoStack:
                DrawCargoStackProp(g, prop);
                break;
            case RoomPropKind.PipeManifold:
                DrawPipeManifoldProp(g, prop);
                break;
            case RoomPropKind.SpecimenCabinet:
                DrawSpecimenCabinetProp(g, prop);
                break;
            case RoomPropKind.PressureTank:
                DrawPressureTankProp(g, prop);
                break;
            case RoomPropKind.CableReel:
                DrawCableReelProp(g, prop);
                break;
            default:
                DrawWorkLightProp(g, prop);
                break;
        }
        g.Restore(state);
    }

    private void DrawCargoStackProp(Graphics g, RoomProp prop)
    {
        var side = (prop.Variant & 1) == 0 ? -1f : 1f;
        DrawUtilityCrate(g, new PointF(-17, 5), 31, 25, prop.Variant, false);
        DrawUtilityCrate(g, new PointF(14, 8), 29, 21, prop.Variant + 1, false);
        DrawUtilityCrate(g, new PointF(side * 8, -16), 37, 27, prop.Variant + 2, false);

        var latchFrame = RoomPropFrame(prop, 4f, 9);
        using var tag = new SolidBrush(latchFrame == 0 ? C.Signal : Color.FromArgb(166, 145, 96));
        using var ink = new SolidBrush(C.Ink);
        g.FillRectangle(tag, side * 4 - 7, -20, 14, 10);
        g.FillRectangle(ink, side * 4 - 4, -17, 8, 2);
        if (latchFrame == 0)
            g.FillRectangle(tag, side * 4 - 2, -25, 5, 4);
    }

    private void DrawUtilityCrate(Graphics g, PointF center, float width, float height, int variant,
        bool manifested)
    {
        using var shadow = new SolidBrush(Color.FromArgb(190, C.Void));
        using var shell = new SolidBrush(manifested
            ? Color.FromArgb(177, 170, 132)
            : variant % 3 == 0 ? Color.FromArgb(103, 111, 91) : Color.FromArgb(117, 119, 95));
        using var face = new SolidBrush(Color.FromArgb(38, 47, 42));
        using var strap = new SolidBrush(manifested ? C.Signal : Color.FromArgb(102, 67, 48));
        using var edge = new SolidBrush(Color.FromArgb(53, 61, 52));
        var x = center.X - width / 2;
        var y = center.Y - height / 2;
        g.FillRectangle(shadow, x + 4, y + 5, width, height);
        g.FillPolygon(shell,
        [
            new PointF(x + 4, y), new PointF(x + width - 3, y),
            new PointF(x + width, y + 5), new PointF(x + width, y + height - 3),
            new PointF(x + width - 4, y + height), new PointF(x, y + height),
            new PointF(x, y + 4)
        ]);
        g.FillRectangle(face, center.X - width / 2 + 4, center.Y - height / 2 + 4, width - 8, height - 8);
        g.FillRectangle(strap, center.X - 2, center.Y - height / 2, 4, height);
        if ((variant & 1) == 0)
            g.FillRectangle(strap, center.X - width / 2 + 4, center.Y - 2, width - 8, 4);
        g.FillRectangle(edge, x + 2, y + 2, 5, 4);
        g.FillRectangle(edge, x + width - 7, y + height - 6, 5, 4);
        using var stencil = new SolidBrush(Color.FromArgb(152, 144, 103));
        g.FillRectangle(stencil, x + 7, y + 7, Math.Max(4, width / 5), 2);
        g.FillRectangle(stencil, x + 7, y + 11, Math.Max(3, width / 8), 2);
    }

    private void DrawPipeManifoldProp(Graphics g, RoomProp prop)
    {
        var pulse = RoomPropFrame(prop, 5f, 8);
        using var shadow = new SolidBrush(Color.FromArgb(235, C.Void));
        using var shell = new SolidBrush(Color.FromArgb(47, 56, 49));
        using var pipe = new Pen(Color.FromArgb(126, 129, 99), 7)
        {
            StartCap = LineCap.Square,
            EndCap = LineCap.Square,
            LineJoin = LineJoin.Miter
        };
        using var pipeDark = new Pen(Color.FromArgb(65, 73, 63), 3)
        {
            StartCap = LineCap.Square,
            EndCap = LineCap.Square
        };
        using var valve = new SolidBrush(Color.FromArgb(125, 57, 43));
        using var valveCore = new SolidBrush(C.Void);
        using var lamp = new SolidBrush(pulse is 0 or 1 ? C.Signal : Color.FromArgb(74, 63, 48));

        g.FillPolygon(shadow,
        [
            new PointF(-31, -24), new PointF(29, -24), new PointF(34, -17),
            new PointF(31, 23), new PointF(-33, 23), new PointF(-36, -17)
        ]);
        g.FillRectangle(shell, -29, -20, 58, 39);
        g.DrawLines(pipe, [new PointF(-35, -12), new PointF(-17, -12), new PointF(-17, 13), new PointF(1, 13)]);
        g.DrawLines(pipe, [new PointF(35, 9), new PointF(16, 9), new PointF(16, -14), new PointF(-2, -14)]);
        g.DrawLine(pipeDark, -35, -12, -17, -12);
        g.DrawLine(pipeDark, 35, 9, 16, 9);
        DrawPixelValve(g, new PointF(-3, -13), 12, valve, valveCore, pulse);
        DrawPixelValve(g, new PointF(2, 13), 10, valve, valveCore, pulse + 2);
        g.FillRectangle(lamp, -4, -3, 8, 7);
        using var gauge = new SolidBrush(Color.FromArgb(174, C.Bone));
        using var gaugeInk = new SolidBrush(C.Ink);
        g.FillPolygon(gauge, PixelOctagon(new PointF(17, -4), 7, 5));
        g.FillRectangle(gaugeInk, 16, -5, pulse < 4 ? 5 : 2, 2);
    }

    private static void DrawPixelValve(Graphics g, PointF center, float radius, Brush edge,
        Brush core, int frame)
    {
        g.FillPolygon(edge, PixelOctagon(center, radius, radius));
        g.FillPolygon(core, PixelOctagon(center, radius - 4, radius - 4));
        if ((frame & 1) == 0)
        {
            g.FillRectangle(edge, center.X - radius, center.Y - 2, radius * 2, 4);
            g.FillRectangle(edge, center.X - 2, center.Y - radius, 4, radius * 2);
        }
        else
        {
            using var spoke = new Pen(Color.FromArgb(125, 57, 43), 4)
            {
                StartCap = LineCap.Square,
                EndCap = LineCap.Square
            };
            g.DrawLine(spoke, center.X - radius * .7f, center.Y - radius * .7f,
                center.X + radius * .7f, center.Y + radius * .7f);
            g.DrawLine(spoke, center.X + radius * .7f, center.Y - radius * .7f,
                center.X - radius * .7f, center.Y + radius * .7f);
        }
    }

    private void DrawSpecimenCabinetProp(Graphics g, RoomProp prop)
    {
        var drift = RoomPropFrame(prop, 2.2f, 6);
        var knockCycle = RoomPropFrame(prop, 4f, 37);
        using var shadow = new SolidBrush(Color.FromArgb(238, C.Void));
        using var cabinet = new SolidBrush(Color.FromArgb(83, 92, 76));
        using var edge = new SolidBrush(Color.FromArgb(137, 137, 103));
        using var glass = new SolidBrush(Color.FromArgb(185, 15, 24, 23));
        using var fog = new SolidBrush(Color.FromArgb(47, 146, 157, 123));
        using var specimen = new SolidBrush(Color.FromArgb(136, 83, 65));
        using var lamp = new SolidBrush(knockCycle == 0 ? C.Oxide : Color.FromArgb(101, 89, 58));

        g.FillRectangle(shadow, -27, -31, 56, 61);
        g.FillPolygon(cabinet,
        [
            new PointF(-24, -29), new PointF(21, -29), new PointF(25, -24),
            new PointF(25, 27), new PointF(-25, 27), new PointF(-25, -25)
        ]);
        g.FillRectangle(edge, -20, -25, 40, 4);
        g.FillRectangle(glass, -19, -19, 38, 38);

        var y = -7 + (drift is 1 or 2 ? 2 : drift is 4 ? -2 : 0);
        g.FillRectangle(specimen, -8, y - 8, 16, 4);
        g.FillRectangle(specimen, -12, y - 4, 24, 9);
        g.FillRectangle(specimen, -5, y + 5, 10, 7);
        g.FillRectangle(specimen, -14, y + (knockCycle is 0 or 1 ? -5 : 1), 5, 8);
        g.FillRectangle(specimen, 9, y + (knockCycle is 0 or 1 ? 2 : -2), 5, 8);

        for (var index = 0; index < 5; index++)
        {
            var bubbleY = 14 - ((drift * 5 + index * 9 + prop.Variant * 3) % 32);
            var bubbleX = -15 + PositiveHash(index * 13 + prop.RoomId * 7) % 30;
            g.FillRectangle(fog, bubbleX, bubbleY, index % 2 == 0 ? 3 : 2, index % 2 == 0 ? 3 : 2);
        }
        g.FillRectangle(fog, -17, -17, 9, 3);
        g.FillRectangle(fog, 7, 12, 10, 3);
        g.FillRectangle(edge, -22, 20, 44, 6);
        g.FillRectangle(lamp, 15, 22, 4, 3);
        LabFont.Draw(g, $"{prop.RoomId % 10}", -16, 22, 1, C.Ink, LabTextAlign.Center, 0);
    }

    private void DrawPressureTankProp(Graphics g, RoomProp prop)
    {
        var gaugeFrame = RoomPropFrame(prop, 3.5f, 7);
        var dripFrame = RoomPropFrame(prop, 2.4f, 8);
        using var shadow = new SolidBrush(Color.FromArgb(238, C.Void));
        using var shell = new SolidBrush(Color.FromArgb(153, 151, 116));
        using var shellDark = new SolidBrush(Color.FromArgb(93, 100, 82));
        using var strap = new SolidBrush(Color.FromArgb(76, 85, 72));
        using var oxide = new SolidBrush(Color.FromArgb(127, 56, 43));
        using var gauge = new SolidBrush(C.Bone);
        using var ink = new SolidBrush(C.Ink);
        using var wet = new SolidBrush(Color.FromArgb(128, 75, 104, 100));

        g.FillPolygon(shadow, PixelOctagon(new PointF(2, 0), 23, 34));
        g.FillPolygon(shell, PixelOctagon(PointF.Empty, 19, 31));
        g.FillRectangle(shellDark, -15, -25, 30, 6);
        g.FillRectangle(shellDark, -15, 19, 30, 6);
        g.FillRectangle(strap, -21, -11, 42, 7);
        g.FillRectangle(strap, -21, 10, 42, 7);
        g.FillRectangle(oxide, -5, -36, 10, 7);
        g.FillRectangle(ink, -2, -38, 4, 4);

        g.FillPolygon(gauge, PixelOctagon(new PointF(0, 0), 10, 9));
        g.FillPolygon(ink, PixelOctagon(new PointF(0, 0), 6, 5));
        var needleX = gaugeFrame switch { 0 => -4, 1 => -2, 2 or 3 => 0, 4 => 2, _ => 4 };
        using var needle = new Pen(C.Oxide, 2) { StartCap = LineCap.Square, EndCap = LineCap.Square };
        g.DrawLine(needle, 0, 0, needleX, -4);

        var dripY = -21 + dripFrame * 6;
        g.FillRectangle(wet, -13, dripY, 3, 6);
        if (dripFrame > 5) g.FillRectangle(wet, 9, -7 + (dripFrame - 5) * 5, 2, 4);
        LabFont.Draw(g, prop.Variant % 2 == 0 ? "P" : "O2", 0, 28, 1, C.Ink,
            LabTextAlign.Center, 0);
    }

    private void DrawCableReelProp(Graphics g, RoomProp prop)
    {
        var crawl = RoomPropFrame(prop, 4f, 12);
        using var shadow = new SolidBrush(Color.FromArgb(238, C.Void));
        using var flange = new SolidBrush(Color.FromArgb(144, 140, 105));
        using var edge = new SolidBrush(Color.FromArgb(70, 79, 67));
        using var cable = new SolidBrush(Color.FromArgb(112, 61, 44));
        using var hub = new SolidBrush(C.Void);

        g.FillPolygon(shadow, PixelOctagon(new PointF(3, 4), 30, 25));
        g.FillPolygon(flange, PixelOctagon(PointF.Empty, 27, 23));
        g.FillPolygon(edge, PixelOctagon(PointF.Empty, 21, 18));
        for (var ring = 0; ring < 4; ring++)
        {
            var ringWidth = 31 - ring * 5;
            g.FillRectangle(cable, -ringWidth / 2f, -13 + ring * 6, ringWidth, 4);
        }
        g.FillPolygon(hub, PixelOctagon(PointF.Empty, 8, 8));
        g.FillRectangle(flange, -2, -16, 4, 32);
        g.FillRectangle(flange, -18, -2, 36, 4);
        g.FillRectangle(cable, 24, 10, 18, 5);
        g.FillRectangle(cable, 39, 10, 5, 18);
        using var live = new SolidBrush(crawl is 0 or 1 ? C.Signal : C.Oxide);
        g.FillRectangle(live, 39, 12 + crawl, 5, 5);
    }

    private void DrawWorkLightProp(Graphics g, RoomProp prop)
    {
        var sweep = RoomPropFrame(prop, 1.5f, 5) - 2;
        var flicker = RoomPropFrame(prop, 8f, 17);
        using var shadow = new SolidBrush(Color.FromArgb(238, C.Void));
        using var frame = new SolidBrush(Color.FromArgb(85, 95, 79));
        using var joint = new SolidBrush(Color.FromArgb(151, 145, 105));
        using var lens = new SolidBrush(flicker == 0 ? C.Oxide : C.Signal);
        using var glass = new SolidBrush(flicker == 0
            ? Color.FromArgb(52, C.Oxide)
            : Color.FromArgb(116, C.Bone));

        g.FillRectangle(shadow, -24, -27, 48, 50);
        g.FillRectangle(frame, -20, 18, 40, 7);
        g.FillRectangle(frame, -4, -20, 8, 40);
        g.FillPolygon(frame,
        [
            new PointF(-20, 22), new PointF(-7, 15), new PointF(-2, 18), new PointF(-11, 27)
        ]);
        g.FillPolygon(frame,
        [
            new PointF(20, 22), new PointF(7, 15), new PointF(2, 18), new PointF(11, 27)
        ]);
        g.FillPolygon(joint, PixelOctagon(new PointF(0, -15), 8, 7));

        var lampX = sweep * 4;
        g.FillPolygon(frame,
        [
            new PointF(lampX - 17, -34), new PointF(lampX + 13, -34),
            new PointF(lampX + 18, -26), new PointF(lampX + 13, -17),
            new PointF(lampX - 17, -17), new PointF(lampX - 21, -25)
        ]);
        g.FillRectangle(glass, lampX - 13, -30, 25, 9);
        g.FillRectangle(lens, lampX - 7, -28, 13, 5);
    }

    private static PointF[] PixelOctagon(PointF center, float radiusX, float radiusY)
    {
        var cutX = Math.Max(2, radiusX * .36f);
        var cutY = Math.Max(2, radiusY * .36f);
        return
        [
            new PointF(center.X - radiusX + cutX, center.Y - radiusY),
            new PointF(center.X + radiusX - cutX, center.Y - radiusY),
            new PointF(center.X + radiusX, center.Y - radiusY + cutY),
            new PointF(center.X + radiusX, center.Y + radiusY - cutY),
            new PointF(center.X + radiusX - cutX, center.Y + radiusY),
            new PointF(center.X - radiusX + cutX, center.Y + radiusY),
            new PointF(center.X - radiusX, center.Y + radiusY - cutY),
            new PointF(center.X - radiusX, center.Y - radiusY + cutY)
        ];
    }

    private void DrawRoomSalvage(Graphics g, RoomSalvage salvage)
    {
        var p = CellCenter(salvage.Cell);
        var bob = MathF.Sin(_time * 3.1f + salvage.RoomId) * 3;
        p.Y += bob;
        using var glow = new SolidBrush(Color.FromArgb(36, C.Signal));
        using var dark = new SolidBrush(C.Void);
        using var edge = new SolidBrush(C.Signal);
        using var core = new SolidBrush(Color.FromArgb(143, 139, 103));
        g.FillRectangle(glow, p.X - 24, p.Y - 20, 48, 40);
        switch (salvage.Kind)
        {
            case SalvageKind.CopperSpool:
                g.FillRectangle(edge, p.X - 17, p.Y - 13, 34, 26);
                g.FillRectangle(dark, p.X - 11, p.Y - 9, 22, 18);
                for (var x = -7; x <= 7; x += 4) g.FillRectangle(core, p.X + x, p.Y - 7, 2, 14);
                break;
            case SalvageKind.OpticShard:
                g.FillPolygon(edge, [new PointF(p.X, p.Y - 18), new PointF(p.X + 14, p.Y + 9),
                    new PointF(p.X + 3, p.Y + 16), new PointF(p.X - 13, p.Y + 6)]);
                g.FillPolygon(core, [new PointF(p.X, p.Y - 10), new PointF(p.X + 7, p.Y + 6),
                    new PointF(p.X - 5, p.Y + 7)]);
                break;
            case SalvageKind.ServoClutch:
                g.FillRectangle(edge, p.X - 16, p.Y - 16, 32, 32);
                g.FillRectangle(dark, p.X - 10, p.Y - 10, 20, 20);
                g.FillRectangle(core, p.X - 4, p.Y - 4, 8, 8);
                break;
            default:
                g.FillPolygon(edge, [new PointF(p.X - 17, p.Y - 10), new PointF(p.X + 12, p.Y - 15),
                    new PointF(p.X + 17, p.Y + 11), new PointF(p.X - 12, p.Y + 15)]);
                for (var y = -7; y <= 7; y += 5) g.FillRectangle(dark, p.X - 9, p.Y + y, 18, 2);
                break;
        }
        LabFont.Draw(g, "S", p.X, p.Y + 23, 1, C.Signal, LabTextAlign.Center, 0);
    }

    private void DrawShopKioskFloor(Graphics g, ShopKiosk kiosk)
    {
        var p = CellCenter(kiosk.Cell);
        var pulse = PositiveHash((int)MathF.Floor(_time * 5) + kiosk.RoomId) % 8;
        using var aura = new SolidBrush(Color.FromArgb(pulse < 2 ? 41 : 27, C.Signal));
        using var pad = new SolidBrush(Color.FromArgb(128, 24, 31, 27));
        using var safeLine = new SolidBrush(Color.FromArgb(93, C.Signal));
        using var deadLine = new SolidBrush(Color.FromArgb(78, C.Steel));
        g.FillPolygon(aura,
        [
            new PointF(p.X - 47, p.Y - 29), new PointF(p.X + 47, p.Y - 29),
            new PointF(p.X + 59, p.Y + 6), new PointF(p.X + 40, p.Y + 38),
            new PointF(p.X - 40, p.Y + 38), new PointF(p.X - 59, p.Y + 6)
        ]);
        g.FillPolygon(pad,
        [
            new PointF(p.X - 39, p.Y - 22), new PointF(p.X + 39, p.Y - 22),
            new PointF(p.X + 48, p.Y + 8), new PointF(p.X + 32, p.Y + 29),
            new PointF(p.X - 32, p.Y + 29), new PointF(p.X - 48, p.Y + 8)
        ]);
        for (var x = -34; x <= 28; x += 14)
        {
            g.FillRectangle((x / 14 + pulse) % 3 == 0 ? safeLine : deadLine,
                p.X + x, p.Y + 24, 8, 3);
        }
        g.FillRectangle(safeLine, p.X - 46, p.Y - 4, 5, 14);
        g.FillRectangle(safeLine, p.X + 41, p.Y - 4, 5, 14);
    }

    private void DrawShopKiosk(Graphics g, ShopKiosk kiosk)
    {
        var p = ShopKioskRenderCenter(kiosk);
        var bodyFrame = PositiveHash((int)MathF.Floor(_time * 2.2f) + kiosk.RoomId * 3) % 4;
        var player = CellCenter(_visualCell);
        var lookX = Math.Abs(player.X - p.X) < 12 ? 0 : Math.Sign(player.X - p.X);
        var lookY = Math.Abs(player.Y - p.Y) < 12 ? 0 : Math.Sign(player.Y - p.Y);
        using var gantryShadow = new SolidBrush(Color.FromArgb(220, C.Void));
        using var gantry = new SolidBrush(Color.FromArgb(69, 78, 66));
        using var edge = new SolidBrush(Color.FromArgb(128, 124, 94));
        using var signal = new SolidBrush(bodyFrame == 0 ? C.Signal : Color.FromArgb(129, 88, 50));
        using var cable = new Pen(Color.FromArgb(102, 105, 84), 3)
        {
            StartCap = LineCap.Square,
            EndCap = LineCap.Square
        };

        // A freestanding gantry holds the same unknowable silhouette seen in the
        // full shop window. It hangs above the traversable tile so the drone can
        // pass underneath without the prop reading as a solid obstacle.
        g.FillPolygon(gantryShadow,
        [
            new PointF(p.X - 39, p.Y - 39), new PointF(p.X + 34, p.Y - 39),
            new PointF(p.X + 43, p.Y + 20), new PointF(p.X + 35, p.Y + 29),
            new PointF(p.X - 39, p.Y + 29), new PointF(p.X - 47, p.Y + 20)
        ]);
        g.FillRectangle(gantry, p.X - 37, p.Y - 38, 74, 8);
        g.FillRectangle(gantry, p.X - 39, p.Y - 34, 7, 51);
        g.FillRectangle(gantry, p.X + 32, p.Y - 34, 7, 51);
        g.DrawLine(cable, p.X - 20, p.Y - 30, p.X - 17, p.Y - 20 + bodyFrame);
        g.DrawLine(cable, p.X + 17, p.Y - 30, p.X + 19, p.Y - 18 - bodyFrame / 2f);

        var bodyCenter = new PointF(p.X, p.Y - 1);
        DrawShopkeeperShroud(g, bodyCenter, .52f, bodyFrame);
        DrawShopkeeperEyes(g, new PointF(bodyCenter.X, bodyCenter.Y - 28 * .52f),
            .52f, lookX, lookY, kiosk.RoomId, 0);

        g.FillPolygon(gantry,
        [
            new PointF(p.X - 43, p.Y + 11), new PointF(p.X + 43, p.Y + 11),
            new PointF(p.X + 37, p.Y + 27), new PointF(p.X - 37, p.Y + 27)
        ]);
        g.FillRectangle(edge, p.X - 43, p.Y + 11, 86, 5);
        g.FillRectangle(signal, p.X - 32, p.Y + 19, 11, 4);
        g.FillRectangle(signal, p.X + 24, p.Y + 19, 8, 4);
        for (var x = -14; x <= 14; x += 7)
            g.FillRectangle(gantryShadow, p.X + x, p.Y + 18, 3, 5);

        var near = IsShopKioskInRange(_playerCell);
        if (near || ((int)(_time * 2 + kiosk.RoomId) & 1) == 0)
            LabFont.Draw(g, near ? "E / TRADE" : "SAFE TRADE", p.X, p.Y + 35, 1,
                near ? C.Signal : C.Sick, LabTextAlign.Center, 0);
    }

    private PointF ShopKioskRenderCenter(ShopKiosk kiosk)
    {
        var center = CellCenter(kiosk.Cell);
        var room = _maze?.Rooms.FirstOrDefault(candidate =>
            candidate.Id == kiosk.RoomId);
        if (room is null) return center;

        var outward = new List<Point>(4);
        if (!room.Contains(new Point(kiosk.Cell.X, kiosk.Cell.Y - 1)))
            outward.Add(new Point(0, -1));
        if (!room.Contains(new Point(kiosk.Cell.X + 1, kiosk.Cell.Y)))
            outward.Add(new Point(1, 0));
        if (!room.Contains(new Point(kiosk.Cell.X, kiosk.Cell.Y + 1)))
            outward.Add(new Point(0, 1));
        if (!room.Contains(new Point(kiosk.Cell.X - 1, kiosk.Cell.Y)))
            outward.Add(new Point(-1, 0));
        if (outward.Count == 0) return center;

        var anchor = outward[PositiveHash(kiosk.RoomId) % outward.Count];
        var offset = _cellSize * .36f;
        return new PointF(center.X + anchor.X * offset,
            center.Y + anchor.Y * offset);
    }

    private void DrawShopConsole(Graphics g)
    {
        var outer = new RectangleF(48, 63, DesignWidth - 96, DesignHeight - 126);
        DrawCutPanel(g, outer, Color.FromArgb(12, 18, 18), Color.FromArgb(98, C.Steel), 18, 5);
        using (var hardware = new SolidBrush(Color.FromArgb(63, 73, 63)))
        using (var warning = new SolidBrush(Color.FromArgb(112, C.Oxide)))
        {
            g.FillRectangle(hardware, outer.X + 12, outer.Y + 54, 7, outer.Height - 100);
            g.FillRectangle(hardware, outer.Right - 19, outer.Y + 54, 7, outer.Height - 100);
            for (var y = outer.Y + 64; y < outer.Bottom - 52; y += 42)
            {
                g.FillRectangle(warning, outer.X + 13, y, 5, 15);
                g.FillRectangle(warning, outer.Right - 18, y + 17, 5, 15);
            }
        }
        LabFont.Draw(g, "RECLAMATION WINDOW / SAFE LIGHT", outer.X + 24, outer.Y + 18, 2, C.Signal);
        LabFont.Draw(g, $"AVAILABLE {AvailableShopCredits:000000}", outer.Right - 24, outer.Y + 20,
            2, C.Bone, LabTextAlign.Right);

        var portrait = new RectangleF(70, 111, 450, 390);
        var inventory = new RectangleF(543, 111, 667, 390);
        DrawCutPanel(g, portrait, Color.FromArgb(4, 7, 7), Color.FromArgb(72, C.Steel), 13, 3);
        DrawCutPanel(g, inventory, Color.FromArgb(23, 30, 27), Color.FromArgb(88, C.Steel), 13, 3);
        DrawShopkeeperPortrait(g, portrait);
        DrawShopInventoryPanel(g, inventory);

        var dialogue = new RectangleF(70, 519, 1140, 105);
        DrawCutPanel(g, dialogue, Color.FromArgb(202, 190, 143), Color.FromArgb(51, 57, 48), 12, 4);
        DrawShopDialogue(g, dialogue);

        var commandNames = new[] { "BUY", "SELL", "TALK", "LEAVE" };
        const float gap = 12;
        var width = (1140 - gap * 3) / 4;
        for (var index = 0; index < commandNames.Length; index++)
        {
            var rect = new RectangleF(70 + index * (width + gap), 641, width, 62);
            _shopCommandButtons[index] = rect;
            var selected = _shopPage == ShopPage.Commands && _shopCommandSelection == index;
            var hovered = _hoverShopCommand == index;
            DrawShopCommand(g, rect, commandNames[index], selected, hovered,
                _shopPage != ShopPage.Commands && (int)_shopPage - 1 == index);
        }
        LabFont.Draw(g, $"REPAIR {_shopRepairReserve:00}    AEGIS {_shopProtectionCharges:00}    SALVAGE {SellInventory().Sum(x => x.Count):00}",
            outer.Right - 24, outer.Bottom - 25, 1, C.Sick, LabTextAlign.Right);
    }

    private void DrawShopkeeperPortrait(Graphics g, RectangleF rect)
    {
        var clip = g.Save();
        g.SetClip(RectangleF.Inflate(rect, -7, -7), CombineMode.Intersect);
        using var murk = new SolidBrush(Color.FromArgb(9, 12, 11));
        using var black = new SolidBrush(Color.Black);
        using var nearBlack = new SolidBrush(Color.FromArgb(4, 7, 7));
        using var machinery = new SolidBrush(Color.FromArgb(35, 43, 38));
        using var steel = new SolidBrush(Color.FromArgb(72, 81, 67));
        using var safeGlow = new SolidBrush(Color.FromArgb(24, C.Signal));
        using var counter = new SolidBrush(Color.FromArgb(65, 67, 55));
        using var counterFace = new SolidBrush(Color.FromArgb(38, 45, 40));
        using var rim = new SolidBrush(Color.FromArgb(139, 126, 91));
        using var signal = new SolidBrush(Color.FromArgb(159, 103, 54));
        g.FillRectangle(murk, rect.X + 7, rect.Y + 7, rect.Width - 14, rect.Height - 14);

        // A deep service recess replaces a conventional portrait backdrop.
        // Pipes and inspection slots continue the exact silhouette language of
        // the in-world kiosk, making this feel like a close camera on it.
        g.FillRectangle(machinery, rect.X + 22, rect.Y + 25, 12, rect.Height - 107);
        g.FillRectangle(machinery, rect.Right - 38, rect.Y + 25, 12, rect.Height - 107);
        for (var y = rect.Y + 31; y < rect.Bottom - 87; y += 35)
        {
            g.FillRectangle(steel, rect.X + 18, y, 22, 6);
            g.FillRectangle(steel, rect.Right - 44, y + 14, 22, 6);
        }
        for (var index = 0; index < 13; index++)
        {
            var x = rect.X + 45 + PositiveHash(index * 89 + 13) % (int)(rect.Width - 90);
            var y = rect.Y + 21 + PositiveHash(index * 151 + 7) % (int)(rect.Height - 115);
            var jitter = !ShopDialogueReady && (index + _shopDialogueVisible) % 7 == 0 ? 13 : 0;
            g.FillRectangle(nearBlack, x + jitter, y, 18 + index * 7 % 55, 4 + index % 3);
        }
        g.FillPolygon(safeGlow,
        [
            new PointF(rect.X + 76, rect.Bottom - 82), new PointF(rect.Right - 70, rect.Bottom - 82),
            new PointF(rect.Right - 24, rect.Y + 68), new PointF(rect.X + 29, rect.Y + 68)
        ]);

        var bodyFrame = PositiveHash((int)MathF.Floor(_time * 2.15f) + 3) % 4;
        var center = new PointF(rect.X + rect.Width * .51f, rect.Y + 198);
        DrawShopkeeperShroud(g, center, 1.82f, bodyFrame);

        var lookX = 0;
        var lookY = 0;
        if (!ShopDialogueReady)
        {
            lookY = 1;
            lookX = (_shopDialogueVisible / 7) % 3 - 1;
        }
        else if (_shopPage == ShopPage.Commands)
        {
            lookX = _shopCommandSelection < 2 ? -1 : 1;
            lookY = 1;
        }
        else
        {
            lookX = 1;
            lookY = Math.Clamp(_shopListSelection - 1, -1, 1);
        }

        var expression = ShopkeeperExpression();
        DrawShopkeeperEyes(g, new PointF(center.X, center.Y - 28 * 1.82f),
            1.82f, lookX, lookY, 73, expression);

        // The lower silhouette changes pose with the active transaction instead
        // of being a static bust.
        using (var limb = new Pen(Color.Black, 17)
               {
                   StartCap = LineCap.Square, EndCap = LineCap.Square, LineJoin = LineJoin.Miter
               })
        {
            if (_shopPage is ShopPage.Buy or ShopPage.Sell)
            {
                var reachY = rect.Bottom - (_shopPage == ShopPage.Buy ? 90 : 105);
                g.DrawLines(limb,
                [
                    new PointF(center.X + 72, center.Y + 35),
                    new PointF(center.X + 123, center.Y + 70),
                    new PointF(rect.Right - 18, reachY)
                ]);
            }
            else if (_shopPage == ShopPage.Talk)
            {
                g.DrawLine(limb, center.X - 64, center.Y + 43, center.X - 109, rect.Bottom - 79);
                g.DrawLine(limb, center.X + 63, center.Y + 43, center.X + 103, rect.Bottom - 79);
            }
        }

        g.FillPolygon(counter,
        [
            new PointF(rect.X + 8, rect.Bottom - 78), new PointF(rect.Right - 8, rect.Bottom - 78),
            new PointF(rect.Right - 17, rect.Bottom - 10), new PointF(rect.X + 17, rect.Bottom - 10)
        ]);
        g.FillRectangle(rim, rect.X + 8, rect.Bottom - 78, rect.Width - 16, 9);
        g.FillRectangle(counterFace, rect.X + 23, rect.Bottom - 61, rect.Width - 46, 36);
        for (var x = rect.X + 34; x < rect.Right - 30; x += 46)
        {
            g.FillRectangle(black, x, rect.Bottom - 53, 21, 5);
            g.FillRectangle(signal, x + 7, rect.Bottom - 42, 7, 4);
        }
        LabFont.Draw(g, "COUNTER-LIGHT HOLDS", rect.X + rect.Width / 2, rect.Bottom - 31,
            1, C.Sick, LabTextAlign.Center, 0);
        g.Restore(clip);
    }

    private int ShopkeeperExpression()
    {
        if (!ShopDialogueReady) return 3;
        if (_shopDialogue.Contains("lighter", StringComparison.OrdinalIgnoreCase) ||
            _shopDialogue.Contains("empty hook", StringComparison.OrdinalIgnoreCase) ||
            _shopDialogue.Contains("ignored", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (_shopDialogue.Contains("armed", StringComparison.OrdinalIgnoreCase) ||
            _shopDialogue.Contains("fracture", StringComparison.OrdinalIgnoreCase) ||
            _shopDialogue.Contains("credits", StringComparison.OrdinalIgnoreCase) ||
            _shopDialogue.Contains("take them", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 0;
    }

    private static PointF ShopkeeperPoint(PointF center, float scale, float x, float y) =>
        new(center.X + x * scale, center.Y + y * scale);

    private static void DrawShopkeeperShroud(Graphics g, PointF center, float scale, int frame)
    {
        var heave = frame switch { 1 => 2f, 2 => 0, 3 => -2f, _ => 0 };
        using var black = new SolidBrush(Color.Black);
        using var inside = new SolidBrush(Color.FromArgb(4, 6, 6));
        using var fringe = new SolidBrush(Color.FromArgb(2, 3, 3));
        g.FillPolygon(black,
        [
            ShopkeeperPoint(center, scale, -72 - heave, 66),
            ShopkeeperPoint(center, scale, -62, 7),
            ShopkeeperPoint(center, scale, -47 + heave, -34),
            ShopkeeperPoint(center, scale, -20, -64 - heave),
            ShopkeeperPoint(center, scale, 12, -69),
            ShopkeeperPoint(center, scale, 39 + heave, -52),
            ShopkeeperPoint(center, scale, 58, -17),
            ShopkeeperPoint(center, scale, 73 - heave, 67)
        ]);
        g.FillPolygon(inside,
        [
            ShopkeeperPoint(center, scale, -53, 62),
            ShopkeeperPoint(center, scale, -45, 11),
            ShopkeeperPoint(center, scale, -28, -42),
            ShopkeeperPoint(center, scale, 8, -56),
            ShopkeeperPoint(center, scale, 35, -39),
            ShopkeeperPoint(center, scale, 51, 9),
            ShopkeeperPoint(center, scale, 57, 62)
        ]);
        g.FillPolygon(black,
        [
            ShopkeeperPoint(center, scale, -66, 45),
            ShopkeeperPoint(center, scale, -78 - heave, 83),
            ShopkeeperPoint(center, scale, -56, 74),
            ShopkeeperPoint(center, scale, -43, 91 + heave),
            ShopkeeperPoint(center, scale, -23, 72),
            ShopkeeperPoint(center, scale, -6, 94 - heave),
            ShopkeeperPoint(center, scale, 12, 73),
            ShopkeeperPoint(center, scale, 31, 90 + heave),
            ShopkeeperPoint(center, scale, 48, 72),
            ShopkeeperPoint(center, scale, 76 + heave, 84),
            ShopkeeperPoint(center, scale, 66, 44)
        ]);
        g.FillRectangle(fringe, center.X - 21 * scale, center.Y - 61 * scale,
            25 * scale, 7 * scale);
        g.FillRectangle(fringe, center.X + 17 * scale, center.Y - 48 * scale,
            24 * scale, 5 * scale);
        g.FillRectangle(fringe, center.X - 48 * scale, center.Y + 9 * scale,
            18 * scale, 4 * scale);
    }

    private void DrawShopkeeperEyes(Graphics g, PointF center, float scale, int lookX, int lookY,
        int seed, int expression)
    {
        var leftPhase = (_time + seed * .173f) % 5.7f;
        var rightPhase = (_time + seed * .319f + 1.9f) % 7.3f;
        var closeLeft = leftPhase < .11f;
        var closeRight = rightPhase < .13f;
        var talkingJitter = expression == 3 && (_shopDialogueVisible & 3) == 0 ? 1 : 0;
        using var glow = new SolidBrush(Color.FromArgb(31, C.Bone));
        using var eye = new SolidBrush(expression == 1 ? Color.FromArgb(176, 164, 126) : C.Bone);
        using var pupil = new SolidBrush(C.Void);
        using var lid = new SolidBrush(Color.Black);
        for (var side = -1; side <= 1; side += 2)
        {
            var closed = side < 0 ? closeLeft : closeRight;
            var eyeCenter = new PointF(
                center.X + side * 17 * scale,
                center.Y + (side > 0 ? 1.5f : 0) * scale + talkingJitter * scale);
            var halfWidth = (expression == 2 ? 11f : 9.5f) * scale;
            var halfHeight = (expression == 1 ? 3.5f : 5.5f) * scale;
            g.FillRectangle(glow, eyeCenter.X - halfWidth - 3 * scale,
                eyeCenter.Y - halfHeight - 3 * scale, (halfWidth + 3 * scale) * 2,
                (halfHeight + 3 * scale) * 2);
            if (closed)
            {
                g.FillRectangle(eye, eyeCenter.X - halfWidth, eyeCenter.Y - scale,
                    halfWidth * 2, Math.Max(2, scale * 1.5f));
                continue;
            }

            g.FillPolygon(eye,
            [
                new PointF(eyeCenter.X - halfWidth + 3 * scale, eyeCenter.Y - halfHeight),
                new PointF(eyeCenter.X + halfWidth, eyeCenter.Y - halfHeight),
                new PointF(eyeCenter.X + halfWidth, eyeCenter.Y + halfHeight - 2 * scale),
                new PointF(eyeCenter.X + halfWidth - 3 * scale, eyeCenter.Y + halfHeight),
                new PointF(eyeCenter.X - halfWidth, eyeCenter.Y + halfHeight),
                new PointF(eyeCenter.X - halfWidth, eyeCenter.Y - halfHeight + 2 * scale)
            ]);
            var pupilX = eyeCenter.X + lookX * 3.5f * scale;
            var pupilY = eyeCenter.Y + lookY * 2.2f * scale;
            g.FillRectangle(pupil, pupilX - 2.5f * scale, pupilY - 4.5f * scale,
                5 * scale, 9 * scale);
            g.FillRectangle(eye, pupilX - .7f * scale, pupilY - 2 * scale,
                1.5f * scale, 2 * scale);
            if (expression == 1)
                g.FillPolygon(lid,
                [
                    new PointF(eyeCenter.X - halfWidth, eyeCenter.Y - halfHeight),
                    new PointF(eyeCenter.X + halfWidth, eyeCenter.Y - halfHeight),
                    new PointF(eyeCenter.X + side * halfWidth, eyeCenter.Y - halfHeight + 4 * scale)
                ]);
        }
    }

    private void DrawShopInventoryPanel(Graphics g, RectangleF rect)
    {
        foreach (var row in _shopListRows) { }
        if (_shopPage == ShopPage.Commands)
        {
            LabFont.Draw(g, "THE KIOSK KNOWS YOUR CHASSIS", rect.X + 24, rect.Y + 24, 2, C.Bone);
            LabFont.Draw(g, "SAVED AND FIELD CREDITS ARE BOTH SPENDABLE.", rect.X + 24, rect.Y + 75, 1, C.Sick);
            LabFont.Draw(g, "ROOM SALVAGE REMAINS INVENTORY UNTIL SOLD.", rect.X + 24, rect.Y + 103, 1, C.Sick);
            LabFont.Draw(g, "EXCESS REPAIR STOCK BANKS FOR THE NEXT HIT.", rect.X + 24, rect.Y + 131, 1, C.Sick);
            LabFont.Draw(g, "AEGIS FUSES CANCEL ONE FUTURE HIT.", rect.X + 24, rect.Y + 159, 1, C.Sick);
            using var line = new Pen(Color.FromArgb(95, C.Steel), 2);
            g.DrawLine(line, rect.X + 24, rect.Y + 202, rect.Right - 24, rect.Y + 202);
            LabFont.Draw(g, $"FRAME DAMAGE  {_damageTaken:00}/{GetMaximumHealth():00}", rect.X + 24, rect.Y + 229, 2,
                _damageTaken > 0 ? C.Oxide : C.Bone);
            LabFont.Draw(g, $"WARD INVENTORY  {_shopProtectionCharges:00}", rect.X + 24, rect.Y + 278, 2, C.Signal);
            LabFont.Draw(g, $"REPAIR RESERVE  {_shopRepairReserve:00}    SALVAGE  {SellInventory().Sum(item => item.Count):00}",
                rect.X + 24, rect.Y + 327, 2, C.Bone);
            return;
        }

        var count = ShopListCount();
        var heading = _shopPage switch
        {
            ShopPage.Buy => "LIMITED STOCK",
            ShopPage.Sell => "SALVAGE RACK",
            _ => "ASK THE SILHOUETTE"
        };
        LabFont.Draw(g, heading, rect.X + 22, rect.Y + 18, 2, C.Signal);
        for (var index = 0; index < _shopListRows.Length; index++)
        {
            var row = new RectangleF(rect.X + 19, rect.Y + 58 + index * 51, rect.Width - 38, 44);
            _shopListRows[index] = row;
            if (index >= count) continue;
            var selected = _shopListSelection == index;
            var hovered = _hoverShopRow == index;
            DrawCutPanel(g, row,
                selected ? Color.FromArgb(48, 55, 46) : Color.FromArgb(16, 23, 21),
                hovered || selected ? C.Signal : Color.FromArgb(70, C.Steel), 7, selected ? 3 : 2);
            if (selected)
            {
                using var marker = new SolidBrush(C.Signal);
                g.FillRectangle(marker, row.X + 7, row.Y + 8, 6, row.Height - 16);
            }
            DrawShopRowText(g, row, index, selected || hovered);
        }
    }

    private void DrawShopRowText(Graphics g, RectangleF row, int index, bool active)
    {
        var color = active ? C.Bone : C.Sick;
        switch (_shopPage)
        {
            case ShopPage.Buy:
            {
                var item = _shopStock[index];
                LabFont.Draw(g, item.Name, row.X + 23, row.Y + 8, 2, item.Stock > 0 ? color : C.Steel);
                LabFont.Draw(g, item.Stock > 0 ? $"{item.Price:000} CR / {item.Stock} LEFT" : "SOLD OUT",
                    row.Right - 14, row.Y + 10, 1, item.Stock > 0 ? C.Signal : C.Oxide, LabTextAlign.Right);
                if (_shopListSelection == index)
                    LabFont.Draw(g, item.Description.ToUpperInvariant(), row.X + 23, row.Bottom - 12, 1, C.Steel);
                break;
            }
            case ShopPage.Sell:
            {
                var item = SellInventory()[index];
                LabFont.Draw(g, SalvageName(item.Kind), row.X + 23, row.Y + 8, 2, color);
                LabFont.Draw(g, $"X{item.Count:00} / +{item.Value:000} CR", row.Right - 14, row.Y + 10,
                    1, C.Signal, LabTextAlign.Right);
                break;
            }
            case ShopPage.Talk:
                LabFont.Draw(g, ShopTopics[index].Topic, row.X + 23, row.Y + 10, 2, color);
                break;
        }
    }

    private void DrawShopDialogue(Graphics g, RectangleF rect)
    {
        var visible = _shopDialogue[..Math.Clamp(_shopDialogueVisible, 0, _shopDialogue.Length)]
            .Replace('\n', ' ')
            .ToUpperInvariant();
        var lines = WrapShopText(visible, 90).Take(3).ToArray();
        for (var index = 0; index < lines.Length; index++)
            LabFont.Draw(g, lines[index], rect.X + 26, rect.Y + 19 + index * 25, 1, C.Ink);
        if (!ShopDialogueReady && ((int)(_time * 6) & 1) == 0)
        {
            var last = lines.LastOrDefault() ?? string.Empty;
            var cursorX = rect.X + 28 + LabFont.Measure(last, 1).Width;
            var cursorY = rect.Y + 19 + Math.Max(0, lines.Length - 1) * 25;
            using var cursor = new SolidBrush(C.Oxide);
            g.FillRectangle(cursor, cursorX, cursorY, 7, 13);
        }
    }

    private static IEnumerable<string> WrapShopText(string text, int maximumCharacters)
    {
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + word.Length + 1 > maximumCharacters)
            {
                yield return line;
                line = word;
            }
            else
            {
                line = line.Length == 0 ? word : $"{line} {word}";
            }
        }
        if (line.Length > 0) yield return line;
    }

    private static void DrawShopCommand(Graphics g, RectangleF rect, string text, bool selected,
        bool hovered, bool pageActive)
    {
        var active = selected || hovered || pageActive;
        DrawCutPanel(g, rect,
            selected ? Color.FromArgb(58, 53, 39) : Color.FromArgb(19, 27, 25),
            active ? C.Signal : C.Steel, 9, active ? 4 : 2);
        if (selected)
        {
            using var pointer = new SolidBrush(C.Signal);
            g.FillPolygon(pointer, [new PointF(rect.X + 17, rect.Y + rect.Height / 2),
                new PointF(rect.X + 31, rect.Y + rect.Height / 2 - 10),
                new PointF(rect.X + 31, rect.Y + rect.Height / 2 + 10)]);
        }
        LabFont.Draw(g, text, rect.X + rect.Width / 2, rect.Y + 19, 2,
            active ? C.Bone : C.Sick, LabTextAlign.Center);
    }

    private void HandleShopMouseMove(PointF hit)
    {
        for (var index = 0; index < _shopCommandButtons.Length; index++)
            if (_shopCommandButtons[index].Contains(hit)) _hoverShopCommand = index;
        if (_shopPage == ShopPage.Commands) return;
        for (var index = 0; index < Math.Min(_shopListRows.Length, ShopListCount()); index++)
            if (_shopListRows[index].Contains(hit)) _hoverShopRow = index;
    }

    private bool HandleShopMouseDown(PointF hit)
    {
        if (!ShopDialogueReady) return true;
        for (var index = 0; index < _shopCommandButtons.Length; index++)
        {
            if (!_shopCommandButtons[index].Contains(hit)) continue;
            _shopCommandSelection = index;
            ActivateShopCommand();
            return true;
        }
        if (_shopPage == ShopPage.Commands) return false;
        for (var index = 0; index < Math.Min(_shopListRows.Length, ShopListCount()); index++)
        {
            if (!_shopListRows[index].Contains(hit)) continue;
            _shopListSelection = index;
            ActivateShopListSelection();
            return true;
        }
        return false;
    }
}
