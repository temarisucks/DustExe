namespace Dust;

internal sealed partial class GameForm
{
    private void EnterTitle(bool resetSelection = false)
    {
        var previousMode = _mode;
        if (_pauseMenuOpen)
        {
            SettleOfflinePauseClock();
            ResetPauseMenuState();
        }
        DisconnectOnlineSessionForTitle();
        CloseInventory(playSound: false);
        ResetInventoryOverlay();
        CloseMissionDossier(playSound: false);
        ResetMissionDossier();
        if (_mode == ScreenMode.Playing) RecordAchievementAbandonment();
        if (!IsMenuFamilyMode(previousMode)) _audio.StopMusic();
        _mode = ScreenMode.Title;
        if (resetSelection) _menuSelection = 0;
        ResetHover();
        RequestMenuMusic();
    }

    private void OpenCustomize()
    {
        _mode = ScreenMode.Customize;
        _customizeSection = 0;
        _customizeIndex = (int)_drone;
        ResetHover();
    }

    private void OpenSettings()
    {
        _mode = ScreenMode.Settings;
        _settingsSelection = 0;
        ResetHover();
    }

    private void ActivateTitleSelection()
    {
        _audio.Play(AudioCue.Confirm);
        switch (_menuSelection)
        {
            case 0:
                OpenRunSettings();
                break;
            case 1:
                OpenOnlinePlay();
                break;
            case 2:
                OpenCustomize();
                break;
            case 3:
                OpenProgression();
                break;
            case 4:
                OpenSettings();
                break;
        }
    }

    private void ActivateCustomizeSelection()
    {
        _audio.Play(AudioCue.Confirm);
        var changed = true;
        switch (_customizeSection)
        {
            case 0:
                _drone = (DroneModel)Math.Clamp(_customizeIndex, 0, _droneButtons.Length - 1);
                break;
            case 1:
                _paintPart = (DronePaintPart)Math.Clamp(_customizeIndex, 0, 1);
                break;
            case 2:
                var color = _palette[Math.Clamp(_customizeIndex, 0, _palette.Length - 1)];
                if (_paintPart == DronePaintPart.Core) _playerColor = color;
                else _playerFrameColor = color;
                break;
            default:
                changed = false;
                EnterTitle();
                break;
        }
        if (changed) SaveSettings();
    }

    private void MoveTitleSelection(int direction)
    {
        _menuSelection = Wrap(_menuSelection + direction, _titleButtons.Length);
        _audio.Play(AudioCue.Select);
    }

    private void MoveCustomizeSection(int direction)
    {
        _customizeSection = Wrap(_customizeSection + direction, 4);
        _customizeIndex = _customizeSection switch
        {
            0 => (int)_drone,
            1 => (int)_paintPart,
            2 => SelectedPaletteIndex(),
            _ => 0
        };
        _audio.Play(AudioCue.Select);
    }

    private void MoveCustomizeSelection(int direction)
    {
        var count = _customizeSection switch
        {
            0 => _droneButtons.Length,
            1 => 2,
            2 => _palette.Length,
            _ => 1
        };
        if (_customizeSection == 2)
        {
            // The color bank is a real 6x2 grid. Horizontal input stays on the
            // visible row instead of falling off the right edge onto the next
            // row's first swatch.
            var rowStart = Math.Clamp(_customizeIndex / 6, 0, 1) * 6;
            var column = Math.Clamp(_customizeIndex % 6 + direction, 0, 5);
            _customizeIndex = rowStart + column;
        }
        else
        {
            _customizeIndex = Math.Clamp(_customizeIndex + direction, 0, count - 1);
        }
        _audio.Play(AudioCue.Select);
    }

    private void MoveCustomizeVertical(int direction)
    {
        if (direction < 0)
        {
            switch (_customizeSection)
            {
                case 1:
                    _customizeSection = 0;
                    _customizeIndex = _customizeIndex == 0 ? 1 : 3;
                    break;
                case 2 when _customizeIndex >= 6:
                    _customizeIndex -= 6;
                    break;
                case 2:
                    _customizeSection = 1;
                    _customizeIndex = _customizeIndex < 3 ? 0 : 1;
                    break;
                case 3:
                    _customizeSection = 2;
                    _customizeIndex = 6;
                    break;
                default:
                    return;
            }
        }
        else
        {
            switch (_customizeSection)
            {
                case 0:
                    _customizeSection = 1;
                    _customizeIndex = _customizeIndex <= 1 ? 0 : 1;
                    break;
                case 1:
                    _customizeSection = 2;
                    _customizeIndex = _customizeIndex == 0 ? 1 : 4;
                    break;
                case 2 when _customizeIndex < 6:
                    _customizeIndex += 6;
                    break;
                case 2:
                    _customizeSection = 3;
                    _customizeIndex = 0;
                    break;
                default:
                    return;
            }
        }
        _audio.Play(AudioCue.Select);
    }

    private int SelectedPaletteIndex()
    {
        var selected = _paintPart == DronePaintPart.Core ? _playerColor : _playerFrameColor;
        for (var i = 0; i < _palette.Length; i++)
            if (_palette[i].ToArgb() == selected.ToArgb()) return i;
        return 0;
    }

    private static int Wrap(int value, int count)
    {
        if (count <= 0) return 0;
        value %= count;
        return value < 0 ? value + count : value;
    }
}
