namespace Dust;

internal sealed partial class GameForm
{
    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F11 || (e.Alt && e.KeyCode == Keys.Enter))
        {
            _audio.Play(AudioCue.Confirm);
            ToggleFullscreen();
            ConsumeKey(e);
            return;
        }

        if (IsPauseMenuActive)
        {
            HandlePauseKey(e);
            return;
        }

        switch (_mode)
        {
            case ScreenMode.TutorialOffer:
                HandleTutorialOfferKey(e);
                break;
            case ScreenMode.Tutorial:
                HandleTutorialKey(e);
                break;
            case ScreenMode.Title:
                HandleTitleKey(e);
                break;
            case ScreenMode.RunSettings:
                HandleRunSettingsKey(e);
                break;
            case ScreenMode.Customize:
                HandleCustomizeKey(e);
                break;
            case ScreenMode.Settings:
                HandleSettingsKey(e);
                break;
            case ScreenMode.Achievements:
                HandleProgressionKey(e);
                break;
            case ScreenMode.OnlineAccount:
                HandleOnlineAccountKey(e);
                break;
            case ScreenMode.LobbyBrowser:
                HandleLobbyBrowserKey(e);
                break;
            case ScreenMode.LobbyRoom:
                HandleLobbyRoomKey(e);
                break;
            case ScreenMode.Playing:
                HandlePlayingKey(e);
                break;
            case ScreenMode.Shop:
                HandleShopKey(e);
                break;
            case ScreenMode.Won:
                HandleWonKey(e);
                break;
            case ScreenMode.Failed:
                HandleFailedKey(e);
                break;
        }
    }

    private void HandleTitleKey(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.W or Keys.Up) MoveTitleSelection(-1);
        else if (e.KeyCode is Keys.S or Keys.Down) MoveTitleSelection(1);
        else if (e.KeyCode is Keys.Enter or Keys.Space) ActivateTitleSelection();
        else if (e.KeyCode == Keys.Escape) Close();
        else return;
        ConsumeKey(e);
    }

    private void HandleCustomizeKey(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            _audio.Play(AudioCue.Confirm);
            EnterTitle();
        }
        else if (e.KeyCode is Keys.W or Keys.Up || e.Shift && e.KeyCode == Keys.Tab)
            MoveCustomizeSection(-1);
        else if (e.KeyCode is Keys.S or Keys.Down or Keys.Tab)
            MoveCustomizeSection(1);
        else if (e.KeyCode is Keys.A or Keys.Left)
            MoveCustomizeSelection(-1);
        else if (e.KeyCode is Keys.D or Keys.Right)
            MoveCustomizeSelection(1);
        else if (e.KeyCode is Keys.Enter or Keys.Space)
            ActivateCustomizeSelection();
        else return;
        ConsumeKey(e);
    }

    private void HandleSettingsKey(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            _audio.Play(AudioCue.Confirm);
            EnterTitle();
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
        else return;
        ConsumeKey(e);
    }

    private void HandlePlayingKey(KeyEventArgs e)
    {
        if (_missionDossierOpen)
        {
            if (e.KeyCode == Keys.Escape)
            {
                CloseMissionDossier(playSound: false);
                OpenPauseMenu();
            }
            else if (e.KeyCode == Keys.Q)
            {
                CloseMissionDossier();
            }
            ConsumeKey(e);
            return;
        }
        if (_failurePending)
        {
            ConsumeKey(e);
            return;
        }
        if (e.KeyCode == Keys.Q)
        {
            OpenMissionDossier();
            ConsumeKey(e);
            return;
        }
        if (e.KeyCode == Keys.Escape)
        {
            OpenPauseMenu();
            ConsumeKey(e);
            return;
        }
        if (e.KeyCode == Keys.E)
        {
            TryPickupCargo();
            ConsumeKey(e);
            return;
        }
        if (e.KeyCode == Keys.Space && TryActivateSpacePerk())
        {
            ConsumeKey(e);
            return;
        }

        var direction = e.KeyCode switch
        {
            Keys.W or Keys.Up => Direction.Up,
            Keys.D or Keys.Right => Direction.Right,
            Keys.S or Keys.Down => Direction.Down,
            Keys.A or Keys.Left => Direction.Left,
            _ => (Direction?)null
        };
        if (!direction.HasValue) return;
        TryMove(direction.Value);
        ConsumeKey(e);
    }

    private void HandleWonKey(KeyEventArgs e)
    {
        // The cycle record is a committed printer sequence. Inputs are swallowed
        // until the final character lands so it cannot be skipped or bypassed.
        if (!ResultReady)
        {
            ConsumeKey(e);
            return;
        }

        if (e.KeyCode is Keys.A or Keys.Left or Keys.W or Keys.Up)
        {
            _resultSelection = Wrap(_resultSelection - 1, 2);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode is Keys.D or Keys.Right or Keys.S or Keys.Down or Keys.Tab)
        {
            _resultSelection = Wrap(_resultSelection + 1, 2);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            if (_onlineMatchActive)
            {
                if (_resultSelection == 0) FinishOnlineRunToLobby();
                else LeaveOnlineLobby();
            }
            else
            {
                _audio.Play(AudioCue.Confirm);
                if (_resultSelection == 0) StartGame(true);
                else EnterTitle();
            }
        }
        else if (e.KeyCode == Keys.Escape)
        {
            if (_onlineMatchActive) LeaveOnlineLobby();
            else
            {
                _audio.Play(AudioCue.Confirm);
                EnterTitle();
            }
        }
        else return;
        ConsumeKey(e);
    }

    private void HandleFailedKey(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            if (_onlineMatchActive) FinishOnlineRunToLobby();
            else
            {
                _audio.Play(AudioCue.Confirm);
                RestartAfterFailure();
            }
        }
        else if (e.KeyCode == Keys.Escape)
        {
            if (_onlineMatchActive) LeaveOnlineLobby();
            else
            {
                _audio.Play(AudioCue.Confirm);
                EnterTitle();
            }
        }
        else return;
        ConsumeKey(e);
    }

    private static void ConsumeKey(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void HandleMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            if (!_settings.Fullscreen)
                Location = new Point(Location.X + e.X - _dragOrigin.X, Location.Y + e.Y - _dragOrigin.Y);
            return;
        }

        var oldToken = CurrentHoverToken();
        ResetHover();
        if (!TryWindowToScene(e.Location, out var hit))
        {
            Cursor = _labCursor.Cursor;
            return;
        }
        if (_closeButton.Contains(hit) || _minButton.Contains(hit))
        {
            Cursor = _labActionCursor.Cursor;
            return;
        }

        if (IsPauseMenuActive)
        {
            HandlePauseMouseMove(hit);
        }
        else switch (_mode)
        {
            case ScreenMode.TutorialOffer:
            case ScreenMode.Tutorial:
                HandleTutorialMouseMove(hit);
                break;
            case ScreenMode.Title:
                for (var i = 0; i < _titleButtons.Length; i++)
                    if (_titleButtons[i].Contains(hit)) _hoverMenu = i;
                break;
            case ScreenMode.RunSettings:
                for (var i = 0; i < _runSettingRows.Length; i++)
                    if (_runSettingRows[i].Contains(hit)) _hoverRunSetting = i;
                _hoverRunStart = _runStartButton.Contains(hit);
                _hoverBack = _backButton.Contains(hit);
                break;
            case ScreenMode.Customize:
                for (var i = 0; i < _droneButtons.Length; i++)
                    if (_droneButtons[i].Contains(hit)) _hoverDrone = i;
                for (var i = 0; i < _paintPartButtons.Length; i++)
                    if (_paintPartButtons[i].Contains(hit)) _hoverPaintPart = i;
                for (var i = 0; i < _colorButtons.Length; i++)
                    if (_colorButtons[i].Contains(hit)) _hoverColor = i;
                _hoverBack = _backButton.Contains(hit);
                break;
            case ScreenMode.Settings:
                for (var i = 0; i < _settingsRows.Length; i++)
                    if (_settingsRows[i].Contains(hit)) _hoverSetting = i;
                _hoverBack = _backButton.Contains(hit);
                break;
            case ScreenMode.Achievements:
                for (var i = 0; i < _progressionTabButtons.Length; i++)
                    if (_progressionTabButtons[i].Contains(hit)) _hoverProgressionTab = i;
                for (var row = 0; row < _progressionRows.Length; row++)
                    if (_progressionRows[row].Contains(hit) && ProgressionIndexForRow(row) >= 0)
                        _hoverProgressionRow = row;
                _hoverProgressionToggle = _progressionTab == 1 && _progressionToggleButton.Contains(hit);
                _hoverBack = _backButton.Contains(hit);
                break;
            case ScreenMode.OnlineAccount:
            case ScreenMode.LobbyBrowser:
            case ScreenMode.LobbyRoom:
                HandleOnlineMouseMove(hit);
                break;
            case ScreenMode.Playing:
                if (_missionDossierOpen)
                    _hoverMissionDossierClose = _missionDossierCloseButton.Contains(hit);
                else
                    _hoverMissionDossier = _missionDossierButton.Contains(hit);
                break;
            case ScreenMode.Shop:
                HandleShopMouseMove(hit);
                break;
            case ScreenMode.Won:
            case ScreenMode.Failed:
                if (_againButton.Contains(hit)) _hoverOverlay = 0;
                else if (_menuButton.Contains(hit)) _hoverOverlay = 1;
                break;
        }

        var newToken = CurrentHoverToken();
        if (newToken >= 0 && newToken != oldToken) _audio.Play(AudioCue.Select);
        Cursor = newToken >= 0 ? _labActionCursor.Cursor : _labCursor.Cursor;
    }

    private void HandleMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !TryWindowToScene(e.Location, out var hit)) return;
        if (_closeButton.Contains(hit)) { Close(); return; }
        if (_minButton.Contains(hit)) { WindowState = FormWindowState.Minimized; return; }

        if (IsPauseMenuActive)
        {
            HandlePauseMouseDown(hit);
            return;
        }
        if (_mode is ScreenMode.TutorialOffer or ScreenMode.Tutorial)
        {
            if (HandleTutorialMouseDown(hit)) return;
        }
        else if (_mode == ScreenMode.Title)
        {
            for (var i = 0; i < _titleButtons.Length; i++)
            {
                if (!_titleButtons[i].Contains(hit)) continue;
                _menuSelection = i;
                ActivateTitleSelection();
                return;
            }
        }
        else if (_mode == ScreenMode.RunSettings)
        {
            for (var index = 0; index < _runSettingRows.Length; index++)
            {
                if (!_runSettingRows[index].Contains(hit)) continue;
                _runSettingsSelection = index;
                if (index < 3)
                {
                    if (_runSettingDecreaseButtons[index].Contains(hit))
                    {
                        _audio.Play(AudioCue.Confirm);
                        AdjustRunSettingsSelection(-1);
                    }
                    else if (_runSettingIncreaseButtons[index].Contains(hit))
                    {
                        _audio.Play(AudioCue.Confirm);
                        AdjustRunSettingsSelection(1);
                    }
                    else
                        _audio.Play(AudioCue.Select);
                }
                else
                    ActivateRunSettingsSelection();
                return;
            }
            if (_runStartButton.Contains(hit))
            {
                _runSettingsSelection = 11;
                ActivateRunSettingsSelection();
                return;
            }
            if (_backButton.Contains(hit))
            {
                _runSettingsSelection = 12;
                ActivateRunSettingsSelection();
                return;
            }
        }
        else if (_mode == ScreenMode.Customize)
        {
            for (var i = 0; i < _droneButtons.Length; i++)
            {
                if (!_droneButtons[i].Contains(hit)) continue;
                _customizeSection = 0;
                _customizeIndex = i;
                ActivateCustomizeSelection();
                return;
            }
            for (var i = 0; i < _paintPartButtons.Length; i++)
            {
                if (!_paintPartButtons[i].Contains(hit)) continue;
                _customizeSection = 1;
                _customizeIndex = i;
                ActivateCustomizeSelection();
                return;
            }
            for (var i = 0; i < _colorButtons.Length; i++)
            {
                if (!_colorButtons[i].Contains(hit)) continue;
                _customizeSection = 2;
                _customizeIndex = i;
                ActivateCustomizeSelection();
                return;
            }
            if (_backButton.Contains(hit))
            {
                _customizeSection = 3;
                ActivateCustomizeSelection();
                return;
            }
        }
        else if (_mode == ScreenMode.Settings)
        {
            for (var i = 0; i < _settingsRows.Length; i++)
            {
                if (!_settingsRows[i].Contains(hit)) continue;
                _settingsSelection = i;
                if (_settingsDecreaseButtons[i].Contains(hit))
                {
                    _audio.Play(AudioCue.Confirm);
                    AdjustSetting(i, -1);
                }
                else if (_settingsIncreaseButtons[i].Contains(hit) || i == 3)
                {
                    _audio.Play(AudioCue.Confirm);
                    AdjustSetting(i, 1);
                }
                return;
            }
            if (_backButton.Contains(hit))
            {
                _audio.Play(AudioCue.Confirm);
                EnterTitle();
                return;
            }
        }
        else if (_mode == ScreenMode.Achievements)
        {
            for (var index = 0; index < _progressionTabButtons.Length; index++)
            {
                if (!_progressionTabButtons[index].Contains(hit)) continue;
                _progressionTab = index;
                _audio.Play(AudioCue.Confirm);
                ResetHover();
                return;
            }
            for (var row = 0; row < _progressionRows.Length; row++)
            {
                if (!_progressionRows[row].Contains(hit)) continue;
                var index = ProgressionIndexForRow(row);
                if (index < 0) break;
                if (_progressionTab == 0) _achievementSelection = index;
                else _perkSelection = index;
                _audio.Play(AudioCue.Select);
                return;
            }
            if (_progressionTab == 1 && _progressionToggleButton.Contains(hit))
            {
                ToggleSelectedPerk();
                return;
            }
            if (_backButton.Contains(hit))
            {
                _audio.Play(AudioCue.Confirm);
                EnterTitle();
                return;
            }
        }
        else if (_mode is ScreenMode.OnlineAccount or ScreenMode.LobbyBrowser or ScreenMode.LobbyRoom)
        {
            if (HandleOnlineMouseDown(hit)) return;
        }
        else if (_mode == ScreenMode.Playing)
        {
            if (_missionDossierOpen)
            {
                if (_missionDossierCloseButton.Contains(hit))
                    CloseMissionDossier();
                return;
            }
            if (_missionDossierButton.Contains(hit))
            {
                OpenMissionDossier();
                return;
            }
        }
        else if (_mode == ScreenMode.Shop)
        {
            if (HandleShopMouseDown(hit)) return;
        }
        else if (_mode == ScreenMode.Won)
        {
            if (_againButton.Contains(hit))
            {
                if (!ResultReady) return;
                _resultSelection = 0;
                if (_onlineMatchActive) FinishOnlineRunToLobby();
                else
                {
                    _audio.Play(AudioCue.Confirm);
                    StartGame(true);
                }
                return;
            }
            if (_menuButton.Contains(hit))
            {
                if (!ResultReady) return;
                _resultSelection = 1;
                if (_onlineMatchActive) LeaveOnlineLobby();
                else
                {
                    _audio.Play(AudioCue.Confirm);
                    EnterTitle();
                }
                return;
            }
        }
        else if (_mode == ScreenMode.Failed)
        {
            if (_againButton.Contains(hit))
            {
                if (_onlineMatchActive) FinishOnlineRunToLobby();
                else
                {
                    _audio.Play(AudioCue.Confirm);
                    RestartAfterFailure();
                }
                return;
            }
            if (_menuButton.Contains(hit))
            {
                if (_onlineMatchActive) LeaveOnlineLobby();
                else
                {
                    _audio.Play(AudioCue.Confirm);
                    EnterTitle();
                }
                return;
            }
        }

        if (!_settings.Fullscreen && hit.Y < 30)
        {
            _dragging = true;
            _dragOrigin = e.Location;
        }
    }

    private void HandleMouseWheel(object? sender, MouseEventArgs e)
    {
        if (_mode != ScreenMode.Achievements || e.Delta == 0) return;
        MoveProgressionSelection(e.Delta > 0 ? -1 : 1);
        ResetHover();
    }

    private int CurrentHoverToken()
    {
        if (_hoverTutorialOffer >= 0) return 10 + _hoverTutorialOffer;
        if (_hoverTutorialDirection >= 0) return 20 + _hoverTutorialDirection;
        if (_hoverTutorialInput) return 25;
        if (_hoverTutorialAdvance) return 26;
        if (_hoverTutorialLeave) return 27;
        if (_hoverMenu >= 0) return 100 + _hoverMenu;
        if (_hoverRunSetting >= 0) return 150 + _hoverRunSetting;
        if (_hoverRunStart) return 160;
        if (_hoverDrone >= 0) return 200 + _hoverDrone;
        if (_hoverPaintPart >= 0) return 220 + _hoverPaintPart;
        if (_hoverColor >= 0) return 240 + _hoverColor;
        if (_hoverSetting >= 0) return 300 + _hoverSetting;
        if (_hoverProgressionTab >= 0) return 340 + _hoverProgressionTab;
        if (_hoverProgressionRow >= 0) return 350 + _hoverProgressionRow;
        if (_hoverProgressionToggle) return 370;
        if (_hoverShopCommand >= 0) return 380 + _hoverShopCommand;
        if (_hoverShopRow >= 0) return 390 + _hoverShopRow;
        if (_hoverMissionDossier) return 395;
        if (_hoverMissionDossierClose) return 396;
        if (_hoverBack) return 400;
        if (_hoverPause >= 0) return 410 + _hoverPause;
        if (_hoverOverlay >= 0) return 500 + _hoverOverlay;
        if (_onlineHover >= 0) return 600 + _onlineHover;
        return -1;
    }
}
