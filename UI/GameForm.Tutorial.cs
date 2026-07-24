using System.Drawing.Drawing2D;

namespace Dust;

internal sealed partial class GameForm
{
    private const int TutorialStageCount = 6;

    private readonly RectangleF[] _tutorialOfferButtons = new RectangleF[2];
    private readonly RectangleF[] _tutorialDirectionButtons = new RectangleF[4];
    private RectangleF _tutorialInputButton;
    private RectangleF _tutorialAdvanceButton;
    private RectangleF _tutorialLeaveButton;
    private int _tutorialOfferSelection;
    private int _tutorialStage;
    private Point _tutorialDroneCell;
    private bool _tutorialCargoLatched;
    private bool _tutorialFileOpen;
    private bool _tutorialFileWasClosed;
    private bool _tutorialPerkTriggered;
    private bool _tutorialEvadedHollow;
    private int _hoverTutorialOffer = -1;
    private int _hoverTutorialDirection = -1;
    private bool _hoverTutorialInput;
    private bool _hoverTutorialAdvance;
    private bool _hoverTutorialLeave;

    private bool TutorialStageReady => _tutorialStage switch
    {
        0 => _tutorialDroneCell == new Point(4, 2),
        1 => _tutorialCargoLatched,
        2 => _tutorialFileWasClosed,
        3 => _tutorialPerkTriggered,
        4 => _tutorialEvadedHollow,
        _ => true
    };

    // Kept as narrow read-only seams so the reflection QA can assert that a
    // constructor alone never steals the normal title route.
    internal bool TutorialOfferVisibleForQa => _mode == ScreenMode.TutorialOffer;
    internal int TutorialStageForQa => _tutorialStage;
    internal bool TutorialStageReadyForQa => TutorialStageReady;

    private void OfferTutorialOnFirstShown()
    {
        if (!_settings.ShouldOfferCurrentTutorial || _mode != ScreenMode.Title) return;
        _mode = ScreenMode.TutorialOffer;
        _tutorialOfferSelection = 0;
        ResetHover();
    }

    private void BeginTutorial()
    {
        MarkTutorialOffered();
        _mode = ScreenMode.Tutorial;
        _tutorialStage = 0;
        _tutorialDroneCell = new Point(1, 2);
        _tutorialCargoLatched = false;
        _tutorialFileOpen = false;
        _tutorialFileWasClosed = false;
        _tutorialPerkTriggered = false;
        _tutorialEvadedHollow = false;
        ResetHover();
        RequestMenuMusic();
    }

    private void DeclineTutorialOffer()
    {
        MarkTutorialOffered();
        EnterTitle(resetSelection: true);
    }

    private void MarkTutorialOffered()
    {
        if (_settings.TutorialOfferVersion >= GameSettings.CurrentTutorialVersion) return;
        _settings.TutorialOfferVersion = GameSettings.CurrentTutorialVersion;
        SaveSettings();
    }

    private void FinishTutorial()
    {
        _settings.TutorialOfferVersion = Math.Max(
            _settings.TutorialOfferVersion, GameSettings.CurrentTutorialVersion);
        _settings.TutorialCompletedVersion = Math.Max(
            _settings.TutorialCompletedVersion, GameSettings.CurrentTutorialVersion);
        SaveSettings();
        _audio.Play(AudioCue.Confirm);
        EnterTitle(resetSelection: true);
    }

    private void LeaveTutorial()
    {
        _tutorialFileOpen = false;
        _audio.Play(AudioCue.Confirm);
        EnterTitle(resetSelection: true);
    }

    private void HandleTutorialOfferKey(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.W or Keys.Up or Keys.A or Keys.Left)
        {
            _tutorialOfferSelection = Wrap(_tutorialOfferSelection - 1, 2);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode is Keys.S or Keys.Down or Keys.D or Keys.Right or Keys.Tab)
        {
            _tutorialOfferSelection = Wrap(_tutorialOfferSelection + 1, 2);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            _audio.Play(AudioCue.Confirm);
            if (_tutorialOfferSelection == 0) BeginTutorial();
            else DeclineTutorialOffer();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _audio.Play(AudioCue.Confirm);
            DeclineTutorialOffer();
        }
        else
            return;
        ConsumeKey(e);
    }

    private void HandleTutorialKey(KeyEventArgs e)
    {
        if (_tutorialStage == 2 && _tutorialFileOpen && e.KeyCode is Keys.Q or Keys.Escape)
        {
            CloseTutorialFile();
            ConsumeKey(e);
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            LeaveTutorial();
            ConsumeKey(e);
            return;
        }

        if (e.KeyCode is Keys.Enter)
        {
            AdvanceTutorial();
            ConsumeKey(e);
            return;
        }

        if (_tutorialStage is 0 or 4)
        {
            var direction = e.KeyCode switch
            {
                Keys.W or Keys.Up => Direction.Up,
                Keys.D or Keys.Right => Direction.Right,
                Keys.S or Keys.Down => Direction.Down,
                Keys.A or Keys.Left => Direction.Left,
                _ => (Direction?)null
            };
            if (direction.HasValue)
            {
                MoveTutorialDrone(direction.Value);
                ConsumeKey(e);
                return;
            }
        }
        else if (_tutorialStage == 1 && e.KeyCode == Keys.E)
        {
            _tutorialCargoLatched = true;
            _audio.Play(AudioCue.Confirm);
            ConsumeKey(e);
            return;
        }
        else if (_tutorialStage == 2 && e.KeyCode == Keys.Q)
        {
            _tutorialFileOpen = true;
            _audio.Play(AudioCue.Confirm);
            ConsumeKey(e);
            return;
        }
        else if (_tutorialStage == 3 && e.KeyCode == Keys.Space)
        {
            _tutorialPerkTriggered = true;
            _audio.Play(AudioCue.Confirm);
            ConsumeKey(e);
            return;
        }

        // Space is an action during the perk lesson and must not also advance it.
        if (e.KeyCode == Keys.Space && _tutorialStage != 3)
        {
            if (TutorialStageReady) AdvanceTutorial();
            ConsumeKey(e);
        }
    }

    private void MoveTutorialDrone(Direction direction)
    {
        var next = direction switch
        {
            Direction.Up => new Point(_tutorialDroneCell.X, _tutorialDroneCell.Y - 1),
            Direction.Right => new Point(_tutorialDroneCell.X + 1, _tutorialDroneCell.Y),
            Direction.Down => new Point(_tutorialDroneCell.X, _tutorialDroneCell.Y + 1),
            _ => new Point(_tutorialDroneCell.X - 1, _tutorialDroneCell.Y)
        };
        _tutorialDroneCell = new Point(Math.Clamp(next.X, 0, 5), Math.Clamp(next.Y, 0, 3));
        if (_tutorialStage == 4 && _tutorialDroneCell.Y >= 3)
            _tutorialEvadedHollow = true;
        _audio.Play(AudioCue.Move);
    }

    private void CloseTutorialFile()
    {
        _tutorialFileOpen = false;
        _tutorialFileWasClosed = true;
        _audio.Play(AudioCue.Confirm);
    }

    private void AdvanceTutorial()
    {
        if (!TutorialStageReady)
        {
            _audio.Play(AudioCue.Select);
            return;
        }
        if (_tutorialStage >= TutorialStageCount - 1)
        {
            FinishTutorial();
            return;
        }

        _tutorialStage++;
        if (_tutorialStage == 4)
            _tutorialDroneCell = new Point(3, 1);
        _audio.Play(AudioCue.Confirm);
        ResetHover();
    }

    private void HandleTutorialMouseMove(PointF hit)
    {
        if (_mode == ScreenMode.TutorialOffer)
        {
            for (var index = 0; index < _tutorialOfferButtons.Length; index++)
                if (_tutorialOfferButtons[index].Contains(hit)) _hoverTutorialOffer = index;
            return;
        }

        for (var index = 0; index < _tutorialDirectionButtons.Length; index++)
            if (_tutorialDirectionButtons[index].Contains(hit)) _hoverTutorialDirection = index;
        _hoverTutorialInput = _tutorialInputButton.Contains(hit);
        _hoverTutorialAdvance = _tutorialAdvanceButton.Contains(hit);
        _hoverTutorialLeave = _tutorialLeaveButton.Contains(hit);
    }

    private bool HandleTutorialMouseDown(PointF hit)
    {
        if (_mode == ScreenMode.TutorialOffer)
        {
            for (var index = 0; index < _tutorialOfferButtons.Length; index++)
            {
                if (!_tutorialOfferButtons[index].Contains(hit)) continue;
                _tutorialOfferSelection = index;
                _audio.Play(AudioCue.Confirm);
                if (index == 0) BeginTutorial();
                else DeclineTutorialOffer();
                return true;
            }
            return false;
        }

        if (_tutorialLeaveButton.Contains(hit))
        {
            LeaveTutorial();
            return true;
        }
        if (_tutorialAdvanceButton.Contains(hit))
        {
            AdvanceTutorial();
            return true;
        }
        if (_tutorialStage is 0 or 4)
        {
            for (var index = 0; index < _tutorialDirectionButtons.Length; index++)
            {
                if (!_tutorialDirectionButtons[index].Contains(hit)) continue;
                MoveTutorialDrone((Direction)index);
                return true;
            }
        }
        if (!_tutorialInputButton.Contains(hit)) return false;
        switch (_tutorialStage)
        {
            case 1:
                _tutorialCargoLatched = true;
                break;
            case 2:
                if (_tutorialFileOpen)
                {
                    CloseTutorialFile();
                    return true;
                }
                _tutorialFileOpen = true;
                break;
            case 3:
                _tutorialPerkTriggered = true;
                break;
            default:
                return false;
        }
        _audio.Play(AudioCue.Confirm);
        return true;
    }

    private void ResetTutorialHover()
    {
        _hoverTutorialOffer = -1;
        _hoverTutorialDirection = -1;
        _hoverTutorialInput = false;
        _hoverTutorialAdvance = false;
        _hoverTutorialLeave = false;
    }

    private void DrawTutorialOffer(Graphics g)
    {
        var shell = new RectangleF(108, 78, DesignWidth - 216, DesignHeight - 156);
        DrawMenuConsoleShell(g, shell, "UNREAD TRAINING CASSETTE / REV 01");

        var tape = new RectangleF(156, 151, 354, 438);
        DrawCutPanel(g, tape, Color.FromArgb(24, 31, 28), Color.FromArgb(92, 96, 76), 18, 5);
        DrawPanelBolts(g, tape, C.Steel);
        using (var label = new SolidBrush(Color.FromArgb(198, 184, 139)))
        using (var labelRule = new SolidBrush(Color.FromArgb(106, 51, 39)))
        {
            g.FillRectangle(label, tape.X + 41, tape.Y + 38, tape.Width - 82, 151);
            g.FillRectangle(labelRule, tape.X + 54, tape.Y + 54, tape.Width - 108, 7);
            LabFont.Draw(g, "ORIENTATION", tape.X + tape.Width / 2, tape.Y + 86, 2,
                C.Ink, LabTextAlign.Center, 0);
            LabFont.Draw(g, "SUBJECT 31", tape.X + tape.Width / 2, tape.Y + 129, 2,
                C.Oxide, LabTextAlign.Center, 1);
        }
        using (var reelDark = new SolidBrush(Color.FromArgb(4, 9, 9)))
        using (var reel = new Pen(C.Sick, 8))
        using (var hub = new SolidBrush(C.Oxide))
        {
            var reelY = tape.Y + 291;
            g.FillRectangle(reelDark, tape.X + 34, reelY - 69, tape.Width - 68, 138);
            foreach (var x in new[] { tape.X + 101, tape.Right - 101 })
            {
                g.DrawEllipse(reel, x - 48, reelY - 48, 96, 96);
                g.FillRectangle(hub, x - 9, reelY - 9, 18, 18);
            }
            g.FillRectangle(hub, tape.X + 149, reelY - 4, tape.Width - 298, 8);
        }
        LabFont.Draw(g, "01 / 06", tape.X + 28, tape.Bottom - 38, 1, C.Signal);
        LabFont.Draw(g, "UNSEEN", tape.Right - 28, tape.Bottom - 38, 1, C.Oxide, LabTextAlign.Right);

        var notice = new RectangleF(556, 151, 568, 438);
        DrawCutPanel(g, notice, Color.FromArgb(11, 18, 18), Color.FromArgb(68, 81, 69), 16, 4);
        DrawPanelBolts(g, notice, C.Steel);
        LabFont.Draw(g, "TRAINING OFFER", notice.X + 36, notice.Y + 38, 4, C.Bone);
        LabFont.Draw(g, "A NEW ORIENTATION CASSETTE HAS BEEN", notice.X + 38, notice.Y + 111, 1, C.Sick);
        LabFont.Draw(g, "ISSUED TO THIS LOCAL SUBJECT PROFILE.", notice.X + 38, notice.Y + 137, 1, C.Sick);
        LabFont.Draw(g, "THE DECISION IS RECORDED ON THIS DEVICE.", notice.X + 38, notice.Y + 177, 1, C.Steel);

        _tutorialOfferButtons[0] = new RectangleF(notice.X + 38, notice.Bottom - 154, 316, 64);
        _tutorialOfferButtons[1] = new RectangleF(notice.Right - 178, notice.Bottom - 154, 140, 64);
        DrawLatchButton(g, _tutorialOfferButtons[0], "ACCEPT",
            _tutorialOfferSelection == 0 || _hoverTutorialOffer == 0, showState: false);
        DrawAbortButton(g, _tutorialOfferButtons[1], "DECLINE",
            _tutorialOfferSelection == 1 || _hoverTutorialOffer == 1);
        DrawKeyboardFocusMarker(g, _tutorialOfferButtons[_tutorialOfferSelection]);
    }

    private void DrawTutorialConsole(Graphics g)
    {
        var shell = new RectangleF(42, 54, DesignWidth - 84, DesignHeight - 108);
        DrawMenuConsoleShell(g, shell, $"TRAINING CASSETTE / TRACK {_tutorialStage + 1:00}");

        var manifest = new RectangleF(72, 119, 406, 507);
        DrawCutPanel(g, manifest, Color.FromArgb(201, 190, 146), Color.FromArgb(79, 70, 53), 13, 4);
        using (var inkRule = new SolidBrush(Color.FromArgb(94, 78, 56)))
        {
            g.FillRectangle(inkRule, manifest.X + 26, manifest.Y + 71, manifest.Width - 52, 4);
            for (var y = manifest.Y + 153; y < manifest.Bottom - 77; y += 39)
                g.FillRectangle(inkRule, manifest.X + 31, y, manifest.Width - 62, 2);
        }
        LabFont.Draw(g, $"{_tutorialStage + 1:00} / {TutorialStageCount:00}",
            manifest.X + 27, manifest.Y + 27, 2, C.Oxide);
        LabFont.Draw(g, TutorialStageTitle(), manifest.X + 27, manifest.Y + 91, 3, C.Ink);
        var lines = TutorialStageCopy();
        for (var index = 0; index < lines.Length; index++)
            LabFont.Draw(g, lines[index], manifest.X + 31, manifest.Y + 171 + index * 39,
                1, index == lines.Length - 1 ? C.Oxide : C.Ink);
        LabFont.Draw(g, TutorialStageReady ? "TRACK CONDITION / PASSED" : "TRACK CONDITION / OPEN",
            manifest.X + 31, manifest.Bottom - 55, 1,
            TutorialStageReady ? Color.FromArgb(48, 78, 62) : C.Oxide);

        var cell = new RectangleF(506, 119, 702, 507);
        DrawTutorialCell(g, cell);

        _tutorialLeaveButton = new RectangleF(72, 656, 222, 58);
        _tutorialAdvanceButton = new RectangleF(882, 656, 326, 58);
        DrawAbortButton(g, _tutorialLeaveButton, "LEAVE TRAINING", _hoverTutorialLeave);
        DrawLatchButton(g, _tutorialAdvanceButton,
            _tutorialStage == TutorialStageCount - 1 ? "ENTER ROUTING" : "ADVANCE TRACK",
            TutorialStageReady && _hoverTutorialAdvance, showState: false);
        if (TutorialStageReady)
            DrawKeyboardFocusMarker(g, _tutorialAdvanceButton);
    }

    private string TutorialStageTitle() => _tutorialStage switch
    {
        0 => "NODE MOTION",
        1 => "FIELD USE",
        2 => "MISSION FILE",
        3 => "PERK CHANNEL",
        4 => "HOLLOW SIGHT",
        _ => "EXTRACTION"
    };

    private string[] TutorialStageCopy() => _tutorialStage switch
    {
        0 =>
        [
            "WASD OR ARROW KEYS SHIFT",
            "THE DRONE ONE NODE.",
            "WALLS CANCEL A SHIFT.",
            "REACH THE AMBER SOCKET."
        ],
        1 =>
        [
            "E OPERATES A NEARBY MARKED",
            "CASE, SWITCH, PERSON, KIOSK,",
            "OR CONTRACT FIXTURE.",
            "LATCH THE TRAINING CARGO."
        ],
        2 =>
        [
            "Q OPENS THE MISSION FILE.",
            "THE FILE PAUSES SOLO RUNS.",
            "Q OR ESC CLOSES THE FILE.",
            "OPEN AND CLOSE IT ONCE."
        ],
        3 =>
        [
            "SPACE FIRES AN EQUIPPED",
            "ACTIVE PERK. PASSIVE PERKS",
            "REMAIN LIVE AUTOMATICALLY.",
            "FIRE THE TRAINING MODULE."
        ],
        4 =>
        [
            "HOLLOW CONES SHOW THEIR SIGHT.",
            "WALLS BREAK MOST SIGHT LINES.",
            "IMPACTS BREACH FRAME / DROP CARGO.",
            "MOVE BELOW THE PARTITION."
        ],
        _ =>
        [
            "CHECK THE FILE FOR PERSONAL",
            "ORDERS. MARKED CIRCUITS MAY",
            "SEAL EXTRACTION. THE HATCH",
            "ENDS THE RUN WHEN RELEASED."
        ]
    };

    private void DrawTutorialCell(Graphics g, RectangleF cell)
    {
        DrawCutPanel(g, cell, Color.FromArgb(7, 13, 13), Color.FromArgb(65, 81, 68), 15, 4);
        DrawPanelBolts(g, cell, C.Steel);
        LabFont.Draw(g, $"SIMULATION FEED / {TutorialStageTitle()}", cell.X + 24, cell.Y + 23, 2, C.Signal);
        var viewport = new RectangleF(cell.X + 24, cell.Y + 61, cell.Width - 48, 342);
        DrawCutPanel(g, viewport, Color.FromArgb(3, 8, 8), Color.FromArgb(47, 62, 53), 9, 3);

        switch (_tutorialStage)
        {
            case 0:
                DrawTutorialMovement(g, viewport, false);
                break;
            case 1:
                DrawTutorialInteraction(g, viewport);
                break;
            case 2:
                DrawTutorialFile(g, viewport);
                break;
            case 3:
                DrawTutorialPerk(g, viewport);
                break;
            case 4:
                DrawTutorialMovement(g, viewport, true);
                break;
            default:
                DrawTutorialExtraction(g, viewport);
                break;
        }

        DrawTutorialInputBank(g, new RectangleF(cell.X + 24, cell.Bottom - 87, cell.Width - 48, 58));
    }

    private void DrawTutorialMovement(Graphics g, RectangleF viewport, bool danger)
    {
        var origin = new PointF(viewport.X + 104, viewport.Y + 71);
        const float spacingX = 82;
        const float spacingY = 68;
        using var track = new Pen(Color.FromArgb(61, 73, 62), 7);
        using var node = new SolidBrush(C.Steel);
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 6; x++)
        {
            var point = new PointF(origin.X + x * spacingX, origin.Y + y * spacingY);
            if (x < 5) g.DrawLine(track, point, new PointF(point.X + spacingX, point.Y));
            if (y < 3) g.DrawLine(track, point, new PointF(point.X, point.Y + spacingY));
            g.FillRectangle(node, point.X - 6, point.Y - 6, 12, 12);
        }

        var targetCell = danger ? new Point(3, 3) : new Point(4, 2);
        var target = TutorialGridPoint(origin, targetCell, spacingX, spacingY);
        using (var targetOuter = new SolidBrush(C.Oxide))
        using (var targetInner = new SolidBrush(C.Signal))
        {
            var pulse = 13 + MathF.Sin(_time * 6) * 3;
            g.FillRectangle(targetOuter, target.X - pulse, target.Y - pulse, pulse * 2, pulse * 2);
            g.FillRectangle(targetInner, target.X - 6, target.Y - 6, 12, 12);
        }

        if (danger)
        {
            var hollow = TutorialGridPoint(origin, new Point(5, 1), spacingX, spacingY);
            using var cone = new SolidBrush(Color.FromArgb(55, C.Red));
            g.FillPolygon(cone,
            [
                new PointF(hollow.X - 13, hollow.Y),
                new PointF(origin.X, origin.Y - 20),
                new PointF(origin.X, origin.Y + spacingY * 1.92f)
            ]);
            using var wall = new SolidBrush(Color.FromArgb(102, 107, 84));
            using var wallHot = new SolidBrush(C.Oxide);
            g.FillRectangle(wall, origin.X + spacingX * 2.45f, origin.Y + spacingY * 2.24f,
                spacingX * 2.1f, 22);
            g.FillRectangle(wallHot, origin.X + spacingX * 2.45f, origin.Y + spacingY * 2.24f, 8, 22);
            DrawTutorialHollow(g, hollow);
        }

        var drone = TutorialGridPoint(origin, _tutorialDroneCell, spacingX, spacingY);
        DrawDrone(g, _drone, _playerColor, _playerFrameColor, drone, 28, 255,
            drawShadow: true, drawBrackets: false);
    }

    private static PointF TutorialGridPoint(PointF origin, Point cell, float spacingX, float spacingY) =>
        new(origin.X + cell.X * spacingX, origin.Y + cell.Y * spacingY);

    private void DrawTutorialInteraction(Graphics g, RectangleF viewport)
    {
        var center = new PointF(viewport.X + viewport.Width / 2, viewport.Y + 169);
        using var floor = new SolidBrush(Color.FromArgb(25, 35, 32));
        using var rail = new SolidBrush(C.Steel);
        g.FillRectangle(floor, viewport.X + 83, center.Y + 69, viewport.Width - 166, 20);
        g.FillRectangle(rail, viewport.X + 104, center.Y + 74, viewport.Width - 208, 5);

        var caseRect = new RectangleF(center.X + 47, center.Y - 47, 127, 96);
        using var shell = new SolidBrush(_tutorialCargoLatched ? Color.FromArgb(46, 64, 53) : Color.FromArgb(80, 73, 57));
        using var edge = new Pen(_tutorialCargoLatched ? C.Signal : C.Oxide, 5);
        g.FillRectangle(shell, caseRect);
        g.DrawRectangle(edge, caseRect.X, caseRect.Y, caseRect.Width, caseRect.Height);
        g.DrawLine(edge, caseRect.X, caseRect.Y, caseRect.Right, caseRect.Bottom);
        g.DrawLine(edge, caseRect.Right, caseRect.Y, caseRect.X, caseRect.Bottom);
        LabFont.Draw(g, _tutorialCargoLatched ? "LATCHED" : "CARGO", caseRect.X + caseRect.Width / 2,
            caseRect.Y + 39, 1, _tutorialCargoLatched ? C.Signal : C.Bone, LabTextAlign.Center);

        DrawDrone(g, _drone, _playerColor, _playerFrameColor,
            new PointF(center.X - 65, center.Y + 3), 37, 255, true, false);
        using var tether = new Pen(Color.FromArgb(150, C.Signal), 3) { DashStyle = DashStyle.Dot };
        g.DrawLine(tether, center.X - 24, center.Y + 3, caseRect.Left, center.Y + 3);
    }

    private void DrawTutorialFile(Graphics g, RectangleF viewport)
    {
        var desk = new RectangleF(viewport.X + 80, viewport.Y + 67, viewport.Width - 160, viewport.Height - 117);
        using var shadow = new SolidBrush(Color.FromArgb(2, 4, 4));
        using var folder = new SolidBrush(Color.FromArgb(155, 121, 67));
        using var paper = new SolidBrush(Color.FromArgb(204, 192, 149));
        using var ink = new SolidBrush(Color.FromArgb(72, 63, 48));
        g.FillRectangle(shadow, desk.X + 12, desk.Y + 14, desk.Width, desk.Height);
        g.FillRectangle(folder, desk);
        g.FillRectangle(folder, desk.X + 19, desk.Y - 23, 165, 29);
        if (_tutorialFileOpen)
        {
            var page = RectangleF.Inflate(desk, -31, -25);
            g.FillRectangle(paper, page);
            for (var y = page.Y + 61; y < page.Bottom - 21; y += 31)
                g.FillRectangle(ink, page.X + 23, y, page.Width - 46, 2);
            LabFont.Draw(g, "PERSONAL ORDERS", page.X + 24, page.Y + 22, 2, C.Ink);
            LabFont.Draw(g, "[ ] RECOVER TRAINING CASE", page.X + 25, page.Y + 79, 1, C.Ink);
            LabFont.Draw(g, "[ ] RELEASE EXTRACTION", page.X + 25, page.Y + 112, 1, C.Ink);
        }
        else
        {
            LabFont.Draw(g, _tutorialFileWasClosed ? "FILE RETURNED" : "MISSION FILE",
                desk.X + desk.Width / 2, desk.Y + desk.Height / 2 - 11, 2,
                _tutorialFileWasClosed ? C.Signal : C.Ink, LabTextAlign.Center);
        }
    }

    private void DrawTutorialPerk(Graphics g, RectangleF viewport)
    {
        var center = new PointF(viewport.X + viewport.Width / 2, viewport.Y + viewport.Height / 2);
        var activePulse = _tutorialPerkTriggered ? 1f + MathF.Sin(_time * 12) * .11f : 1f;
        using var recess = new SolidBrush(Color.FromArgb(16, 24, 22));
        using var edge = new Pen(_tutorialPerkTriggered ? C.Signal : C.Steel, 7);
        g.FillRectangle(recess, center.X - 129, center.Y - 129, 258, 258);
        g.DrawRectangle(edge, center.X - 129, center.Y - 129, 258, 258);
        using var ring = new Pen(_tutorialPerkTriggered ? C.Signal : C.Oxide, 15);
        g.DrawArc(ring, center.X - 86, center.Y - 86, 172, 172, -90,
            _tutorialPerkTriggered ? 276 + MathF.Sin(_time * 5) * 60 : 360);
        using var core = new SolidBrush(_tutorialPerkTriggered ? C.Bone : C.Steel);
        var radius = 34 * activePulse;
        g.FillPolygon(core,
        [
            new PointF(center.X, center.Y - radius),
            new PointF(center.X + radius, center.Y),
            new PointF(center.X, center.Y + radius),
            new PointF(center.X - radius, center.Y)
        ]);
        LabFont.Draw(g, _tutorialPerkTriggered ? "CHANNEL FIRED" : "MODULE ARMED",
            center.X, center.Y + 113, 1,
            _tutorialPerkTriggered ? C.Signal : C.Sick, LabTextAlign.Center);
    }

    private static void DrawTutorialHollow(Graphics g, PointF center)
    {
        using var line = new Pen(C.Red, 6);
        g.DrawPolygon(line,
        [
            new PointF(center.X, center.Y - 31),
            new PointF(center.X + 31, center.Y),
            new PointF(center.X, center.Y + 31),
            new PointF(center.X - 31, center.Y)
        ]);
        g.DrawPolygon(line,
        [
            new PointF(center.X, center.Y - 18),
            new PointF(center.X + 18, center.Y),
            new PointF(center.X, center.Y + 18),
            new PointF(center.X - 18, center.Y)
        ]);
    }

    private void DrawTutorialExtraction(Graphics g, RectangleF viewport)
    {
        var centerY = viewport.Y + viewport.Height / 2;
        using var rail = new Pen(Color.FromArgb(67, 78, 66), 8);
        g.DrawLine(rail, viewport.X + 72, centerY, viewport.Right - 72, centerY);
        DrawTutorialOrderGlyph(g, new PointF(viewport.X + 132, centerY), "1", C.Signal);
        DrawTutorialOrderGlyph(g, new PointF(viewport.X + 284, centerY), "2", C.Oxide);
        DrawTutorialOrderGlyph(g, new PointF(viewport.X + 436, centerY), "3", C.Sick);

        var exit = new RectangleF(viewport.Right - 142, centerY - 67, 82, 134);
        using var hatch = new SolidBrush(Color.FromArgb(58, 67, 57));
        using var open = new SolidBrush(C.Signal);
        g.FillRectangle(hatch, exit);
        g.FillRectangle(open, exit.X + 14, exit.Y + 14, exit.Width - 28, exit.Height - 28);
        using var dark = new SolidBrush(C.Ink);
        g.FillRectangle(dark, exit.X + 24, exit.Y + 29, exit.Width - 48, exit.Height - 58);
        LabFont.Draw(g, "EXIT", exit.X + exit.Width / 2, exit.Bottom + 18, 1, C.Signal, LabTextAlign.Center);
    }

    private static void DrawTutorialOrderGlyph(Graphics g, PointF center, string number, Color color)
    {
        using var outer = new SolidBrush(color);
        using var inner = new SolidBrush(C.Ink);
        g.FillRectangle(outer, center.X - 36, center.Y - 36, 72, 72);
        g.FillRectangle(inner, center.X - 28, center.Y - 28, 56, 56);
        LabFont.Draw(g, number, center.X, center.Y - 15, 3, color, LabTextAlign.Center, 0);
    }

    private void DrawTutorialInputBank(Graphics g, RectangleF bank)
    {
        foreach (var index in Enumerable.Range(0, _tutorialDirectionButtons.Length))
            _tutorialDirectionButtons[index] = RectangleF.Empty;
        _tutorialInputButton = RectangleF.Empty;

        if (_tutorialStage is 0 or 4)
        {
            var labels = new[] { "^", ">", "V", "<" };
            for (var index = 0; index < 4; index++)
            {
                var rect = new RectangleF(bank.X + 153 + index * 75, bank.Y + 4, 62, 48);
                _tutorialDirectionButtons[index] = rect;
                DrawTutorialKey(g, rect, labels[index], _hoverTutorialDirection == index);
            }
            LabFont.Draw(g, "MOVE", bank.X + 20, bank.Y + 20, 1, C.Steel);
            return;
        }

        var label = _tutorialStage switch
        {
            1 => "E",
            2 => "Q",
            3 => "SPACE",
            _ => string.Empty
        };
        if (label.Length == 0)
        {
            LabFont.Draw(g, "TRAINING RECORD COMPLETE", bank.X + bank.Width / 2, bank.Y + 20,
                1, C.Signal, LabTextAlign.Center);
            return;
        }
        _tutorialInputButton = new RectangleF(bank.X + bank.Width / 2 - 91, bank.Y + 4, 182, 48);
        DrawTutorialKey(g, _tutorialInputButton, label, _hoverTutorialInput);
    }

    private static void DrawTutorialKey(Graphics g, RectangleF rect, string label, bool hovered)
    {
        using var shadow = new SolidBrush(Color.Black);
        using var key = new SolidBrush(hovered ? C.Signal : C.Bone);
        using var edge = new Pen(hovered ? C.Bone : C.Steel, 3);
        g.FillRectangle(shadow, rect.X + 5, rect.Y + 6, rect.Width, rect.Height);
        g.FillRectangle(key, rect);
        g.DrawRectangle(edge, rect.X, rect.Y, rect.Width, rect.Height);
        if (label is "^" or ">" or "V" or "<")
        {
            var center = new PointF(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            var arrow = label switch
            {
                "^" => new[]
                {
                    new PointF(center.X - 9, center.Y + 5),
                    new PointF(center.X, center.Y - 6),
                    new PointF(center.X + 9, center.Y + 5)
                },
                ">" => new[]
                {
                    new PointF(center.X - 5, center.Y - 9),
                    new PointF(center.X + 6, center.Y),
                    new PointF(center.X - 5, center.Y + 9)
                },
                "V" => new[]
                {
                    new PointF(center.X - 9, center.Y - 5),
                    new PointF(center.X, center.Y + 6),
                    new PointF(center.X + 9, center.Y - 5)
                },
                _ => new[]
                {
                    new PointF(center.X + 5, center.Y - 9),
                    new PointF(center.X - 6, center.Y),
                    new PointF(center.X + 5, center.Y + 9)
                }
            };
            using var arrowPen = new Pen(C.Ink, 4)
            {
                LineJoin = LineJoin.Miter,
                StartCap = LineCap.Square,
                EndCap = LineCap.Square
            };
            g.DrawLines(arrowPen, arrow);
        }
        else
            LabFont.Draw(g, label, rect.X + rect.Width / 2, rect.Y + rect.Height / 2 - 7,
                label.Length > 2 ? 1 : 2, C.Ink, LabTextAlign.Center, 0);
    }
}
