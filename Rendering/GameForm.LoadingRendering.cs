namespace Dust;

internal sealed partial class GameForm
{
    private void DrawLoadingConsole(Graphics g)
    {
        var shell = new RectangleF(42, 54, DesignWidth - 84, DesignHeight - 108);
        DrawMenuConsoleShell(g, shell, "FIELD INTAKE / CASSETTE PREPARATION");

        var faultColor = _loadingFault ? C.Red : C.Signal;
        LabFont.Draw(g, "INITIALIZING", 72, 76, 3, C.Bone);
        // Keep the live cycle marker below the stamped shell legend so both
        // remain legible at the native 1280x800 presentation.
        LabFont.Draw(g, $"CYCLE {_level:00}", 1188, 109, 1, C.Oxide, LabTextAlign.Right);

        var bench = new RectangleF(72, 132, 1116, 490);
        DrawCutPanel(g, bench, Color.FromArgb(13, 20, 19), Color.FromArgb(75, 87, 72), 18, 4);
        DrawPanelBolts(g, bench, C.Steel);

        var signalWell = new RectangleF(104, 166, 710, 334);
        DrawCutPanel(g, signalWell, Color.FromArgb(3, 8, 8), Color.FromArgb(58, 72, 62), 14, 4);
        DrawLoadingSignalWell(g, signalWell, faultColor);

        var relays = new RectangleF(844, 166, 310, 334);
        DrawCutPanel(g, relays, Color.FromArgb(23, 30, 27), Color.FromArgb(70, 79, 65), 12, 3);
        DrawLoadingRelays(g, relays, faultColor);

        var track = new RectangleF(104, 536, 1050, 46);
        using (var channel = new SolidBrush(Color.FromArgb(4, 9, 9)))
        using (var inactive = new SolidBrush(Color.FromArgb(48, 58, 51)))
        using (var active = new SolidBrush(faultColor))
        {
            g.FillRectangle(channel, track);
            const int segmentCount = 35;
            var litSegments = (int)MathF.Ceiling(Math.Clamp(_loadingDisplayProgress, 0, 1) * segmentCount);
            for (var index = 0; index < segmentCount; index++)
            {
                var x = track.X + 9 + index * 29.5f;
                var rect = new RectangleF(x, track.Y + 10, 21, 26);
                g.FillRectangle(index < litSegments ? active : inactive, rect);
                if ((index & 3) == 0) g.FillRectangle(channel, x + 8, track.Y + 15, 5, 16);
            }
        }

        var displayedPercent = Math.Clamp((int)MathF.Round(_loadingDisplayProgress * 100), 0, 99);
        LabFont.Draw(g, _loadingStage, 104, 598, 2, faultColor);
        LabFont.Draw(g, $"{displayedPercent:00} PCT", 1154, 598, 2, C.Bone, LabTextAlign.Right);
        LabFont.Draw(g, "DO NOT RELEASE THE INTAKE LATCH", 72, 672, 1, C.Steel);
        LabFont.Draw(g, "LIVE FEED OPENS AFTER SIGNAL LOCK", 1188, 672, 1, C.Sick, LabTextAlign.Right);
    }

    private void DrawLoadingSignalWell(Graphics g, RectangleF well, Color signalColor)
    {
        LabFont.Draw(g, "RED SIGNAL BED / RECONSTRUCTION", well.X + 24, well.Y + 22, 2, C.Oxide);
        LabFont.Draw(g, "REMOTE FIELD GEOMETRY", well.X + 24, well.Y + 53, 1, C.Steel);

        var lattice = new RectangleF(well.X + 24, well.Y + 84, well.Width - 48, 178);
        using var darkTrace = new Pen(Color.FromArgb(48, C.Steel), 3);
        using var liveTrace = new Pen(Color.FromArgb(176, signalColor), 4);
        using var scan = new SolidBrush(Color.FromArgb(42, signalColor));
        var scanX = lattice.X + (_loadingAge * 104 % lattice.Width);
        g.FillRectangle(scan, scanX, lattice.Y, 8, lattice.Height);

        const int columns = 17;
        const int rows = 7;
        var dx = lattice.Width / (columns - 1);
        var dy = lattice.Height / (rows - 1);
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var x = lattice.X + column * dx;
            var y = lattice.Y + row * dy;
            var index = row * columns + column;
            var live = index / (float)(columns * rows) <= _loadingDisplayProgress;
            var pen = live ? liveTrace : darkTrace;
            if (column + 1 < columns && ((column * 7 + row * 11 + _level) % 5 != 0 ||
                                         row == 0 || row == rows - 1))
                g.DrawLine(pen, x, y, x + dx, y);
            if (row + 1 < rows && ((column * 13 + row * 3 + _level) % 4 != 0 ||
                                   column == 0 || column == columns - 1))
                g.DrawLine(pen, x, y, x, y + dy);
        }

        var pulseX = lattice.X + lattice.Width * Math.Clamp(_loadingDisplayProgress, .03f, .97f);
        var pulseY = lattice.Y + lattice.Height / 2 + MathF.Sin(_loadingAge * 4.5f) * 34;
        DrawReticle(g, new PointF(pulseX, pulseY), 17 + MathF.Sin(_loadingAge * 6) * 3,
            Color.FromArgb(205, signalColor));

        DrawWaveform(g, new RectangleF(well.X + 24, well.Bottom - 48, well.Width - 48, 28),
            signalColor, _loadingAge * 1.8f);
    }

    private void DrawLoadingRelays(Graphics g, RectangleF relays, Color signalColor)
    {
        LabFont.Draw(g, "INTAKE RELAYS", relays.X + 22, relays.Y + 22, 2, C.Bone);
        var labels = new[]
        {
            "FIELD LATTICE",
            "CARGO MANIFEST",
            "NEGATIVE FORMS",
            "AUDIO SIGNAL"
        };
        var thresholds = new[] { .22f, .46f, .67f, .88f };
        for (var index = 0; index < labels.Length; index++)
        {
            var y = relays.Y + 72 + index * 58;
            var active = _loadingDisplayProgress >= thresholds[index];
            using var socket = new SolidBrush(Color.FromArgb(6, 12, 11));
            using var lamp = new SolidBrush(active ? signalColor : Color.FromArgb(58, 66, 57));
            using var pin = new SolidBrush(active ? C.Bone : C.Steel);
            g.FillRectangle(socket, relays.X + 20, y, relays.Width - 40, 40);
            g.FillRectangle(lamp, relays.X + 31, y + 10, 21, 21);
            g.FillRectangle(pin, relays.Right - 58, y + (active ? 5 : 14), 24, active ? 30 : 12);
            LabFont.Draw(g, labels[index], relays.X + 68, y + 13, 1, active ? C.Bone : C.Steel);
        }
        LabFont.Draw(g, _loadingFault ? "REJECT" : "ARMED", relays.Right - 22, relays.Bottom - 28,
            1, signalColor, LabTextAlign.Right);
    }
}
