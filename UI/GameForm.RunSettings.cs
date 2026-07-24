namespace Dust;

internal sealed partial class GameForm
{
    private static readonly string[] RunMapSizeNames = ["SMALL", "MEDIUM", "LARGE"];
    private static readonly string[] RunStrictnessNames = ["STRICT", "NORMAL", "LOOSE"];
    private static readonly string[] RunHollowAmountNames = ["NONE", "SMALL", "NORMAL", "LARGE"];
    private static readonly string[] RunHollowTypeNames =
        ["SQUARE", "DIAMOND", "HEX", "SENTRY", "TRIANGLE", "CAMERA", "STAR"];
    private static readonly RunHollowTypes[] RunHollowTypeFlags =
    [
        RunHollowTypes.Square,
        RunHollowTypes.Diamond,
        RunHollowTypes.Hex,
        RunHollowTypes.Sentry,
        RunHollowTypes.Triangle,
        RunHollowTypes.Camera,
        RunHollowTypes.Star
    ];

    private readonly RectangleF[] _runSettingRows = new RectangleF[11];
    private readonly RectangleF[] _runSettingDecreaseButtons = new RectangleF[3];
    private readonly RectangleF[] _runSettingIncreaseButtons = new RectangleF[3];
    private RectangleF _runStartButton;
    private int _runSettingsSelection;
    private int _hoverRunSetting = -1;
    private bool _hoverRunStart;

    private void OpenRunSettings()
    {
        _mode = ScreenMode.RunSettings;
        _runSettingsSelection = 0;
        ResetHover();
    }

    private void HandleRunSettingsKey(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            _audio.Play(AudioCue.Confirm);
            EnterTitle();
        }
        else if (e.KeyCode is Keys.W or Keys.Up || e.Shift && e.KeyCode == Keys.Tab)
        {
            _runSettingsSelection = Wrap(_runSettingsSelection - 1, 13);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode is Keys.S or Keys.Down or Keys.Tab)
        {
            _runSettingsSelection = Wrap(_runSettingsSelection + 1, 13);
            _audio.Play(AudioCue.Select);
        }
        else if (e.KeyCode is Keys.A or Keys.Left)
        {
            AdjustRunSettingsSelection(-1);
        }
        else if (e.KeyCode is Keys.D or Keys.Right)
        {
            AdjustRunSettingsSelection(1);
        }
        else if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            ActivateRunSettingsSelection();
        }
        else return;
        ConsumeKey(e);
    }

    private void AdjustRunSettingsSelection(int direction)
    {
        switch (_runSettingsSelection)
        {
            case 0:
                _runSettings.MapSize = (RunMapSize)Wrap((int)_runSettings.MapSize + direction, 3);
                break;
            case 1:
                _runSettings.Strictness = (MazeStrictness)Wrap((int)_runSettings.Strictness + direction, 3);
                break;
            case 2:
                _runSettings.HollowAmount = (RunHollowAmount)Wrap((int)_runSettings.HollowAmount + direction, 4);
                break;
            case >= 3 and <= 9:
                SetRunHollowType(RunHollowTypeFlags[_runSettingsSelection - 3], direction > 0);
                break;
            case 10:
                _runSettings.DifficultyScaling = direction > 0;
                break;
            case 11:
                if (direction < 0) _runSettingsSelection = 12;
                break;
            case 12:
                if (direction > 0) _runSettingsSelection = 11;
                break;
        }
        _audio.Play(AudioCue.Select);
    }

    private void ActivateRunSettingsSelection()
    {
        _audio.Play(AudioCue.Confirm);
        switch (_runSettingsSelection)
        {
            case 0:
                _runSettings.MapSize = (RunMapSize)Wrap((int)_runSettings.MapSize + 1, 3);
                break;
            case 1:
                _runSettings.Strictness = (MazeStrictness)Wrap((int)_runSettings.Strictness + 1, 3);
                break;
            case 2:
                _runSettings.HollowAmount = (RunHollowAmount)Wrap((int)_runSettings.HollowAmount + 1, 4);
                break;
            case >= 3 and <= 9:
                ToggleRunHollowType(RunHollowTypeFlags[_runSettingsSelection - 3]);
                break;
            case 10:
                _runSettings.DifficultyScaling = !_runSettings.DifficultyScaling;
                break;
            case 11:
                StartConfiguredRun();
                break;
            case 12:
                EnterTitle();
                break;
        }
    }

    private void ToggleRunHollowType(RunHollowTypes type) =>
        SetRunHollowType(type, !_runSettings.HollowTypes.HasFlag(type));

    private void SetRunHollowType(RunHollowTypes type, bool enabled)
    {
        var updated = enabled
            ? _runSettings.HollowTypes | type
            : _runSettings.HollowTypes & ~type;
        updated &= RunHollowTypes.All;
        // Hollow Amount is the sole authority for enemy-free runs. Keeping one
        // socket armed also prevents challenge conditions from being bypassed
        // by selecting a nonzero amount with an empty roster.
        if (updated != RunHollowTypes.None) _runSettings.HollowTypes = updated;
    }

    private void DrawRunSettingsConsole(Graphics g)
    {
        var shell = new RectangleF(42, 54, DesignWidth - 84, DesignHeight - 108);
        DrawMenuConsoleShell(g, shell, "RUN CONFIGURATION");
        LabFont.Draw(g, "RUN SETTINGS", 72, 74, 3, C.Bone);

        var parameterBay = new RectangleF(72, 130, 538, 484);
        var rosterBay = new RectangleF(638, 130, 550, 484);
        DrawCutPanel(g, parameterBay, Color.FromArgb(11, 18, 18), Color.FromArgb(72, 86, 73), 16, 4);
        DrawCutPanel(g, rosterBay, Color.FromArgb(11, 18, 18), Color.FromArgb(72, 86, 73), 16, 4);
        DrawPanelBolts(g, parameterBay, C.Steel);
        DrawPanelBolts(g, rosterBay, C.Steel);
        LabFont.Draw(g, "FIELD GEOMETRY", parameterBay.X + 24, parameterBay.Y + 23, 2, C.Signal);
        LabFont.Draw(g, "HOLLOW ROSTER", rosterBay.X + 24, rosterBay.Y + 23, 2, C.Signal);

        var parameterLabels = new[] { "MAP SIZE", "MAZE STRICTNESS", "HOLLOW AMOUNT" };
        var parameterValues = new[]
        {
            RunMapSizeNames[(int)_runSettings.MapSize],
            RunStrictnessNames[(int)_runSettings.Strictness],
            RunHollowAmountNames[(int)_runSettings.HollowAmount]
        };
        for (var index = 0; index < 3; index++)
        {
            var rect = new RectangleF(parameterBay.X + 22, parameterBay.Y + 66 + index * 104,
                parameterBay.Width - 44, 86);
            _runSettingRows[index] = rect;
            DrawRunScalarRow(g, rect, index, parameterLabels[index], parameterValues[index]);
        }

        var scalingRect = new RectangleF(parameterBay.X + 22, parameterBay.Y + 378,
            parameterBay.Width - 44, 78);
        _runSettingRows[10] = scalingRect;
        DrawRunToggleRow(g, scalingRect, "DIFFICULTY SCALING", _runSettings.DifficultyScaling,
            _runSettingsSelection == 10, _hoverRunSetting == 10);

        for (var index = 0; index < RunHollowTypeFlags.Length; index++)
        {
            var rowIndex = index + 3;
            var rect = new RectangleF(rosterBay.X + 22, rosterBay.Y + 60 + index * 49,
                rosterBay.Width - 44, 40);
            _runSettingRows[rowIndex] = rect;
            DrawRunToggleRow(g, rect, RunHollowTypeNames[index],
                _runSettings.HollowTypes.HasFlag(RunHollowTypeFlags[index]),
                _runSettingsSelection == rowIndex, _hoverRunSetting == rowIndex);
        }

        var preview = _runSettings.Snapshot();
        var enemyText = preview.HollowAmount == RunHollowAmount.None
            ? "NO HOLLOW SIGNALS"
            : preview.HollowTypes == RunHollowTypes.None
                ? "NO TYPES ENABLED"
                : $"{RunHollowAmountNames[(int)preview.HollowAmount]} DENSITY";
        LabFont.Draw(g, enemyText, rosterBay.X + 28, rosterBay.Bottom - 62, 1,
            preview.HollowAmount == RunHollowAmount.None || preview.HollowTypes == RunHollowTypes.None
                ? C.Oxide : C.Sick);
        LabFont.Draw(g, preview.DifficultyScaling ? "SCALING ACTIVE" : "FIXED DIFFICULTY",
            rosterBay.Right - 28, rosterBay.Bottom - 62, 1,
            preview.DifficultyScaling ? C.Signal : C.Steel, LabTextAlign.Right);

        _backButton = new RectangleF(72, 650, 236, 62);
        _runStartButton = new RectangleF(866, 650, 322, 62);
        DrawAbortButton(g, _backButton, "BACK", _hoverBack || _runSettingsSelection == 12);
        DrawLatchButton(g, _runStartButton, "BEGIN RUN", _hoverRunStart || _runSettingsSelection == 11,
            showState: false);
        if (_runSettingsSelection == 11) DrawKeyboardFocusMarker(g, _runStartButton);
        if (_runSettingsSelection == 12) DrawKeyboardFocusMarker(g, _backButton);
    }

    private void DrawRunScalarRow(Graphics g, RectangleF rect, int index, string label, string value)
    {
        var focused = _runSettingsSelection == index;
        var hovered = _hoverRunSetting == index;
        var active = focused || hovered;
        DrawCutPanel(g, rect, focused ? Color.FromArgb(38, 49, 43) : Color.FromArgb(19, 27, 26),
            active ? C.Signal : C.Steel, 10, focused ? 4 : 2);
        LabFont.Draw(g, label, rect.X + 20, rect.Y + 15, 1, active ? C.Bone : C.Sick);

        _runSettingDecreaseButtons[index] = new RectangleF(rect.X + 18, rect.Bottom - 43, 49, 30);
        _runSettingIncreaseButtons[index] = new RectangleF(rect.Right - 67, rect.Bottom - 43, 49, 30);
        DrawRunStepper(g, _runSettingDecreaseButtons[index], "-", active);
        DrawRunStepper(g, _runSettingIncreaseButtons[index], "+", active);
        var readout = new RectangleF(rect.X + 77, rect.Bottom - 43, rect.Width - 154, 30);
        using var readoutFill = new SolidBrush(Color.FromArgb(4, 10, 10));
        using var readoutEdge = new Pen(active ? C.Signal : C.Steel, 2);
        g.FillRectangle(readoutFill, readout);
        g.DrawRectangle(readoutEdge, readout.X, readout.Y, readout.Width, readout.Height);
        LabFont.Draw(g, value, readout.X + readout.Width / 2, readout.Y + 9, 1,
            active ? C.Signal : C.Sick, LabTextAlign.Center, 0);
        if (focused) DrawKeyboardFocusMarker(g, rect);
    }

    private void DrawRunToggleRow(Graphics g, RectangleF rect, string label, bool enabled,
        bool focused, bool hovered)
    {
        var active = focused || hovered;
        DrawCutPanel(g, rect,
            focused ? Color.FromArgb(38, 49, 43) : Color.FromArgb(19, 27, 26),
            active ? C.Signal : C.Steel, 9, focused ? 4 : 2);
        LabFont.Draw(g, label, rect.X + 22, rect.Y + rect.Height / 2 - 7, 1,
            active ? C.Bone : C.Sick);
        var switchPadding = rect.Height < 55 ? 8 : 15;
        var switchRect = new RectangleF(
            rect.Right - 130, rect.Y + switchPadding, 103,
            rect.Height - switchPadding * 2);
        using var switchFill = new SolidBrush(enabled ? Color.FromArgb(67, 67, 45) : Color.FromArgb(4, 10, 10));
        using var switchEdge = new Pen(active ? C.Signal : C.Steel, 2);
        g.FillRectangle(switchFill, switchRect);
        g.DrawRectangle(switchEdge, switchRect.X, switchRect.Y, switchRect.Width, switchRect.Height);
        using var lamp = new SolidBrush(enabled ? C.Signal : C.Oxide);
        g.FillRectangle(lamp, enabled ? switchRect.Right - 31 : switchRect.X + 9,
            switchRect.Y + 8, 22, switchRect.Height - 16);
        LabFont.Draw(g, enabled ? "ON" : "OFF", enabled ? switchRect.X + 12 : switchRect.Right - 12,
            switchRect.Y + switchRect.Height / 2 - 5, 1, enabled ? C.Bone : C.Steel,
            enabled ? LabTextAlign.Left : LabTextAlign.Right, 0);
        if (focused) DrawKeyboardFocusMarker(g, rect);
    }

    private static void DrawRunStepper(Graphics g, RectangleF rect, string label, bool active)
    {
        using var fill = new SolidBrush(active ? C.Bone : C.Steel);
        using var recess = new SolidBrush(Color.Black);
        g.FillRectangle(recess, RectangleF.Inflate(rect, 3, 3));
        g.FillRectangle(fill, rect);
        LabFont.Draw(g, label, rect.X + rect.Width / 2, rect.Y + 8, 1, C.Ink,
            LabTextAlign.Center, 0);
    }
}
