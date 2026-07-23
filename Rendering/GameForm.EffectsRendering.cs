using System.Drawing.Drawing2D;

namespace Dust;

internal sealed partial class GameForm
{
    private void DrawDetectionWarning(Graphics g)
    {
        if (_warningFlash <= 0 || _hitEffect > 0 || _mode != ScreenMode.Playing) return;
        var elapsed = .82f - _warningFlash;
        var beat = (int)(elapsed * 8) % 4;
        if (beat == 3) return;
        var center = CellCenter(_visualCell);
        var radius = _cellSize * .29f;
        var markerY = center.Y - radius - 43;
        var markerBounds = new RectangleF(center.X - 12, markerY - 2, 24, 32);
        var telemetry = new RectangleF(_mazeRect.X + 15, _mazeRect.Y + 14, 302, 58);
        if (markerY < _mazeRect.Y + 12 || markerBounds.IntersectsWith(telemetry))
            markerY = center.Y + radius + 15;
        var color = beat == 0 ? C.Signal : C.Red;
        LabFont.Draw(g, "!", center.X - 2, markerY, 4, C.Ink, LabTextAlign.Center, 0);
        LabFont.Draw(g, "!", center.X + 2, markerY, 4, C.Ink, LabTextAlign.Center, 0);
        LabFont.Draw(g, "!", center.X, markerY - 2, 4, C.Ink, LabTextAlign.Center, 0);
        LabFont.Draw(g, "!", center.X, markerY + 2, 4, C.Ink, LabTextAlign.Center, 0);
        LabFont.Draw(g, "!", center.X, markerY, 4, color, LabTextAlign.Center, 0);
    }

    private void DrawDestabilizedDrone(Graphics g)
    {
        var center = CellCenter(_visualCell);
        var radius = Math.Max(14, (int)(_cellSize * .29f));
        var intensity = HitInterference();
        var pixel = Math.Max(2, radius / 6);

        var bandHeight = Math.Max(7, (radius * 2 + 6) / 6);
        var band = 0;
        for (var y = center.Y - radius - 3; y < center.Y + radius + 4; y += bandHeight)
        {
            var state = g.Save();
            var gap = intensity > .18f ? 1 : 0;
            g.SetClip(new RectangleF(center.X - radius - 26, y, radius * 2 + 52, bandHeight - gap), CombineMode.Intersect);
            var direction = (band & 1) == 0 ? 1 : -1;
            var phase = ((band * 7 + _damageTaken * 3 + (int)(_time * 24)) % 5) - 2;
            var offset = direction * intensity * (7 + Math.Abs(phase) * 3);
            DrawDrone(g, _drone, _playerColor, _playerFrameColor,
                new PointF(center.X + offset, center.Y), radius, 255,
                drawShadow: false, drawBrackets: false, bank: _droneBank, pitch: _dronePitch,
                showDamage: true);
            g.Restore(state);
            band++;
        }
        var bracketAlpha = (int)((1 - intensity) * 255);
        if (bracketAlpha > 8)
            DrawTrackingBrackets(g, center, radius + pixel * 2, Color.FromArgb(bracketAlpha, C.Signal));
    }

    private float HitInterference()
    {
        if (_hitEffect <= 0) return 0;
        var progress = Math.Clamp(1f - _hitEffect / 1.16f, 0, 1);
        return MathF.Pow(MathF.Sin(progress * MathF.PI), .55f);
    }

    private enum DroneLayer { Silhouette, Frame, Coating }

    private void DrawChamberOcclusion(Graphics g)
    {
        using var dark1 = new SolidBrush(Color.FromArgb(72, 0, 2, 2));
        using var dark2 = new SolidBrush(Color.FromArgb(38, 0, 2, 2));
        g.FillRectangle(dark1, _mazeRect.X, _mazeRect.Y, _mazeRect.Width, 28);
        g.FillRectangle(dark1, _mazeRect.X, _mazeRect.Bottom - 28, _mazeRect.Width, 28);
        g.FillRectangle(dark1, _mazeRect.X, _mazeRect.Y, 28, _mazeRect.Height);
        g.FillRectangle(dark1, _mazeRect.Right - 28, _mazeRect.Y, 28, _mazeRect.Height);
        g.FillRectangle(dark2, _mazeRect.X + 28, _mazeRect.Y + 28, _mazeRect.Width - 56, 18);
        g.FillRectangle(dark2, _mazeRect.X + 28, _mazeRect.Bottom - 46, _mazeRect.Width - 56, 18);
    }

    private void DrawFeedDamage(Graphics g)
    {
        if (_mode is not (ScreenMode.Playing or ScreenMode.Won or ScreenMode.Failed)) return;
        var state = g.Save();
        g.SetClip(_mazeRect);
        using var dropout = new SolidBrush(Color.FromArgb(22, 0, 0, 0));
        for (var y = (int)_mazeRect.Y + 11; y < _mazeRect.Bottom; y += 13)
            if ((y / 13) % 3 != 0) g.FillRectangle(dropout, _mazeRect.X, y, _mazeRect.Width, 1);

        if (_hitEffect > 0)
        {
            var intensity = HitInterference();
            var frame = (uint)Math.Max(0, (int)(_time * 30));
            var seed = frame * 747796405u ^ (uint)(_damageTaken * 2891336453u);
            using var blackout = new SolidBrush(Color.FromArgb((int)(intensity * 72), C.Void));
            using var whiteNoise = new SolidBrush(Color.FromArgb((int)(intensity * 145), C.Bone));
            using var redNoise = new SolidBrush(Color.FromArgb((int)(intensity * 112), C.Oxide));
            using var deadNoise = new SolidBrush(Color.FromArgb((int)(intensity * 180), 0, 3, 3));
            g.FillRectangle(blackout, _mazeRect);

            // Noise arrives in a few failed registration bands rather than as
            // uniform confetti, keeping the disturbance tied to the apparatus.
            var registrationBandCount = 3 + (int)(intensity * 2);
            var registrationBands = new int[registrationBandCount];
            for (var i = 0; i < registrationBands.Length; i++)
                registrationBands[i] = (int)_mazeRect.Y +
                    NextNoise(ref seed, Math.Max(1, (int)_mazeRect.Height / 2)) * 2;

            var fragments = (int)(intensity * 78);
            for (var i = 0; i < fragments; i++)
            {
                var x = (int)_mazeRect.X + NextNoise(ref seed, Math.Max(1, (int)_mazeRect.Width / 2)) * 2;
                var bandY = registrationBands[NextNoise(ref seed, registrationBands.Length)];
                var y = bandY + (NextNoise(ref seed, 9) - 4) * 2;
                var width = (1 + NextNoise(ref seed, 3 + (int)(intensity * 14))) * 2;
                var height = 2 + NextNoise(ref seed, 2) * 2;
                var brush = ((i + NextNoise(ref seed, 3)) % 7) switch
                {
                    0 => redNoise,
                    1 or 2 => whiteNoise,
                    _ => deadNoise
                };
                g.FillRectangle(brush, x, y, width, height);
            }

            var tears = intensity < .22f ? 0 : 1 + (int)(intensity * 3);
            for (var i = 0; i < tears; i++)
            {
                var y = registrationBands[NextNoise(ref seed, registrationBands.Length)];
                var height = (1 + NextNoise(ref seed, 2 + (int)(intensity * 4))) * 2;
                var inset = NextNoise(ref seed, Math.Max(1, (int)(_mazeRect.Width * .11f))) * 2;
                g.FillRectangle((i & 2) == 0 ? deadNoise : redNoise,
                    _mazeRect.X + inset, y, _mazeRect.Width - inset, height);
            }

            var splitY = _mazeRect.Y + ((int)(_time * 92) % Math.Max(1, (int)_mazeRect.Height / 2)) * 2;
            g.FillRectangle(whiteNoise, _mazeRect.X, splitY, _mazeRect.Width, 2);
            g.Restore(state);
            return;
        }

        var cycle = (int)(_time * 14) % 157;
        if (cycle is 0 or 1 or 2)
        {
            using var tear = new SolidBrush(Color.FromArgb(115, 5, 9, 9));
            using var reagent = new SolidBrush(Color.FromArgb(50, C.Oxide));
            var y = (int)_mazeRect.Y + 80 + ((int)(_time * 83) % Math.Max(1, (int)_mazeRect.Height - 160));
            g.FillRectangle(tear, _mazeRect.X, y, _mazeRect.Width, 7);
            g.FillRectangle(reagent, _mazeRect.X + _mazeRect.Width / 5, y - 3, _mazeRect.Width * .58f, 2);
        }
        g.Restore(state);
    }

    private static int NextNoise(ref uint state, int maximum)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return maximum <= 1 ? 0 : (int)(state % (uint)maximum);
    }

    private void DrawLensFlaws(Graphics g)
    {
        using var lint = new SolidBrush(Color.FromArgb(62, 4, 8, 8));
        var marks = new (float X, float Y, int W, int H)[]
        {
            (.18f, .23f, 17, 3), (.72f, .16f, 4, 12), (.83f, .61f, 13, 2),
            (.37f, .78f, 3, 9), (.58f, .42f, 6, 3), (.11f, .67f, 9, 2)
        };
        foreach (var mark in marks)
            g.FillRectangle(lint, _mazeRect.X + _mazeRect.Width * mark.X, _mazeRect.Y + _mazeRect.Height * mark.Y, mark.W, mark.H);
    }
}
