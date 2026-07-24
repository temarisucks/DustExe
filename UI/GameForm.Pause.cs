namespace Dust;

internal sealed partial class GameForm
{
    private readonly RectangleF[] _pauseButtons = new RectangleF[4];
    private bool _pauseMenuOpen;
    private bool _pauseSettingsOpen;
    private int _pauseSelection;
    private int _hoverPause = -1;
    private DateTime _offlinePauseOpenedAt;

    private bool IsPauseMenuActive =>
        _pauseMenuOpen && _mode == ScreenMode.Playing;

    private bool OfflinePauseFreezesGame =>
        IsPauseMenuActive && !IsOnlineGameplayActive;

    private void OpenPauseMenu()
    {
        if (_mode != ScreenMode.Playing || _pauseMenuOpen) return;

        // The dossier owns its own offline clock suspension. Commit that
        // interval before the pause menu starts a new one.
        CloseMissionDossier(playSound: false);
        _pauseMenuOpen = true;
        _pauseSettingsOpen = false;
        _pauseSelection = 0;
        _offlinePauseOpenedAt = IsOnlineGameplayActive
            ? default
            : DateTime.Now;
        ResetHover();
        _audio.Play(AudioCue.Confirm);
        Invalidate();
    }

    private void ResumeFromPause(bool playSound = true)
    {
        if (!_pauseMenuOpen) return;
        SettleOfflinePauseClock();
        ResetPauseMenuState();
        ResetHover();
        if (playSound) _audio.Play(AudioCue.Confirm);
        Invalidate();
    }

    private void SettleOfflinePauseClock()
    {
        if (_offlinePauseOpenedAt == default) return;

        var paused = DateTime.Now - _offlinePauseOpenedAt;
        _offlinePauseOpenedAt = default;
        if (paused <= TimeSpan.Zero) return;

        _startedAt += paused;
        // Hit-window achievements use absolute timestamps, so they must move
        // with the mission clock when an offline run resumes.
        for (var index = 0; index < _runHitTimes.Count; index++)
            _runHitTimes[index] = _runHitTimes[index].Add(paused);
    }

    private void ResetPauseMenuState()
    {
        _pauseMenuOpen = false;
        _pauseSettingsOpen = false;
        _pauseSelection = 0;
        _hoverPause = -1;
        _offlinePauseOpenedAt = default;
        Array.Fill(_pauseButtons, RectangleF.Empty);
    }

    private void OpenPauseSettings()
    {
        if (!IsPauseMenuActive) return;
        _pauseSettingsOpen = true;
        _settingsSelection = 0;
        ResetHover();
        _audio.Play(AudioCue.Confirm);
        Invalidate();
    }

    private void ReturnToPauseMenu()
    {
        if (!IsPauseMenuActive || !_pauseSettingsOpen) return;
        _pauseSettingsOpen = false;
        ResetHover();
        _audio.Play(AudioCue.Confirm);
        Invalidate();
    }

    private void HandlePauseKey(KeyEventArgs e)
    {
        if (_pauseSettingsOpen)
        {
            HandlePauseSettingsKey(e);
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            ResumeFromPause();
        }
        else if (e.KeyCode is Keys.W or Keys.Up ||
                 e.Shift && e.KeyCode == Keys.Tab)
        {
            _pauseSelection = Wrap(_pauseSelection - 1, _pauseButtons.Length);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode is Keys.S or Keys.Down or Keys.Tab)
        {
            _pauseSelection = Wrap(_pauseSelection + 1, _pauseButtons.Length);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            ActivatePauseSelection();
        }
        else
        {
            // Gameplay controls are deliberately swallowed while the local
            // console is open. Online authority still advances in TickGame.
            ConsumeKey(e);
            return;
        }

        ConsumeKey(e);
    }

    private void HandlePauseSettingsKey(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            ReturnToPauseMenu();
        }
        else if (e.KeyCode is Keys.W or Keys.Up)
        {
            _settingsSelection = Wrap(_settingsSelection - 1, _settingsRows.Length);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode is Keys.S or Keys.Down)
        {
            _settingsSelection = Wrap(_settingsSelection + 1, _settingsRows.Length);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode is Keys.A or Keys.Left)
        {
            AdjustSetting(_settingsSelection, -1);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode is Keys.D or Keys.Right)
        {
            AdjustSetting(_settingsSelection, 1);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            _audio.Play(AudioCue.Confirm);
            AdjustSetting(_settingsSelection, 1);
        }
        else
        {
            ConsumeKey(e);
            return;
        }

        ConsumeKey(e);
    }

    private void ActivatePauseSelection()
    {
        switch (_pauseSelection)
        {
            case 0:
                ResumeFromPause();
                break;

            case 1:
                OpenPauseSettings();
                break;

            case 2:
                _audio.Play(AudioCue.Confirm);
                ResumeFromPause(playSound: false);
                // A pause-menu exit is a local navigation command, not a
                // request that should leave the player in a live plate while
                // waiting for Railway. EnterTitle tears the session down
                // immediately and sends lobby.leave best-effort in the
                // background.
                EnterTitle();
                break;

            case 3:
                _audio.Play(AudioCue.Confirm);
                ResumeFromPause(playSound: false);
                Close();
                break;
        }
    }

    private void HandlePauseMouseMove(PointF hit)
    {
        if (_pauseSettingsOpen)
        {
            for (var index = 0; index < _settingsRows.Length; index++)
                if (_settingsRows[index].Contains(hit)) _hoverSetting = index;
            _hoverBack = _backButton.Contains(hit);
            return;
        }

        for (var index = 0; index < _pauseButtons.Length; index++)
            if (_pauseButtons[index].Contains(hit)) _hoverPause = index;
    }

    private bool HandlePauseMouseDown(PointF hit)
    {
        if (_pauseSettingsOpen)
        {
            for (var index = 0; index < _settingsRows.Length; index++)
            {
                if (!_settingsRows[index].Contains(hit)) continue;
                _settingsSelection = index;
                if (_settingsDecreaseButtons[index].Contains(hit))
                {
                    _audio.Play(AudioCue.Confirm);
                    AdjustSetting(index, -1);
                }
                else if (_settingsIncreaseButtons[index].Contains(hit) || index == 3)
                {
                    _audio.Play(AudioCue.Confirm);
                    AdjustSetting(index, 1);
                }
                else
                {
                    _audio.Play(AudioCue.Select);
                }
                return true;
            }

            if (_backButton.Contains(hit))
            {
                ReturnToPauseMenu();
                return true;
            }
            return true;
        }

        for (var index = 0; index < _pauseButtons.Length; index++)
        {
            if (!_pauseButtons[index].Contains(hit)) continue;
            _pauseSelection = index;
            ActivatePauseSelection();
            return true;
        }
        return true;
    }

    private void DrawPauseConsole(Graphics g)
    {
        using var veil = new SolidBrush(Color.FromArgb(
            IsOnlineGameplayActive ? 188 : 222, 2, 6, 6));
        g.FillRectangle(veil, 0, 0, DesignWidth, DesignHeight);

        var panel = new RectangleF(326, 84, 628, 632);
        using var shadow = new SolidBrush(Color.FromArgb(230, Color.Black));
        g.FillPolygon(shadow, CutPanelPoints(
            new RectangleF(panel.X + 14, panel.Y + 16, panel.Width, panel.Height), 24));
        DrawCutPanel(g, panel, Color.FromArgb(17, 25, 23),
            Color.FromArgb(107, 103, 79), 24, 6);
        DrawPanelBolts(g, panel, C.Steel);

        using (var titleRail = new SolidBrush(C.Oxide))
        using (var railDark = new SolidBrush(Color.FromArgb(54, 38, 34)))
        {
            g.FillRectangle(railDark, panel.X + 32, panel.Y + 30, panel.Width - 64, 25);
            for (var x = panel.X + 38; x < panel.Right - 45; x += 34)
                g.FillRectangle(titleRail, x, panel.Y + 36, 19, 13);
        }

        LabFont.Draw(g, IsOnlineGameplayActive ? "LOCAL CONSOLE" : "FIELD HOLD",
            panel.X + 38, panel.Y + 80, 4, C.Bone);
        LabFont.Draw(g, IsOnlineGameplayActive
                ? "NETWORK LIVE / PLATE CONTINUES"
                : "PLATE CLOCK SUSPENDED",
            panel.Right - 38, panel.Y + 132, 1,
            IsOnlineGameplayActive ? C.Red : C.Signal, LabTextAlign.Right);

        var labels = new[]
        {
            "RESUME",
            "SETTINGS",
            "QUIT TO MENU",
            "QUIT TO DESKTOP"
        };

        for (var index = 0; index < _pauseButtons.Length; index++)
        {
            var rect = new RectangleF(panel.X + 62, panel.Y + 166 + index * 88,
                panel.Width - 124, 65);
            _pauseButtons[index] = rect;
            var focused = _pauseSelection == index;
            DrawPauseCartridge(g, rect, labels[index], index,
                focused, _hoverPause == index);
            if (focused) DrawKeyboardFocusMarker(g, rect);
        }

        using var footer = new SolidBrush(Color.FromArgb(55, 67, 58));
        g.FillRectangle(footer, panel.X + 42, panel.Bottom - 67,
            panel.Width - 84, 4);
        LabFont.Draw(g, $"SUBJECT {_playerCell.X:00}:{_playerCell.Y:00}",
            panel.X + 44, panel.Bottom - 43, 1, C.Steel);
        LabFont.Draw(g, IsOnlineGameplayActive ? "REMOTE AUTHORITY RETAINED" : "LOCAL AUTHORITY HELD",
            panel.Right - 44, panel.Bottom - 43, 1,
            IsOnlineGameplayActive ? C.Signal : C.Sick, LabTextAlign.Right);
    }

    private static void DrawPauseCartridge(
        Graphics g,
        RectangleF rect,
        string label,
        int index,
        bool focused,
        bool hovered)
    {
        var active = focused || hovered;
        DrawCutPanel(g, rect,
            focused ? Color.FromArgb(47, 55, 44) : Color.FromArgb(22, 31, 29),
            active ? C.Signal : C.Steel, 11, focused ? 4 : 2);

        using var indexBay = new SolidBrush(active ? C.Oxide : Color.FromArgb(66, 57, 47));
        using var slot = new SolidBrush(Color.Black);
        g.FillRectangle(slot, rect.X + 18, rect.Y + 14, 48, rect.Height - 28);
        g.FillRectangle(indexBay, rect.X + 23, rect.Y + 19, 38, rect.Height - 38);
        LabFont.Draw(g, $"{index + 1:00}", rect.X + 42, rect.Y + rect.Height / 2 - 7,
            1, C.Bone, LabTextAlign.Center);
        LabFont.Draw(g, label, rect.X + 88, rect.Y + rect.Height / 2 - 10,
            2, active ? C.Bone : C.Sick);

        using var latch = new SolidBrush(active ? C.Signal : C.Steel);
        g.FillRectangle(latch, rect.Right - 35, rect.Y + 16, 12, rect.Height - 32);
    }
}
