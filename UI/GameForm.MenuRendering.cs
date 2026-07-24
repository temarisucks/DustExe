namespace Dust;

internal sealed partial class GameForm
{
    private static readonly string[] MenuRouteNames =
        ["OFFLINE PLAY", "ONLINE PLAY", "CUSTOMIZE", "ACHIEVEMENTS", "SETTINGS"];
    private static readonly string[] DroneModelNames = ["MITE", "KITE", "TRIAD", "CICADA", "CRADLE"];
    private static readonly string[] PaintPartNames = ["CORE", "FRAME"];
    private static readonly string[] PaintColorCodes =
    [
        "PHOS", "ARTL", "IODN", "COOL", "BRSE", "CERA",
        "HZRD", "STER", "RUST", "MOSS", "ROSE", "GRPH"
    ];

    private void DrawTitleMenu(Graphics g)
    {
        var shell = new RectangleF(42, 54, DesignWidth - 84, DesignHeight - 108);
        DrawMenuConsoleShell(g, shell, string.Empty);

        var specimenBay = new RectangleF(72, 106, 688, 546);
        DrawCutPanel(g, specimenBay, Color.FromArgb(12, 19, 19), Color.FromArgb(69, 82, 71), 18, 4);

        // A single oxidized bus physically joins the specimen bay to every route.
        using var busShadow = new SolidBrush(Color.Black);
        using var bus = new SolidBrush(Color.FromArgb(91, 86, 68));
        using var busHot = new SolidBrush(C.Oxide);
        g.FillRectangle(busShadow, 778, 174, 22, 468);
        g.FillRectangle(bus, 782, 170, 14, 468);
        g.FillRectangle(busHot, 786, 182, 6, 442);

        LabFont.Draw(g, "DUST", 110, 142, 14, C.Bone, tracking: 0);

        var observationTank = new RectangleF(104, 330, 608, 252);
        DrawCutPanel(g, observationTank, Color.FromArgb(5, 11, 11), Color.FromArgb(72, 88, 74), 16, 4);
        DrawPanelBolts(g, observationTank, C.Steel);
        var droneCenter = new PointF(observationTank.X + observationTank.Width / 2,
            observationTank.Y + observationTank.Height / 2);
        DrawReticle(g, droneCenter, 91, Color.FromArgb(88, C.Steel));
        DrawDrone(g, _drone, _playerColor, _playerFrameColor, droneCenter, 77,
            255, drawShadow: true, drawBrackets: false);

        using (var scan = new SolidBrush(Color.FromArgb(46, C.Signal)))
        {
            var scanY = observationTank.Y + 18 + ((_time * 34) % (observationTank.Height - 36));
            g.FillRectangle(scan, observationTank.X + 18, scanY, observationTank.Width - 36, 3);
        }

        for (var i = 0; i < _titleButtons.Length; i++)
        {
            var rect = new RectangleF(820, 176 + i * 92, 336, 72);
            _titleButtons[i] = rect;
            var focused = _menuSelection == i;
            var hovered = _hoverMenu == i;
            DrawMenuRouteCartridge(g, rect, MenuRouteNames[i], focused, hovered);

            using var branch = new SolidBrush(focused || hovered ? C.Signal : C.Steel);
            g.FillRectangle(branch, 792, rect.Y + 34, rect.X - 792, 12);
            g.FillRectangle(branch, rect.X - 10, rect.Y + 25, 10, 30);
            if (focused) DrawKeyboardFocusMarker(g, rect);
        }
    }

    private void DrawCustomizeConsole(Graphics g)
    {
        var shell = new RectangleF(42, 54, DesignWidth - 84, DesignHeight - 108);
        DrawMenuConsoleShell(g, shell, "AIRFRAME COATING / SUBJECT 31");

        LabFont.Draw(g, "CUSTOMIZE", 72, 74, 3, C.Bone);

        var preview = new RectangleF(72, 132, 384, 506);
        DrawCustomizePreviewUnit(g, preview);

        var controls = new RectangleF(486, 132, 702, 506);
        DrawCutPanel(g, controls, Color.FromArgb(16, 24, 23), Color.FromArgb(73, 85, 72), 16, 4);
        DrawPanelBolts(g, controls, C.Steel);
        using (var rail = new SolidBrush(C.Oxide))
        {
            g.FillRectangle(rail, controls.X + 18, controls.Y + 54, controls.Width - 36, 4);
            g.FillRectangle(rail, controls.X + 18, controls.Y + 212, controls.Width - 36, 4);
            g.FillRectangle(rail, controls.X + 18, controls.Y + 318, controls.Width - 36, 4);
        }

        LabFont.Draw(g, "01  AIRFRAME SOCKET", controls.X + 26, controls.Y + 25, 2, C.Signal);
        const float airframeGap = 8;
        var airframeWidth = (controls.Width - 52 - airframeGap * (_droneButtons.Length - 1)) /
                            _droneButtons.Length;
        for (var i = 0; i < _droneButtons.Length; i++)
        {
            var rect = new RectangleF(controls.X + 26 + i * (airframeWidth + airframeGap),
                controls.Y + 72, airframeWidth, 126);
            _droneButtons[i] = rect;
            DrawCustomizeAirframeSocket(g, rect, (DroneModel)i, DroneModelNames[i],
                _drone == (DroneModel)i, _hoverDrone == i, _customizeSection == 0 && _customizeIndex == i);
        }

        LabFont.Draw(g, "02  PAINT TARGET", controls.X + 26, controls.Y + 232, 2, C.Signal);
        var targetWidth = (controls.Width - 62) / 2;
        for (var i = 0; i < _paintPartButtons.Length; i++)
        {
            var rect = new RectangleF(controls.X + 26 + i * (targetWidth + 10), controls.Y + 262, targetWidth, 48);
            _paintPartButtons[i] = rect;
            DrawPaintTargetRelay(g, rect, (DronePaintPart)i, PaintPartNames[i],
                _paintPart == (DronePaintPart)i, _hoverPaintPart == i,
                _customizeSection == 1 && _customizeIndex == i);
        }

        LabFont.Draw(g, "03  COLOR BANK", controls.X + 26, controls.Y + 338, 2, C.Signal);
        const float colorGap = 10;
        var colorWidth = (controls.Width - 52 - colorGap * 5) / 6;
        var activePaint = _paintPart == DronePaintPart.Core ? _playerColor : _playerFrameColor;
        for (var i = 0; i < _colorButtons.Length; i++)
        {
            var row = i / 6;
            var column = i % 6;
            var rect = new RectangleF(controls.X + 26 + column * (colorWidth + colorGap),
                controls.Y + 370 + row * 62, colorWidth, 52);
            _colorButtons[i] = rect;
            DrawColorBankCell(g, rect, _palette[i], PaintColorCodes[i],
                activePaint.ToArgb() == _palette[i].ToArgb(), _hoverColor == i,
                _customizeSection == 2 && _customizeIndex == i);
        }

        _backButton = new RectangleF(72, 666, 188, 56);
        DrawAbortButton(g, _backButton, "BACK", _hoverBack || _customizeSection == 3);
        if (_customizeSection == 3) DrawKeyboardFocusMarker(g, _backButton);
    }

    private void DrawSettingsConsole(Graphics g)
    {
        var shell = new RectangleF(42, 54, DesignWidth - 84, DesignHeight - 108);
        DrawMenuConsoleShell(g, shell, "ROOM CONTROL / NONCLINICAL ADJUSTMENT");

        LabFont.Draw(g, "SETTINGS", 72, 74, 3, C.Bone);

        var meterBay = new RectangleF(72, 132, 362, 506);
        DrawCutPanel(g, meterBay, Color.FromArgb(10, 17, 17), Color.FromArgb(73, 86, 73), 16, 4);
        DrawPanelBolts(g, meterBay, C.Steel);
        LabFont.Draw(g, "ENVIRONMENT TRACE", meterBay.X + 24, meterBay.Y + 24, 2, C.Signal);
        LabFont.Draw(g, "NO SUBJECT PRESENT", meterBay.X + 24, meterBay.Y + 55, 1, C.Steel);

        var scope = new RectangleF(meterBay.X + 24, meterBay.Y + 91, meterBay.Width - 48, 160);
        DrawCutPanel(g, scope, Color.FromArgb(4, 10, 10), Color.FromArgb(52, 68, 59), 10, 3);
        DrawWaveform(g, new RectangleF(scope.X + 15, scope.Y + 42, scope.Width - 30, 58), C.Sick,
            _time * (.5f + _settings.Volume / 100f));
        using (var sweep = new SolidBrush(Color.FromArgb(42, C.Signal)))
        {
            var sweepX = scope.X + 12 + ((_time * 52) % (scope.Width - 24));
            g.FillRectangle(sweep, sweepX, scope.Y + 12, 3, scope.Height - 24);
        }
        LabFont.Draw(g, $"LUMA {_settings.Brightness:000}", scope.X + 16, scope.Y + 15, 1, C.Oxide);
        LabFont.Draw(g, $"AUDIO {_settings.Volume:000}", scope.Right - 16, scope.Bottom - 25, 1, C.Sick,
            LabTextAlign.Right);

        LabFont.Draw(g, "ROOM LOAD", meterBay.X + 24, meterBay.Y + 281, 1, C.Steel);
        DrawSegmentMeter(g, new RectangleF(meterBay.X + 24, meterBay.Y + 307, meterBay.Width - 48, 26),
            _settings.Brightness - 50, 100, C.Signal);
        LabFont.Draw(g, "SPEAKER RETURN", meterBay.X + 24, meterBay.Y + 357, 1, C.Steel);
        DrawSegmentMeter(g, new RectangleF(meterBay.X + 24, meterBay.Y + 383, meterBay.Width - 48, 26),
            _settings.Volume, 100, C.Sick);
        LabFont.Draw(g, _settings.Fullscreen ? "FIELD SEAL  ON" : "FIELD SEAL  OFF",
            meterBay.X + 24, meterBay.Bottom - 54, 1, _settings.Fullscreen ? C.Signal : C.Oxide);
        LabFont.Draw(g, "CHANGES RETAINED", meterBay.X + 24, meterBay.Bottom - 28, 1, C.Steel);

        // The vertical bus and its four branch arms make the controls read as one apparatus.
        using (var shadow = new SolidBrush(Color.Black))
        using (var rail = new SolidBrush(Color.FromArgb(96, 88, 67)))
        using (var hot = new SolidBrush(C.Oxide))
        {
            g.FillRectangle(shadow, 451, 154, 22, 438);
            g.FillRectangle(rail, 455, 150, 14, 438);
            g.FillRectangle(hot, 459, 162, 6, 412);
        }

        var labels = new[] { "BRIGHTNESS", "VOLUME", "RESOLUTION", "FULLSCREEN" };
        var values = new[]
        {
            $"{_settings.Brightness:000} PERCENT",
            $"{_settings.Volume:000} PERCENT",
            SettingsCatalog.Resolutions[_settings.ResolutionIndex].Label,
            _settings.Fullscreen ? "SEALED" : "WINDOWED"
        };
        var annotations = new[]
        {
            "OPTICAL GATE",
            "RETURN BUS",
            "VIEW PLATE",
            "FIELD LATCH"
        };

        for (var i = 0; i < _settingsRows.Length; i++)
        {
            var rect = new RectangleF(490, 142 + i * 119, 698, 99);
            _settingsRows[i] = rect;
            DrawSettingsInstrument(g, rect, i, labels[i], values[i], annotations[i],
                _settingsSelection == i, _hoverSetting == i);

            using var branch = new SolidBrush(_settingsSelection == i || _hoverSetting == i ? C.Signal : C.Steel);
            g.FillRectangle(branch, 469, rect.Y + 43, rect.X - 469, 12);
            g.FillRectangle(branch, rect.X - 9, rect.Y + 32, 9, 34);
        }

        _backButton = new RectangleF(72, 666, 188, 56);
        DrawAbortButton(g, _backButton, "BACK", _hoverBack);
        LabFont.Draw(g, "SETTINGS WRITE AUTOMATICALLY", 1188, 690, 1, C.Signal, LabTextAlign.Right);
    }

    private static void DrawMenuConsoleShell(Graphics g, RectangleF rect, string stamp)
    {
        DrawCutPanel(g, rect, C.Ink, Color.FromArgb(78, 90, 75), 20, 5);
        var inner = RectangleF.Inflate(rect, -14, -14);
        DrawCutPanel(g, inner, Color.FromArgb(25, 34, 32), Color.FromArgb(49, 62, 55), 13, 2);
        using var throat = new SolidBrush(Color.FromArgb(8, 14, 14));
        using var rule = new SolidBrush(C.Oxide);
        g.FillRectangle(throat, rect.X + 30, rect.Y + 20, rect.Width - 60, 25);
        g.FillRectangle(rule, rect.X + 42, rect.Y + 45, rect.Width - 84, 4);
        if (!string.IsNullOrWhiteSpace(stamp))
            LabFont.Draw(g, stamp, rect.Right - 38, rect.Y + 26, 1, C.Steel, LabTextAlign.Right);
    }

    private static void DrawMenuRouteCartridge(Graphics g, RectangleF rect, string title,
        bool focused, bool hovered)
    {
        var active = focused || hovered;
        DrawLatchButton(g, rect, title, active, showState: false);

        using var socketDark = new SolidBrush(Color.Black);
        using var socket = new SolidBrush(active ? C.Signal : C.Oxide);
        g.FillRectangle(socketDark, rect.X - 15, rect.Y + 20, 18, rect.Height - 40);
        g.FillRectangle(socket, rect.X - 10, rect.Y + 25, 10, rect.Height - 50);
    }

    private void DrawCustomizePreviewUnit(Graphics g, RectangleF rect)
    {
        DrawCutPanel(g, rect, Color.FromArgb(7, 13, 13), Color.FromArgb(72, 87, 74), 16, 4);
        DrawPanelBolts(g, rect, C.Steel);
        LabFont.Draw(g, "CONFIGURED SUBJECT", rect.X + 24, rect.Y + 25, 2, C.Signal);
        LabFont.Draw(g, "LIVE COATING RETURN", rect.X + 24, rect.Y + 56, 1, C.Steel);

        var tank = new RectangleF(rect.X + 24, rect.Y + 89, rect.Width - 48, 274);
        DrawCutPanel(g, tank, Color.FromArgb(3, 9, 9), Color.FromArgb(52, 68, 58), 12, 3);
        var center = new PointF(tank.X + tank.Width / 2, tank.Y + tank.Height / 2 - 7);
        DrawReticle(g, center, 102, Color.FromArgb(90, C.Steel));
        DrawDrone(g, _drone, _playerColor, _playerFrameColor, center, 82,
            255, drawShadow: true, drawBrackets: false);
        using (var scan = new SolidBrush(Color.FromArgb(54, C.Signal)))
        {
            var scanY = tank.Y + 14 + ((_time * 38) % (tank.Height - 28));
            g.FillRectangle(scan, tank.X + 12, scanY, tank.Width - 24, 3);
        }

        LabFont.Draw(g, "CORE", rect.X + 24, rect.Y + 389, 1,
            _paintPart == DronePaintPart.Core ? C.Signal : C.Steel);
        DrawColorSample(g, new RectangleF(rect.X + 24, rect.Y + 412, 132, 28), _playerColor);
        LabFont.Draw(g, "FRAME", rect.X + 196, rect.Y + 389, 1,
            _paintPart == DronePaintPart.Frame ? C.Signal : C.Steel);
        DrawColorSample(g, new RectangleF(rect.X + 196, rect.Y + 412, 132, 28), _playerFrameColor);
        DrawWaveform(g, new RectangleF(rect.X + 24, rect.Bottom - 42, rect.Width - 48, 26), C.Sick, _time);
    }

    private void DrawCustomizeAirframeSocket(Graphics g, RectangleF rect, DroneModel model, string name,
        bool selected, bool hovered, bool focused)
    {
        var hot = selected || hovered || focused;
        DrawCutPanel(g, rect,
            selected ? Color.FromArgb(48, 62, 53) : Color.FromArgb(23, 32, 30),
            hot ? (selected ? C.Oxide : C.Signal) : C.Steel, 9, selected ? 4 : 2);
        DrawDrone(g, model, _playerColor, _playerFrameColor,
            new PointF(rect.X + rect.Width / 2, rect.Y + 51), 31,
            selected ? 255 : 190, drawShadow: true, drawBrackets: false);
        LabFont.Draw(g, name, rect.X + rect.Width / 2, rect.Bottom - 27, 1,
            hot ? C.Bone : C.Sick, LabTextAlign.Center);
        using var pin = new SolidBrush(selected ? C.Signal : C.Steel);
        g.FillRectangle(pin, rect.X + 11, rect.Y + 10, 8, 8);
        g.FillRectangle(pin, rect.Right - 19, rect.Y + 10, 8, 8);
        if (focused) DrawKeyboardFocusMarker(g, rect);
    }

    private void DrawPaintTargetRelay(Graphics g, RectangleF rect, DronePaintPart part, string label,
        bool selected, bool hovered, bool focused)
    {
        var hot = selected || hovered || focused;
        DrawCutPanel(g, rect, selected ? Color.FromArgb(54, 58, 45) : Color.FromArgb(21, 29, 28),
            hot ? C.Signal : C.Steel, 8, selected ? 4 : 2);
        var color = part == DronePaintPart.Core ? _playerColor : _playerFrameColor;
        DrawColorSample(g, new RectangleF(rect.X + 16, rect.Y + 13, 58, 22), color);
        LabFont.Draw(g, label, rect.X + 91, rect.Y + 16, 1, hot ? C.Bone : C.Sick);
        LabFont.Draw(g, selected ? "ARMED" : "IDLE", rect.Right - 15, rect.Y + 16, 1,
            selected ? C.Signal : C.Steel, LabTextAlign.Right);
        if (focused) DrawKeyboardFocusMarker(g, rect);
    }

    private void DrawColorBankCell(Graphics g, RectangleF rect, Color color, string code,
        bool selected, bool hovered, bool focused)
    {
        var hot = selected || hovered || focused;
        using var edge = new SolidBrush(hot ? (selected ? C.Signal : C.Bone) : C.Steel);
        using var dark = new SolidBrush(Color.FromArgb(9, 15, 15));
        using var sample = new SolidBrush(color);
        g.FillRectangle(edge, rect);
        g.FillRectangle(dark, rect.X + 3, rect.Y + 3, rect.Width - 6, rect.Height - 6);
        g.FillRectangle(sample, rect.X + 10, rect.Y + 10, rect.Width - 20, 18);
        g.FillRectangle(edge, rect.X + 10, rect.Y + 33, rect.Width - 20, 3);
        LabFont.Draw(g, code, rect.X + rect.Width / 2, rect.Bottom - 13, 1,
            hot ? C.Bone : C.Steel, LabTextAlign.Center, 0);
        if (focused) DrawKeyboardFocusMarker(g, rect);
    }

    private void DrawSettingsInstrument(Graphics g, RectangleF rect, int index, string label, string value,
        string annotation, bool selected, bool hovered)
    {
        var active = selected || hovered;
        DrawCutPanel(g, rect, selected ? Color.FromArgb(38, 49, 43) : Color.FromArgb(18, 27, 25),
            active ? C.Signal : C.Steel, 12, selected ? 4 : 2);
        DrawPanelBolts(g, rect, active ? C.Signal : C.Steel);

        LabFont.Draw(g, $"{index + 1:00}  {label}", rect.X + 25, rect.Y + 17, 2,
            active ? C.Bone : C.Sick);
        LabFont.Draw(g, annotation, rect.X + 26, rect.Bottom - 24, 1, active ? C.Oxide : C.Steel);

        _settingsDecreaseButtons[index] = new RectangleF(rect.Right - 330, rect.Y + 15, 55, rect.Height - 30);
        _settingsIncreaseButtons[index] = new RectangleF(rect.Right - 73, rect.Y + 15, 55, rect.Height - 30);
        DrawInstrumentKey(g, _settingsDecreaseButtons[index], "-", active);
        DrawInstrumentKey(g, _settingsIncreaseButtons[index], "+", active);

        var valueBay = new RectangleF(rect.Right - 267, rect.Y + 15, 186, rect.Height - 30);
        using var valueDark = new SolidBrush(Color.FromArgb(3, 9, 9));
        using var valueEdge = new Pen(active ? C.Signal : C.Steel, 2);
        g.FillRectangle(valueDark, valueBay);
        g.DrawRectangle(valueEdge, valueBay.X, valueBay.Y, valueBay.Width, valueBay.Height);
        LabFont.Draw(g, value, valueBay.X + valueBay.Width / 2, valueBay.Y + valueBay.Height / 2 - 7, 1,
            active ? C.Signal : C.Sick, LabTextAlign.Center, 0);
        if (selected) DrawKeyboardFocusMarker(g, rect);
    }

    private void DrawKeyboardFocusMarker(Graphics g, RectangleF rect)
    {
        var pulse = .72f + .28f * (MathF.Sin(_time * 7.5f) * .5f + .5f);
        var signal = Color.FromArgb((int)(255 * pulse), C.Signal);
        var bone = Color.FromArgb((int)(230 * pulse), C.Bone);
        using var edge = new Pen(bone, 3);
        using var pointer = new SolidBrush(signal);
        using var core = new SolidBrush(C.Bone);

        var outer = RectangleF.Inflate(rect, -5, -5);
        const float corner = 15;
        g.DrawLine(edge, outer.Left, outer.Top + corner, outer.Left, outer.Top);
        g.DrawLine(edge, outer.Left, outer.Top, outer.Left + corner, outer.Top);
        g.DrawLine(edge, outer.Right - corner, outer.Top, outer.Right, outer.Top);
        g.DrawLine(edge, outer.Right, outer.Top, outer.Right, outer.Top + corner);
        g.DrawLine(edge, outer.Left, outer.Bottom - corner, outer.Left, outer.Bottom);
        g.DrawLine(edge, outer.Left, outer.Bottom, outer.Left + corner, outer.Bottom);
        g.DrawLine(edge, outer.Right - corner, outer.Bottom, outer.Right, outer.Bottom);
        g.DrawLine(edge, outer.Right, outer.Bottom, outer.Right, outer.Bottom - corner);

        var centerY = rect.Y + rect.Height / 2;
        g.FillPolygon(pointer,
        [
            new PointF(rect.X - 17, centerY - 10),
            new PointF(rect.X - 5, centerY),
            new PointF(rect.X - 17, centerY + 10)
        ]);
        g.FillRectangle(core, rect.X + 7, centerY - 3, 12, 6);
    }

    private static void DrawInstrumentKey(Graphics g, RectangleF rect, string mark, bool active)
    {
        DrawCutPanel(g, rect, Color.FromArgb(27, 34, 30), active ? C.Signal : C.Steel, 7, 3);
        using var cap = new SolidBrush(active ? C.Bone : C.Sick);
        g.FillRectangle(cap, rect.X + 13, rect.Y + 11, rect.Width - 26, rect.Height - 22);
        LabFont.Draw(g, mark, rect.X + rect.Width / 2, rect.Y + rect.Height / 2 - 8, 2,
            C.Ink, LabTextAlign.Center, 0);
    }

    private static void DrawSegmentMeter(Graphics g, RectangleF rect, int value, int maximum, Color color)
    {
        using var off = new SolidBrush(Color.FromArgb(40, C.Steel));
        using var on = new SolidBrush(color);
        const int segments = 16;
        const float gap = 4;
        var segmentWidth = (rect.Width - gap * (segments - 1)) / segments;
        var lit = (int)MathF.Round(Math.Clamp(value / (float)Math.Max(1, maximum), 0, 1) * segments);
        for (var i = 0; i < segments; i++)
            g.FillRectangle(i < lit ? on : off, rect.X + i * (segmentWidth + gap), rect.Y, segmentWidth, rect.Height);
    }

    private static void DrawColorSample(Graphics g, RectangleF rect, Color color)
    {
        using var shadow = new SolidBrush(Color.Black);
        using var edge = new SolidBrush(C.Steel);
        using var sample = new SolidBrush(color);
        g.FillRectangle(shadow, rect.X - 3, rect.Y - 3, rect.Width + 6, rect.Height + 6);
        g.FillRectangle(edge, rect);
        g.FillRectangle(sample, rect.X + 4, rect.Y + 4, rect.Width - 8, rect.Height - 8);
    }

    private static void DrawPanelBolts(Graphics g, RectangleF rect, Color color)
    {
        using var recess = new SolidBrush(Color.Black);
        using var bolt = new SolidBrush(color);
        var points = new[]
        {
            new PointF(rect.X + 14, rect.Y + 14),
            new PointF(rect.Right - 14, rect.Y + 14),
            new PointF(rect.X + 14, rect.Bottom - 14),
            new PointF(rect.Right - 14, rect.Bottom - 14)
        };
        foreach (var point in points)
        {
            g.FillRectangle(recess, point.X - 5, point.Y - 5, 10, 10);
            g.FillRectangle(bolt, point.X - 2, point.Y - 4, 4, 8);
        }
    }
}
