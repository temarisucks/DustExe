namespace Dust;

internal sealed partial class GameForm
{
    private void DrawInventoryButton(Graphics g)
    {
        _inventoryButton = new RectangleF(
            _mazeRect.X + 73, _mazeRect.Y + 81, 48, 48);
        var rect = _inventoryButton;
        using var shadow = new SolidBrush(Color.FromArgb(205, Color.Black));
        g.FillPolygon(shadow, CutPanelPoints(
            new RectangleF(rect.X + 5, rect.Y + 5, rect.Width, rect.Height), 6));
        DrawCutPanel(g, rect,
            _hoverInventory ? Color.FromArgb(64, 49, 35) : Color.FromArgb(224, C.Ink),
            _hoverInventory ? C.Signal : C.Steel, 6, _hoverInventory ? 3 : 2);

        var bag = new RectangleF(rect.X + 12, rect.Y + 15, 24, 23);
        using var edge = new Pen(_hoverInventory ? C.Signal : C.Bone, 3);
        using var latch = new SolidBrush(_hoverInventory ? C.Signal : C.Oxide);
        g.DrawRectangle(edge, bag.X, bag.Y, bag.Width, bag.Height);
        g.DrawRectangle(edge, bag.X + 6, bag.Y - 6, bag.Width - 12, 8);
        g.FillRectangle(latch, bag.X + 10, bag.Y + 8, 5, 5);
        using var countLamp = new SolidBrush(
            _framePatchInventory + _reconstructionGelInventory + _shopProtectionCharges > 0
                ? C.Signal : C.Steel);
        g.FillRectangle(countLamp, rect.Right - 9, rect.Y + 5, 4, 4);
    }

    private void DrawPlayerInventory(Graphics g)
    {
        using var veil = new SolidBrush(Color.FromArgb(188, 3, 7, 7));
        g.FillRectangle(veil, _mazeRect);

        _inventoryPanel = new RectangleF(342, 128, 596, 526);
        using var shadow = new SolidBrush(Color.FromArgb(230, Color.Black));
        g.FillPolygon(shadow, CutPanelPoints(
            new RectangleF(_inventoryPanel.X + 12, _inventoryPanel.Y + 14,
                _inventoryPanel.Width, _inventoryPanel.Height), 18));
        DrawCutPanel(g, _inventoryPanel, Color.FromArgb(32, 39, 34),
            C.Steel, 18, 5);

        using var rail = new SolidBrush(Color.FromArgb(75, 77, 61));
        g.FillRectangle(rail, _inventoryPanel.X + 22, _inventoryPanel.Y + 24,
            _inventoryPanel.Width - 44, 48);
        LabFont.Draw(g, "FIELD INVENTORY", _inventoryPanel.X + 42,
            _inventoryPanel.Y + 39, 2, C.Bone);
        LabFont.Draw(g, $"FRAME {RemainingHealth:00}/{GetMaximumHealth():00}",
            _inventoryPanel.Right - 38, _inventoryPanel.Y + 40, 1,
            RemainingHealth < GetMaximumHealth() ? C.Signal : C.Sick,
            LabTextAlign.Right);

        _inventoryCloseButton = new RectangleF(
            _inventoryPanel.Right - 49, _inventoryPanel.Y + 11, 31, 27);
        DrawCutPanel(g, _inventoryCloseButton, C.Ink,
            _hoverInventoryClose ? C.Signal : C.Oxide, 4, 2);
        using (var close = new Pen(_hoverInventoryClose ? C.Signal : C.Bone, 3))
        {
            g.DrawLine(close, _inventoryCloseButton.X + 9,
                _inventoryCloseButton.Y + 7, _inventoryCloseButton.Right - 9,
                _inventoryCloseButton.Bottom - 7);
            g.DrawLine(close, _inventoryCloseButton.Right - 9,
                _inventoryCloseButton.Y + 7, _inventoryCloseButton.X + 9,
                _inventoryCloseButton.Bottom - 7);
        }

        var kinds = new[]
        {
            ShopItemKind.FramePatch,
            ShopItemKind.ReconstructionGel,
            ShopItemKind.AegisFuse
        };
        var names = new[] { "FRAME PATCH", "RECONSTRUCTION GEL", "AEGIS FUSE" };
        var notes = new[]
        {
            "RESTORES 01 INTEGRITY",
            "RESTORES UP TO 02 INTEGRITY",
            _shopProtectionArmed
                ? "WARD ARMED / NEXT IMPACT NULL"
                : $"ARM CHANNEL / {(HasActivePerkEquipped ? "J" : "SPACE")}"
        };

        for (var index = 0; index < _inventoryRows.Length; index++)
        {
            var row = new RectangleF(_inventoryPanel.X + 28,
                _inventoryPanel.Y + 92 + index * 106,
                _inventoryPanel.Width - 56, 88);
            _inventoryRows[index] = row;
            var selected = _inventorySelection == index;
            var hovered = _hoverInventoryRow == index;
            DrawCutPanel(g, row,
                selected ? Color.FromArgb(52, 56, 43) : Color.FromArgb(18, 25, 23),
                selected || hovered ? C.Signal : C.Steel, 10,
                selected ? 4 : 2);
            if (selected) DrawKeyboardFocusMarker(g, row);

            var socket = new RectangleF(row.X + 14, row.Y + 13, 61, 61);
            DrawCutPanel(g, socket, Color.FromArgb(226, C.Ink),
                InventoryCount(kinds[index]) > 0 ? C.Sick : C.Steel, 6, 2);
            DrawInventoryItemGlyph(g, kinds[index],
                RectangleF.Inflate(socket, -11, -11),
                InventoryCount(kinds[index]) > 0 ? C.Bone : C.Steel);
            LabFont.Draw(g, names[index], row.X + 94, row.Y + 17, 2,
                InventoryCount(kinds[index]) > 0 ? C.Bone : C.Steel);
            LabFont.Draw(g, notes[index], row.X + 94, row.Y + 55, 1,
                index == 2 && _shopProtectionArmed ? C.Signal : C.Sick);
            LabFont.Draw(g, $"X{InventoryCount(kinds[index]):00}",
                row.Right - 22, row.Y + 35, 2,
                InventoryCount(kinds[index]) > 0 ? C.Signal : C.Steel,
                LabTextAlign.Right);
        }

        _inventoryUseButton = new RectangleF(
            _inventoryPanel.X + 161, _inventoryPanel.Bottom - 88, 274, 52);
        var usable = _inventorySelection switch
        {
            0 => _framePatchInventory > 0 && _damageTaken > 0,
            1 => _reconstructionGelInventory > 0 && _damageTaken > 0,
            _ => _shopProtectionCharges > 0 && !_shopProtectionArmed
        };
        DrawCutPanel(g, _inventoryUseButton,
            usable ? Color.FromArgb(51, 52, 38) : Color.FromArgb(18, 23, 22),
            _hoverInventoryUse ? C.Signal : usable ? C.Sick : C.Steel, 9, 3);
        LabFont.Draw(g, _inventorySelection == 2 ? "ARM SELECTED" : "USE SELECTED",
            _inventoryUseButton.X + _inventoryUseButton.Width / 2,
            _inventoryUseButton.Y + 19, 2,
            usable ? C.Bone : C.Steel, LabTextAlign.Center);
    }

    private static void DrawInventoryItemGlyph(
        Graphics g, ShopItemKind kind, RectangleF rect, Color color)
    {
        using var brush = new SolidBrush(color);
        using var pen = new Pen(color, 3);
        var center = new PointF(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        switch (kind)
        {
            case ShopItemKind.FramePatch:
                g.FillRectangle(brush, center.X - 4, rect.Y + 3, 8, rect.Height - 6);
                g.FillRectangle(brush, rect.X + 3, center.Y - 4, rect.Width - 6, 8);
                break;
            case ShopItemKind.ReconstructionGel:
                g.DrawRectangle(pen, rect.X + 10, rect.Y + 2,
                    rect.Width - 20, rect.Height - 4);
                g.FillRectangle(brush, rect.X + 14, rect.Bottom - 15,
                    rect.Width - 28, 9);
                g.FillRectangle(brush, center.X - 5, rect.Y - 2, 10, 7);
                break;
            case ShopItemKind.AegisFuse:
            {
                var points = new[]
                {
                    new PointF(center.X, rect.Y),
                    new PointF(rect.Right - 3, rect.Y + 8),
                    new PointF(rect.Right - 6, center.Y + 8),
                    new PointF(center.X, rect.Bottom),
                    new PointF(rect.X + 6, center.Y + 8),
                    new PointF(rect.X + 3, rect.Y + 8)
                };
                g.DrawPolygon(pen, points);
                g.FillRectangle(brush, center.X - 3, rect.Y + 9, 6, rect.Height - 18);
                break;
            }
        }
    }
}
