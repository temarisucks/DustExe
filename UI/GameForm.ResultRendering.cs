namespace Dust;

internal sealed partial class GameForm
{
    private void DrawOutcome(Graphics g)
    {
        using var blackout = new SolidBrush(Color.FromArgb(240, 2, 6, 6));
        g.FillRectangle(blackout, _mazeRect);

        const float paperWidth = 780;
        var paperHeight = Math.Clamp(_resultPaperHeight, 58, 590);
        var paper = new RectangleF(
            _mazeRect.X + (_mazeRect.Width - paperWidth) / 2,
            _mazeRect.Y + 54,
            paperWidth,
            paperHeight);
        using var shadow = new SolidBrush(Color.FromArgb(5, 8, 8));
        using var sheet = new SolidBrush(Color.FromArgb(203, 190, 143));
        using var rule = new Pen(Color.FromArgb(116, 101, 75), 2);
        using var throat = new SolidBrush(C.Steel);
        using var roller = new SolidBrush(Color.Black);

        // A fixed printer throat feeds a sheet that physically lengthens as each
        // line begins, making the result record feel produced rather than overlaid.
        g.FillRectangle(roller, paper.X - 30, paper.Y - 32, paper.Width + 60, 34);
        g.FillRectangle(throat, paper.X - 15, paper.Y - 24, paper.Width + 30, 14);
        for (var x = paper.X - 4; x < paper.Right + 4; x += 27)
            g.FillRectangle(sheet, x, paper.Y - 20, 10, 6);
        g.FillRectangle(shadow, paper.X + 13, paper.Y + 12, paper.Width, paper.Height);
        g.FillRectangle(sheet, paper);

        var state = g.Save();
        g.SetClip(paper);
        for (var y = paper.Y + 51; y < paper.Bottom; y += 34)
            g.DrawLine(rule, paper.X + 31, y, paper.Right - 31, y);
        for (var y = paper.Y + 17; y < paper.Bottom - 10; y += 28)
        {
            g.FillRectangle(roller, paper.X + 9, y, 7, 7);
            g.FillRectangle(roller, paper.Right - 16, y, 7, 7);
        }

        var budget = ResultCharacterBudget();
        var textY = paper.Y + 22;
        var activeLineWidth = 0;
        var activeLineY = textY;
        for (var index = 0; index < _resultLines.Count; index++)
        {
            if (budget <= 0) break;
            var line = _resultLines[index];
            var count = Math.Min(line.Length, budget);
            var visible = line[..count];
            var scale = index == 0 || index == _jobPayResultLineIndex ||
                        index == _accountResultLineIndex ? 2 : 1;
            var color = index == 1 || index == _jobPayResultLineIndex
                ? C.Oxide
                : index == _accountResultLineIndex ||
                  index == _survivorStatusLineIndex && index != _survivorReportLineIndex
                    ? Color.FromArgb(48, 78, 62)
                    : index == _survivorReportLineIndex ? Color.FromArgb(116, 25, 24) : C.Ink;
            if (index == _survivorReportLineIndex && count > 0)
                DrawResultBloodstain(g,
                    new RectangleF(paper.X + 32, textY - 8, paper.Width - 64, 29),
                    count / (float)Math.Max(1, line.Length));
            LabFont.Draw(g, visible, paper.X + 42, textY, scale, color, tracking: index == 0 ? 0 : 1);
            activeLineWidth = LabFont.Measure(visible, scale, index == 0 ? 0 : 1).Width;
            activeLineY = textY;
            budget -= count;
            if (count < line.Length) break;
            budget -= 5;
            textY += 34;
        }

        if (!ResultTypingComplete && ((int)(_time * 5) & 1) == 0)
        {
            using var cursor = new SolidBrush(C.Oxide);
            g.FillRectangle(cursor, paper.X + 45 + activeLineWidth, activeLineY, 8, 14);
        }
        g.Restore(state);

        if (ResultReady)
        {
            _againButton = new RectangleF(paper.X + 42, paper.Bottom - 70, 352, 52);
            _menuButton = new RectangleF(paper.Right - 258, paper.Bottom - 70, 216, 52);
            var advanceLabel = !_onlineMatchActive
                ? "NEXT PLATE"
                : IsOnlineLobbyHost ? "RETURN TO LOBBY" : "WAIT FOR HOST";
            var ejectLabel = _onlineMatchActive ? "LEAVE LOBBY" : "EJECT";
            DrawLatchButton(g, _againButton, advanceLabel, _hoverOverlay == 0 || _resultSelection == 0);
            DrawAbortButton(g, _menuButton, ejectLabel, _hoverOverlay == 1 || _resultSelection == 1);
            DrawKeyboardFocusMarker(g, _resultSelection == 0 ? _againButton : _menuButton);
        }
        else
        {
            _againButton = RectangleF.Empty;
            _menuButton = RectangleF.Empty;
            LabFont.Draw(g, ResultTypingComplete ? "SEALING RECORD / INPUT LOCKED" : "RECORD PRINTING / INPUT LOCKED",
                _mazeRect.Right - 36, _mazeRect.Bottom - 42,
                1, C.Signal, LabTextAlign.Right);
        }

        LabFont.Draw(g, $"ACCOUNT BEFORE  {_creditsBeforeJob:000000}", _mazeRect.X + 34,
            _mazeRect.Bottom - 42, 1, C.Steel);
    }

    private static void DrawResultBloodstain(Graphics g, RectangleF band, float reveal)
    {
        reveal = Math.Clamp(reveal, 0, 1);
        var alpha = (int)(55 + reveal * 105);
        var center = new PointF(band.Right - 96, band.Y + 12);
        using var oldBlood = new SolidBrush(Color.FromArgb(alpha, 83, 18, 19));
        using var freshBlood = new SolidBrush(Color.FromArgb((int)(alpha * .72f), 132, 31, 27));
        g.FillEllipse(oldBlood, center.X - 43, center.Y - 10, 74, 24);
        g.FillEllipse(freshBlood, center.X - 17, center.Y - 14, 43, 29);
        g.FillPolygon(oldBlood,
        [
            new PointF(center.X - 62, center.Y + 2),
            new PointF(center.X - 29, center.Y - 7),
            new PointF(center.X + 53, center.Y + 7),
            new PointF(center.X + 27, center.Y + 12)
        ]);
        g.FillRectangle(oldBlood, center.X + 19, center.Y + 8, 7, 18 * reveal);
        g.FillRectangle(freshBlood, center.X - 33, center.Y + 7, 4, 11 * reveal);
        g.FillEllipse(oldBlood, center.X + 45, center.Y + 13, 8, 6);
    }
}
