using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Dust;

internal sealed partial class GameForm
{
    private void PaintScene(object? sender, PaintEventArgs e)
    {
        using (var g = Graphics.FromImage(_canvas))
        {
            g.Clear(C.Void);
            g.SmoothingMode = SmoothingMode.None;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            g.ScaleTransform(.5f, .5f);
            DrawFacilityShell(g);
            switch (_mode)
            {
                case ScreenMode.TutorialOffer:
                    DrawTutorialOffer(g);
                    break;
                case ScreenMode.Tutorial:
                    DrawTutorialConsole(g);
                    break;
                case ScreenMode.Title:
                    DrawTitleMenu(g);
                    break;
                case ScreenMode.RunSettings:
                    DrawRunSettingsConsole(g);
                    break;
                case ScreenMode.Customize:
                    DrawCustomizeConsole(g);
                    break;
                case ScreenMode.Settings:
                    DrawSettingsConsole(g);
                    break;
                case ScreenMode.Achievements:
                    DrawProgressionConsole(g);
                    break;
                case ScreenMode.OnlineAccount:
                    DrawOnlineAccountConsole(g);
                    break;
                case ScreenMode.LobbyBrowser:
                    DrawLobbyBrowserConsole(g);
                    break;
                case ScreenMode.LobbyRoom:
                    DrawLobbyRoomConsole(g);
                    break;
                case ScreenMode.Loading:
                    DrawLoadingConsole(g);
                    break;
                case ScreenMode.Shop:
                    DrawShopConsole(g);
                    break;
                case ScreenMode.Playing:
                case ScreenMode.Won:
                case ScreenMode.Failed:
                    DrawTrialFeed(g);
                    if (IsPauseMenuActive)
                    {
                        if (_pauseSettingsOpen) DrawSettingsConsole(g);
                        else DrawPauseConsole(g);
                    }
                    break;
            }
            DrawFeedDamage(g);
            DrawAchievementToast(g);
            DrawWindowChrome(g);
            DrawBrightnessOverlay(g);
        }

        var output = e.Graphics;
        output.Clear(Color.Black);
        output.InterpolationMode = InterpolationMode.NearestNeighbor;
        output.PixelOffsetMode = PixelOffsetMode.Half;
        output.DrawImage(_canvas, Rectangle.Round(CanvasDestination()), new Rectangle(0, 0, CanvasWidth, CanvasHeight), GraphicsUnit.Pixel);
    }

    private void DrawFacilityShell(Graphics g)
    {
        g.Clear(C.Void);
        var housing = new RectangleF(8, 18, DesignWidth - 16, DesignHeight - 30);
        DrawCutPanel(g, housing, Color.FromArgb(62, 68, 57), Color.FromArgb(13, 18, 18), 26, 8);
        var enamel = RectangleF.Inflate(housing, -12, -12);
        DrawCutPanel(g, enamel, Color.FromArgb(111, 108, 87), Color.FromArgb(39, 45, 40), 18, 4);

        // The window is the face of one old behavioral apparatus: chipped enamel,
        // a punched lot label, and a chart-paper throat along the bottom edge.
        using var chipDark = new SolidBrush(Color.FromArgb(37, 40, 34));
        using var chipLight = new SolidBrush(Color.FromArgb(158, 151, 116));
        for (var i = 0; i < 74; i++)
        {
            var x = 17 + (i * 167) % (DesignWidth - 34);
            var y = 26 + (i * 97) % (DesignHeight - 63);
            var width = 2 + (i * 7) % 9;
            g.FillRectangle((i & 3) == 0 ? chipLight : chipDark, x, y, width, 2 + (i & 1));
        }

        if (_mode != ScreenMode.Title)
        {
            using var paper = new SolidBrush(Color.FromArgb(203, 190, 143));
            using var paperShadow = new SolidBrush(Color.FromArgb(57, 55, 45));
            g.FillRectangle(paperShadow, 78, 7, 532, 42);
            g.FillRectangle(paper, 70, 3, 532, 42);
            for (var x = 86; x < 588; x += 29) g.FillRectangle(chipDark, x, 7, 6, 6);
            LabFont.Draw(g, "DUST BEHAVIORAL PLATE  LOT 31", 95, 23, 2, C.Ink);

            g.FillRectangle(paperShadow, 146, DesignHeight - 34, DesignWidth - 292, 32);
            g.FillRectangle(paper, 138, DesignHeight - 40, DesignWidth - 292, 32);
            for (var x = 153; x < DesignWidth - 168; x += 31)
                g.FillRectangle(chipDark, x, DesignHeight - 36, 7, 7);
            LabFont.Draw(g, "CYCLE RECORD ADVANCES ONLY AFTER TRANSFER", DesignWidth / 2,
                DesignHeight - 28, 1, C.Ink, LabTextAlign.Center);
        }

        using var residue = new SolidBrush(Color.FromArgb(96, 57, 39));
        g.FillRectangle(residue, DesignWidth - 92, 87, 19, 63);
        g.FillRectangle(residue, DesignWidth - 104, 136, 31, 11);
        g.FillRectangle(residue, DesignWidth - 80, 151, 7, 25);
        if (_mode != ScreenMode.Title)
            LabFont.Draw(g, "31", 31, DesignHeight - 83, 3, C.Ink);
    }

    private void DrawTelemetry(Graphics g)
    {
        var elapsed = _mode == ScreenMode.Won ? _wonTime : CurrentMissionElapsed();
        var left = new RectangleF(_mazeRect.X + 15, _mazeRect.Y + 14, 302, 58);
        DrawCutPanel(g, left, Color.FromArgb(218, C.Ink), Color.FromArgb(145, C.Steel), 9, 2);
        LabFont.Draw(g, $"CH {_level:00}", left.X + 13, left.Y + 11, 2, C.Signal);
        LabFont.Draw(g, $"RSP {_steps:000}", left.X + 103, left.Y + 11, 2, C.Bone);
        LabFont.Draw(g, $"T {(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}", left.Right - 13, left.Y + 11, 2, C.Sick, LabTextAlign.Right);
        var condition = _hitEffect > 0
            ? "FRAME IMPACT"
            : $"NEGATIVE {_hollows.Count + _sentries.Count:00}";
        LabFont.Draw(g, condition, left.X + 13, left.Y + 38, 1,
            _hitEffect > 0 ? C.Red : C.Oxide);
        DrawHealthMonitor(g, left);
        if (_mode == ScreenMode.Playing)
        {
            DrawMissionDossierButton(g);
            DrawInventoryButton(g);
        }
        DrawPerkTelemetry(g);

        var rightX = _mazeRect.Right - 31;
        using var tick = new SolidBrush(Color.FromArgb(145, C.Sick));
        for (var y = _mazeRect.Y + 84; y < _mazeRect.Bottom - 70; y += 17)
            g.FillRectangle(tick, rightX - (((int)y / 17) % 4 == 0 ? 11 : 5), y, ((int)y / 17) % 4 == 0 ? 11 : 5, 2);
        LabFont.Draw(g, "03", rightX - 1, _mazeRect.Y + 50, 1, C.Oxide, LabTextAlign.Right);

        if (_shopProtectionCharges > 0 || _framePatchInventory > 0 ||
            _reconstructionGelInventory > 0)
        {
            var supplies = $"PATCH {_framePatchInventory:00}  GEL {_reconstructionGelInventory:00}  " +
                           $"AEGIS {_shopProtectionCharges:00}";
            LabFont.Draw(g, supplies, _mazeRect.X + _mazeRect.Width / 2,
                _mazeRect.Bottom - 23, 1, C.Signal, LabTextAlign.Center);
        }
        DrawMiniMap(g);
        if (!_missionDossierOpen && !_inventoryOpen) DrawMissionPrompt(g);
    }

    private void DrawFeedFrame(Graphics g)
    {
        using var outer = new Pen(Color.Black, 10);
        using var inner = new Pen(Color.FromArgb(95, 106, 88), 3);
        g.DrawRectangle(outer, _mazeRect.X, _mazeRect.Y, _mazeRect.Width, _mazeRect.Height);
        g.DrawRectangle(inner, _mazeRect.X + 5, _mazeRect.Y + 5, _mazeRect.Width - 10, _mazeRect.Height - 10);
        using var oxide = new SolidBrush(C.Oxide);
        g.FillRectangle(oxide, _mazeRect.X - 1, _mazeRect.Y - 1, 64, 6);
        g.FillRectangle(oxide, _mazeRect.X - 1, _mazeRect.Y - 1, 6, 46);
        g.FillRectangle(oxide, _mazeRect.Right - 63, _mazeRect.Bottom - 5, 64, 6);
        g.FillRectangle(oxide, _mazeRect.Right - 5, _mazeRect.Bottom - 45, 6, 46);
        LabFont.Draw(g, $"SUBJ {_playerCell.X:00}:{_playerCell.Y:00}", _mazeRect.Right - 18, _mazeRect.Y + 16, 1, C.Sick, LabTextAlign.Right);
    }

    private static void DrawLatchButton(Graphics g, RectangleF rect, string text, bool hovered,
        bool showState = true)
    {
        DrawCutPanel(g, rect, Color.FromArgb(19, 27, 26), hovered ? C.Signal : C.Steel, 12, 4);
        using var trackShadow = new SolidBrush(Color.Black);
        using var track = new SolidBrush(Color.FromArgb(68, 72, 59));
        using var handle = new SolidBrush(hovered ? C.Signal : C.Bone);
        using var collar = new SolidBrush(C.Oxide);
        var trackRect = new RectangleF(rect.X + 24, rect.Y + rect.Height / 2 - 8, rect.Width - 106, 16);
        g.FillRectangle(trackShadow, trackRect.X - 3, trackRect.Y - 3, trackRect.Width + 6, trackRect.Height + 6);
        g.FillRectangle(track, trackRect);
        var handleX = hovered ? rect.Right - 112 : rect.Right - 79;
        g.FillRectangle(trackShadow, handleX - 5, rect.Y + 9, 34, rect.Height - 12);
        g.FillRectangle(handle, handleX, rect.Y + 5, 24, rect.Height - 18);
        g.FillRectangle(collar, handleX - 6, rect.Y + rect.Height / 2 - 7, 36, 14);
        LabFont.Draw(g, text, rect.X + 42, rect.Y + rect.Height / 2 - 8, 2,
            hovered ? C.Signal : C.Bone);
        if (showState)
            LabFont.Draw(g, hovered ? "SEALED" : "OPEN", rect.Right - 18,
                rect.Y + rect.Height / 2 - 4, 1, hovered ? C.Signal : C.Steel, LabTextAlign.Right);
    }

    private static void DrawAbortButton(Graphics g, RectangleF rect, string text, bool hovered)
    {
        DrawCutPanel(g, rect, hovered ? Color.FromArgb(70, 31, 29) : C.Ink, hovered ? C.Oxide : C.Steel, 9, 3);
        LabFont.Draw(g, text, rect.X + rect.Width / 2, rect.Y + rect.Height / 2 - 7, 2,
            hovered ? C.Signal : C.Sick, LabTextAlign.Center);
    }

    private static void DrawReticle(Graphics g, PointF center, float radius, Color color)
    {
        using var pen = new Pen(color, 2);
        g.DrawRectangle(pen, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        g.DrawRectangle(pen, center.X - radius * .67f, center.Y - radius * .67f, radius * 1.34f, radius * 1.34f);
        g.DrawLine(pen, center.X - radius, center.Y, center.X - radius * .45f, center.Y);
        g.DrawLine(pen, center.X + radius * .45f, center.Y, center.X + radius, center.Y);
        g.DrawLine(pen, center.X, center.Y - radius, center.X, center.Y - radius * .45f);
        g.DrawLine(pen, center.X, center.Y + radius * .45f, center.X, center.Y + radius);
    }

    private static void DrawWaveform(Graphics g, RectangleF rect, Color color, float time)
    {
        using var baseLine = new Pen(Color.FromArgb(55, color), 1);
        using var trace = new Pen(color, 2);
        g.DrawLine(baseLine, rect.X, rect.Y + rect.Height / 2, rect.Right, rect.Y + rect.Height / 2);
        var points = new List<PointF>();
        for (var x = 0; x <= (int)rect.Width; x += 4)
        {
            var phase = (x + time * 42) % 86;
            var spike = phase is > 34 and < 38 ? -18 : phase is >= 38 and < 42 ? 16 : phase is >= 42 and < 48 ? -7 : 0;
            points.Add(new PointF(rect.X + x, rect.Y + rect.Height / 2 + spike));
        }
        if (points.Count > 1) g.DrawLines(trace, points.ToArray());
    }
}
