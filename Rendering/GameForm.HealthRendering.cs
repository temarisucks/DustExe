namespace Dust;

internal sealed partial class GameForm
{
    private void DrawHealthMonitor(Graphics g, RectangleF telemetryPanel)
    {
        var maximum = Math.Max(1, GetMaximumHealth());
        var remaining = RemainingHealth;
        const float segmentWidth = 13;
        const float segmentGap = 5;
        var stripWidth = maximum * segmentWidth + (maximum - 1) * segmentGap;
        var x = telemetryPanel.Right - 13 - stripWidth;
        var y = telemetryPanel.Y + 35;
        LabFont.Draw(g, "FRAME", x - 54, y + 3, 1,
            remaining <= 1 ? C.Red : C.Steel, LabTextAlign.Left, 0);

        using var socket = new SolidBrush(Color.FromArgb(4, 8, 8));
        using var live = new SolidBrush(remaining <= 1 ? C.Signal : C.Bone);
        using var breached = new SolidBrush(Color.FromArgb(112, 43, 37));
        using var fracture = new Pen(C.Red, 2);
        for (var i = 0; i < maximum; i++)
        {
            var segment = new RectangleF(x + i * (segmentWidth + segmentGap), y, segmentWidth, 14);
            g.FillRectangle(socket, segment.X - 2, segment.Y - 2, segment.Width + 4, segment.Height + 4);
            if (i < remaining)
            {
                g.FillRectangle(live, segment);
                g.FillRectangle(socket, segment.X + 3, segment.Y + 3, segment.Width - 6, 3);
            }
            else
            {
                g.FillRectangle(breached, segment);
                g.DrawLine(fracture, segment.X + 1, segment.Bottom - 2, segment.Right - 1, segment.Y + 2);
            }
        }
    }

    private void DrawFailureOverlay(Graphics g)
    {
        using var blackout = new SolidBrush(Color.FromArgb(242, 1, 4, 4));
        g.FillRectangle(blackout, _mazeRect);

        // Frozen registration bands leave the failed camera feed visible as a dead instrument.
        using var dropout = new SolidBrush(Color.FromArgb(67, C.Oxide));
        for (var y = _mazeRect.Y + 31; y < _mazeRect.Bottom; y += 67)
        {
            var width = 74 + ((int)y * 17 % 210);
            var rightAligned = ((int)y / 67 & 1) == 0;
            g.FillRectangle(dropout, rightAligned ? _mazeRect.Right - width : _mazeRect.X, y, width, 3);
        }

        var panel = new RectangleF(_mazeRect.X + (_mazeRect.Width - 844) / 2,
            _mazeRect.Y + 92, 844, 492);
        using var panelShadow = new SolidBrush(Color.Black);
        g.FillRectangle(panelShadow, panel.X + 14, panel.Y + 16, panel.Width, panel.Height);
        DrawCutPanel(g, panel, Color.FromArgb(15, 21, 20), Color.FromArgb(112, 55, 46), 22, 5);
        DrawPanelBolts(g, panel, C.Oxide);

        using var alarmRail = new SolidBrush(C.Red);
        using var deadRail = new SolidBrush(Color.FromArgb(65, 46, 40));
        g.FillRectangle(deadRail, panel.X + 28, panel.Y + 27, panel.Width - 56, 18);
        for (var x = panel.X + 34; x < panel.Right - 38; x += 38)
            if ((((int)(x / 38) + (int)(_time * 3)) & 1) == 0)
                g.FillRectangle(alarmRail, x, panel.Y + 31, 20, 10);

        LabFont.Draw(g, "CARRIER SIGNAL LOST", panel.X + 44, panel.Y + 66, 4, C.Bone, tracking: 1);
        LabFont.Draw(g, "AUTOMATED TERMINATION SLIP / LOT 31", panel.Right - 42, panel.Y + 111,
            1, C.Oxide, LabTextAlign.Right);

        var specimen = new RectangleF(panel.X + 40, panel.Y + 126, 292, 230);
        DrawCutPanel(g, specimen, Color.FromArgb(4, 9, 9), Color.FromArgb(75, 83, 69), 13, 3);
        DrawReticle(g, new PointF(specimen.X + specimen.Width / 2, specimen.Y + 101),
            72, Color.FromArgb(92, C.Red));
        DrawDrone(g, _drone, _playerColor, _playerFrameColor,
            new PointF(specimen.X + specimen.Width / 2, specimen.Y + 101), 59, 238,
            drawShadow: false, drawBrackets: false, showDamage: true);
        using (var scan = new SolidBrush(Color.FromArgb(56, C.Red)))
        {
            var scanY = specimen.Y + 12 + (_time * 23 % (specimen.Height - 58));
            g.FillRectangle(scan, specimen.X + 12, scanY, specimen.Width - 24, 4);
        }
        LabFont.Draw(g, "NO CARRIER RESPONSE", specimen.X + specimen.Width / 2,
            specimen.Bottom - 34, 1, C.Red, LabTextAlign.Center);

        var reportX = panel.X + 370;
        var reportY = panel.Y + 137;
        DrawFailureReadout(g, reportX, reportY, "STRUCTURAL LIMIT",
            $"{_damageTaken:00}/{GetMaximumHealth():00} BREACHES", true);
        DrawFailureReadout(g, reportX, reportY + 58, "FEED DURATION",
            $"{(int)_failedTime.TotalMinutes:00}:{_failedTime.Seconds:00}", false);
        DrawFailureReadout(g, reportX, reportY + 116, "MISSION PROPERTY",
            _cargoLostOnFailure ? "RELEASED IN CHAMBER" : "NO LATCH DETECTED", false);
        var disposition = !_onlineMatchActive
            ? "RESEED OR EJECT"
            : IsOnlineLobbyHost ? "RETURN OR LEAVE" : "WAIT OR LEAVE";
        DrawFailureReadout(g, reportX, reportY + 174, "DISPOSITION",
            disposition, true);

        using var flatline = new Pen(Color.FromArgb(180, C.Red), 3);
        var lineY = panel.Y + 373;
        g.DrawLine(flatline, panel.X + 42, lineY, panel.X + 154, lineY);
        g.DrawLines(flatline,
        [
            new PointF(panel.X + 154, lineY), new PointF(panel.X + 164, lineY - 19),
            new PointF(panel.X + 176, lineY + 22), new PointF(panel.X + 188, lineY)
        ]);
        g.DrawLine(flatline, panel.X + 188, lineY, panel.Right - 42, lineY);

        _againButton = new RectangleF(panel.X + 42, panel.Bottom - 84, 420, 54);
        _menuButton = new RectangleF(panel.Right - 276, panel.Bottom - 84, 234, 54);
        var primaryLabel = !_onlineMatchActive
            ? "RESEED MAZE"
            : IsOnlineLobbyHost ? "RETURN TO LOBBY" : "WAIT FOR HOST";
        var exitLabel = _onlineMatchActive ? "LEAVE LOBBY" : "EJECT";
        DrawLatchButton(g, _againButton, primaryLabel, _hoverOverlay == 0);
        DrawAbortButton(g, _menuButton, exitLabel, _hoverOverlay == 1);
        LabFont.Draw(g, _onlineMatchActive ? "ENTER  RETURN" : "ENTER  RESEED",
            panel.X + 42, panel.Bottom + 18, 1, C.Signal);
        LabFont.Draw(g, _onlineMatchActive ? "ESC  LEAVE LOBBY" : "ESC  EJECT TO ROUTING",
            panel.Right - 42, panel.Bottom + 18,
            1, C.Steel, LabTextAlign.Right);
    }

    private static void DrawFailureReadout(Graphics g, float x, float y, string label,
        string value, bool alarm)
    {
        using var rule = new SolidBrush(alarm ? Color.FromArgb(104, 47, 41) : Color.FromArgb(55, 65, 56));
        g.FillRectangle(rule, x, y + 40, 410, 3);
        LabFont.Draw(g, label, x, y, 1, C.Steel);
        LabFont.Draw(g, value, x, y + 21, 1, alarm ? C.Red : C.Bone);
    }
}
