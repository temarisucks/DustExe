using System.Drawing.Drawing2D;

namespace Dust;

internal sealed partial class GameForm
{
    private void DrawCargoRoomContents(Graphics g)
    {
        foreach (var prop in _roomProps)
        {
            if (IsCellConcealed(prop.Cell)) continue;
            DrawRoomProp(g, prop);
        }
        foreach (var salvage in _roomSalvage)
        {
            if (salvage.Collected || salvage.Sold || IsCellConcealed(salvage.Cell)) continue;
            DrawRoomSalvage(g, salvage);
        }
        if (_shopKiosk is not null && !IsCellConcealed(_shopKiosk.Cell))
            DrawShopKiosk(g, _shopKiosk);
    }

    private void DrawRoomProp(Graphics g, RoomProp prop)
    {
        var p = RoomPropRenderCenter(prop);
        var side = prop.Variant % 2 == 0 ? -1f : 1f;
        using var voidBrush = new SolidBrush(Color.FromArgb(230, C.Void));
        using var dark = new SolidBrush(Color.FromArgb(34, 43, 39));
        using var metal = new SolidBrush(Color.FromArgb(91, 96, 78));
        using var bone = new SolidBrush(Color.FromArgb(151, 148, 116));
        using var oxide = new SolidBrush(Color.FromArgb(128, 59, 43));
        using var signal = new SolidBrush(((int)(_time * 3 + prop.Variant) & 3) == 0 ? C.Signal : C.Oxide);
        switch (prop.Kind)
        {
            case RoomPropKind.CargoStack:
                DrawUtilityCrate(g, new PointF(p.X - 13, p.Y + 10), 27, 22, prop.Variant, false);
                DrawUtilityCrate(g, new PointF(p.X + 10, p.Y + 14), 23, 18, prop.Variant + 1, false);
                DrawUtilityCrate(g, new PointF(p.X + side * 7, p.Y - 7), 29, 21, prop.Variant + 2, false);
                break;
            case RoomPropKind.PipeManifold:
                g.FillRectangle(voidBrush, p.X - 25, p.Y - 20, 50, 39);
                g.FillRectangle(metal, p.X - 20, p.Y - 15, 40, 8);
                g.FillRectangle(metal, p.X - 18, p.Y + 5, 36, 7);
                g.FillRectangle(bone, p.X - 15, p.Y - 14, 6, 27);
                g.FillRectangle(bone, p.X + 8, p.Y - 14, 6, 27);
                g.FillRectangle(oxide, p.X - 5, p.Y - 8, 11, 19);
                g.FillRectangle(dark, p.X - 2, p.Y - 5, 5, 13);
                break;
            case RoomPropKind.SpecimenCabinet:
                g.FillRectangle(voidBrush, p.X - 24, p.Y - 28, 48, 56);
                g.FillRectangle(metal, p.X - 20, p.Y - 25, 40, 50);
                for (var row = 0; row < 3; row++)
                {
                    g.FillRectangle(dark, p.X - 16, p.Y - 20 + row * 16, 32, 12);
                    g.FillRectangle(row == prop.Variant % 3 ? oxide : bone,
                        p.X - 12, p.Y - 17 + row * 16, 15, 5);
                    g.FillRectangle(signal, p.X + 9, p.Y - 17 + row * 16, 3, 3);
                }
                break;
            case RoomPropKind.PressureTank:
                g.FillRectangle(voidBrush, p.X - 19, p.Y - 29, 38, 58);
                g.FillRectangle(bone, p.X - 14, p.Y - 24, 28, 48);
                g.FillRectangle(metal, p.X - 17, p.Y - 15, 34, 7);
                g.FillRectangle(metal, p.X - 17, p.Y + 10, 34, 7);
                g.FillRectangle(oxide, p.X - 4, p.Y - 30, 8, 8);
                LabFont.Draw(g, "P", p.X, p.Y - 4, 1, C.Ink, LabTextAlign.Center, 0);
                break;
            case RoomPropKind.CableReel:
                g.FillRectangle(voidBrush, p.X - 27, p.Y - 22, 54, 44);
                g.FillRectangle(bone, p.X - 23, p.Y - 18, 46, 36);
                g.FillRectangle(dark, p.X - 16, p.Y - 14, 32, 28);
                g.FillRectangle(metal, p.X - 11, p.Y - 10, 22, 20);
                g.FillRectangle(dark, p.X - 5, p.Y - 5, 10, 10);
                var cableX = side < 0 ? p.X - 42 : p.X + 20;
                g.FillRectangle(oxide, cableX, p.Y + 16, 22, 5);
                break;
            default:
                g.FillRectangle(voidBrush, p.X - 20, p.Y - 23, 40, 46);
                g.FillRectangle(metal, p.X - 15, p.Y - 19, 30, 38);
                g.FillRectangle(dark, p.X - 10, p.Y - 13, 20, 22);
                g.FillRectangle(signal, p.X - 6, p.Y - 9, 12, 10);
                using (var cone = new SolidBrush(Color.FromArgb(25, C.Signal)))
                    g.FillPolygon(cone, [new PointF(p.X - 9, p.Y + 13), new PointF(p.X + 9, p.Y + 13),
                        new PointF(p.X + 28, p.Y + 34), new PointF(p.X - 28, p.Y + 34)]);
                break;
        }
    }

    private PointF RoomPropRenderCenter(RoomProp prop)
    {
        var center = CellCenter(prop.Cell);
        var room = _maze?.GetRoomAt(prop.Cell);
        if (room is null) return center;

        // These props are scenery, not collision geometry. Pinning them toward
        // an exterior wall keeps the center of their traversable tile visually
        // clear, so passing through does not read as a collision or hard clip.
        var outward = new List<Point>(4);
        if (!room.Contains(new Point(prop.Cell.X, prop.Cell.Y - 1))) outward.Add(new Point(0, -1));
        if (!room.Contains(new Point(prop.Cell.X + 1, prop.Cell.Y))) outward.Add(new Point(1, 0));
        if (!room.Contains(new Point(prop.Cell.X, prop.Cell.Y + 1))) outward.Add(new Point(0, 1));
        if (!room.Contains(new Point(prop.Cell.X - 1, prop.Cell.Y))) outward.Add(new Point(-1, 0));
        if (outward.Count == 0) return center;

        var anchor = outward[prop.Variant % outward.Count];
        var offset = _cellSize * .42f;
        return new PointF(center.X + anchor.X * offset, center.Y + anchor.Y * offset);
    }

    private void DrawUtilityCrate(Graphics g, PointF center, float width, float height, int variant,
        bool manifested)
    {
        using var shadow = new SolidBrush(Color.FromArgb(190, C.Void));
        using var shell = new SolidBrush(manifested ? Color.FromArgb(177, 170, 132) : Color.FromArgb(117, 119, 95));
        using var face = new SolidBrush(Color.FromArgb(38, 47, 42));
        using var strap = new SolidBrush(manifested ? C.Signal : Color.FromArgb(102, 67, 48));
        g.FillRectangle(shadow, center.X - width / 2 + 4, center.Y - height / 2 + 5, width, height);
        g.FillRectangle(shell, center.X - width / 2, center.Y - height / 2, width, height);
        g.FillRectangle(face, center.X - width / 2 + 4, center.Y - height / 2 + 4, width - 8, height - 8);
        g.FillRectangle(strap, center.X - 2, center.Y - height / 2, 4, height);
        if ((variant & 1) == 0)
            g.FillRectangle(strap, center.X - width / 2 + 4, center.Y - 2, width - 8, 4);
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

    private void DrawShopKiosk(Graphics g, ShopKiosk kiosk)
    {
        var p = CellCenter(kiosk.Cell);
        var eyeShiftX = MathF.Sin(_time * .71f + kiosk.RoomId) * 3;
        var eyeShiftY = MathF.Sin(_time * .43f + 1.2f) * 2;
        using var shadow = new SolidBrush(Color.FromArgb(180, C.Void));
        using var shell = new SolidBrush(Color.FromArgb(20, 25, 23));
        using var steel = new SolidBrush(Color.FromArgb(86, 91, 73));
        using var bone = new SolidBrush(C.Bone);
        using var pupil = new SolidBrush(C.Void);
        using var signal = new SolidBrush(((int)(_time * 4) & 1) == 0 ? C.Signal : C.Oxide);
        g.FillPolygon(shadow, [new PointF(p.X - 31, p.Y + 30), new PointF(p.X + 36, p.Y + 30),
            new PointF(p.X + 25, p.Y - 29), new PointF(p.X - 23, p.Y - 34)]);
        g.FillPolygon(shell, [new PointF(p.X - 30, p.Y + 25), new PointF(p.X + 30, p.Y + 25),
            new PointF(p.X + 22, p.Y - 31), new PointF(p.X - 19, p.Y - 28)]);
        g.FillRectangle(steel, p.X - 33, p.Y + 13, 66, 14);
        g.FillRectangle(signal, p.X - 27, p.Y + 17, 12, 5);
        for (var eye = -1; eye <= 1; eye += 2)
        {
            var ex = p.X + eye * 9 + eyeShiftX;
            var ey = p.Y - 11 + eyeShiftY;
            g.FillRectangle(bone, ex - 6, ey - 4, 12, 8);
            g.FillRectangle(pupil, ex - 1 + eyeShiftX * .25f, ey - 3 + eyeShiftY * .25f, 4, 6);
        }
        LabFont.Draw(g, "E / TRADE", p.X, p.Y + 34, 1, C.Signal, LabTextAlign.Center, 0);
    }

    private void DrawShopConsole(Graphics g)
    {
        var outer = new RectangleF(48, 63, DesignWidth - 96, DesignHeight - 126);
        DrawCutPanel(g, outer, Color.FromArgb(12, 18, 18), Color.FromArgb(98, C.Steel), 18, 5);
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
        LabFont.Draw(g, "ARROWS  NAVIGATE    ENTER / E  ACCEPT    ESC  BACK / LEAVE",
            outer.X + 24, outer.Bottom - 25, 1, C.Steel);
        LabFont.Draw(g, $"REPAIR {_shopRepairReserve:00}    AEGIS {_shopProtectionCharges:00}    SALVAGE {SellInventory().Sum(x => x.Count):00}",
            outer.Right - 24, outer.Bottom - 25, 1, C.Sick, LabTextAlign.Right);
    }

    private void DrawShopkeeperPortrait(Graphics g, RectangleF rect)
    {
        using var murk = new SolidBrush(Color.FromArgb(10, 12, 11));
        using var black = new SolidBrush(Color.Black);
        using var nearBlack = new SolidBrush(Color.FromArgb(5, 7, 7));
        using var eye = new SolidBrush(C.Bone);
        using var pupil = new SolidBrush(C.Void);
        using var counter = new SolidBrush(Color.FromArgb(72, 70, 57));
        using var rim = new SolidBrush(Color.FromArgb(132, 122, 91));
        g.FillRectangle(murk, rect.X + 9, rect.Y + 9, rect.Width - 18, rect.Height - 18);
        for (var index = 0; index < 10; index++)
        {
            var x = rect.X + 25 + PositiveHash(index * 89 + 13) % (int)(rect.Width - 50);
            var y = rect.Y + 21 + PositiveHash(index * 151 + 7) % (int)(rect.Height - 80);
            g.FillRectangle(nearBlack, x, y, 20 + index * 3 % 50, 5 + index % 3);
        }
        var center = new PointF(rect.X + rect.Width * .52f, rect.Y + rect.Height * .51f);
        var breathe = MathF.Sin(_time * .83f) * 5;
        g.FillPolygon(black,
        [
            new PointF(center.X - 136 - breathe, rect.Bottom - 42),
            new PointF(center.X - 111, center.Y + 16),
            new PointF(center.X - 71, center.Y - 83 - breathe),
            new PointF(center.X - 27, center.Y - 126),
            new PointF(center.X + 53, center.Y - 114 + breathe),
            new PointF(center.X + 104, center.Y - 46),
            new PointF(center.X + 143 + breathe, rect.Bottom - 42)
        ]);
        g.FillRectangle(black, center.X - 132, center.Y + 28, 264, rect.Bottom - center.Y - 55);

        var lookX = MathF.Sin(_time * .57f) * 7;
        var lookY = MathF.Sin(_time * .37f + 1.4f) * 4;
        for (var side = -1; side <= 1; side += 2)
        {
            var ex = center.X + side * 27 + lookX;
            var ey = center.Y - 57 + lookY;
            g.FillRectangle(eye, ex - 13, ey - 7, 26, 14);
            g.FillRectangle(pupil, ex - 3 + lookX * .2f, ey - 6 + lookY * .2f, 7, 12);
        }
        g.FillRectangle(counter, rect.X + 10, rect.Bottom - 68, rect.Width - 20, 58);
        g.FillRectangle(rim, rect.X + 10, rect.Bottom - 68, rect.Width - 20, 8);
        for (var x = rect.X + 27; x < rect.Right - 20; x += 43)
            g.FillRectangle(black, x, rect.Bottom - 51, 19, 5);
        LabFont.Draw(g, "COUNTER-LIGHT HOLDS", rect.X + rect.Width / 2, rect.Bottom - 31,
            1, C.Sick, LabTextAlign.Center, 0);
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
