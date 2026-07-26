namespace Dust;

internal sealed partial class GameForm
{
    private const int ProgressionVisibleRows = 7;

    private readonly RectangleF[] _progressionTabButtons = new RectangleF[2];
    private readonly RectangleF[] _progressionRows = new RectangleF[ProgressionVisibleRows];
    private RectangleF _progressionToggleButton;
    private int _progressionTab;
    private int _achievementSelection;
    private int _perkSelection;
    private int _hoverProgressionTab = -1;
    private int _hoverProgressionRow = -1;
    private bool _hoverProgressionToggle;
    private string _progressionNotice = string.Empty;
    private float _progressionNoticeTimer;

    private void OpenProgression()
    {
        _mode = ScreenMode.Achievements;
        _progressionTab = 0;
        _achievementSelection = Math.Clamp(_achievementSelection, 0,
            Math.Max(0, ProgressionCatalog.Achievements.Length - 1));
        _perkSelection = Math.Clamp(_perkSelection, 0, Math.Max(0, ProgressionCatalog.Perks.Length - 1));
        _progressionNotice = string.Empty;
        _progressionNoticeTimer = 0;
        ResetHover();
    }

    private void DrawProgressionConsole(Graphics g)
    {
        var shell = new RectangleF(42, 54, DesignWidth - 84, DesignHeight - 108);
        DrawMenuConsoleShell(g, shell, "BEHAVIORAL ARCHIVE / CLEARANCE MATRIX");

        var unlocked = ProgressionCatalog.Achievements.Count(definition =>
            _settings.IsAchievementUnlocked(definition.Id));
        LabFont.Draw(g, "ACHIEVEMENTS", 72, 74, 3, C.Bone);
        LabFont.Draw(g, $"CLEARED {unlocked:00}/{ProgressionCatalog.Achievements.Length:00}",
            312, 83, 1, unlocked == ProgressionCatalog.Achievements.Length ? C.Signal : C.Sick);

        var tabNames = new[] { "ACHIEVEMENT CARDS", "PERK SOCKETS" };
        for (var index = 0; index < _progressionTabButtons.Length; index++)
        {
            var rect = new RectangleF(72 + index * 328, 118, 310, 48);
            _progressionTabButtons[index] = rect;
            var active = _progressionTab == index;
            var hovered = _hoverProgressionTab == index;
            DrawCutPanel(g, rect,
                active ? Color.FromArgb(52, 56, 44) : Color.FromArgb(17, 25, 24),
                active || hovered ? C.Signal : C.Steel, 8, active ? 4 : 2);
            LabFont.Draw(g, tabNames[index], rect.X + 18, rect.Y + 16, 1,
                active || hovered ? C.Bone : C.Sick);
            using var lamp = new SolidBrush(active ? C.Signal : C.Deep);
            g.FillRectangle(lamp, rect.Right - 24, rect.Y + 13, 8, 22);
        }

        var archive = new RectangleF(72, 184, 654, 444);
        var dossier = new RectangleF(746, 184, 442, 444);
        DrawCutPanel(g, archive, Color.FromArgb(12, 19, 18), Color.FromArgb(72, 85, 72), 14, 3);
        DrawCutPanel(g, dossier, Color.FromArgb(19, 25, 22), Color.FromArgb(82, 89, 69), 14, 3);
        DrawPanelBolts(g, archive, C.Steel);
        DrawPanelBolts(g, dossier, C.Steel);

        if (_progressionTab == 0) DrawAchievementArchive(g, archive, dossier);
        else DrawPerkArchive(g, archive, dossier);

        _backButton = new RectangleF(72, 666, 188, 56);
        DrawAbortButton(g, _backButton, "BACK", _hoverBack);
        if (_progressionNoticeTimer > 0)
            LabFont.Draw(g, _progressionNotice, 1188, 690, 1,
                _progressionNotice.Contains("LOCKED", StringComparison.Ordinal) ? C.Oxide : C.Signal,
                LabTextAlign.Right);
    }

    private void DrawAchievementArchive(Graphics g, RectangleF archive, RectangleF dossier)
    {
        var definitions = ProgressionCatalog.Achievements;
        var selection = Math.Clamp(_achievementSelection, 0, Math.Max(0, definitions.Length - 1));
        var start = ProgressionWindowStart(selection, definitions.Length);
        for (var row = 0; row < ProgressionVisibleRows; row++)
        {
            var rect = new RectangleF(archive.X + 18, archive.Y + 18 + row * 57, archive.Width - 36, 50);
            _progressionRows[row] = rect;
            var index = start + row;
            if (index >= definitions.Length) continue;
            var definition = definitions[index];
            var state = _settings.GetAchievementState(definition.Id);
            DrawAchievementCard(g, rect, definition, state, index == selection,
                _hoverProgressionRow == row, index);
        }

        if (definitions.Length == 0) return;
        var selected = definitions[selection];
        var selectedState = _settings.GetAchievementState(selected.Id);
        DrawArchiveDossier(g, dossier, selected, selectedState);
    }

    private void DrawAchievementCard(Graphics g, RectangleF rect, AchievementDefinition definition,
        AchievementProgressSnapshot state, bool focused, bool hovered, int index)
    {
        var rankColor = ProgressionRankColor(definition.Rank);
        DrawCutPanel(g, rect,
            state.IsUnlocked ? Color.FromArgb(39, 48, 39) : Color.FromArgb(8, 14, 14),
            focused || hovered ? C.Signal : state.IsUnlocked ? C.Sick : C.Steel, 7, focused ? 3 : 2);
        using var punch = new SolidBrush(state.IsUnlocked ? rankColor : C.Deep);
        g.FillRectangle(punch, rect.X + 10, rect.Y + 9, 8, 8);
        g.FillRectangle(punch, rect.X + 10, rect.Bottom - 17, 8, 8);
        LabFont.Draw(g, $"{index + 1:00}", rect.X + 29, rect.Y + 17, 1, C.Steel);
        LabFont.Draw(g, definition.Name.ToUpperInvariant(), rect.X + 72, rect.Y + 11, 2,
            state.IsUnlocked ? C.Bone : C.Sick);
        LabFont.Draw(g, state.IsUnlocked ? "CLEARED" : ProgressionRankLabel(definition.Rank),
            rect.Right - 16, rect.Y + 18, 1, state.IsUnlocked ? C.Signal : rankColor,
            LabTextAlign.Right);
        if (focused) DrawKeyboardFocusMarker(g, rect);
    }

    private void DrawArchiveDossier(Graphics g, RectangleF rect, AchievementDefinition definition,
        AchievementProgressSnapshot state)
    {
        var paper = RectangleF.Inflate(rect, -22, -22);
        using var paperFill = new SolidBrush(Color.FromArgb(194, 183, 140));
        using var ink = new Pen(Color.FromArgb(82, 76, 59), 2);
        g.FillRectangle(paperFill, paper);
        for (var y = paper.Y + 42; y < paper.Bottom - 18; y += 28)
            g.DrawLine(ink, paper.X + 14, y, paper.Right - 14, y);
        using var hole = new SolidBrush(C.Ink);
        for (var y = paper.Y + 13; y < paper.Bottom - 8; y += 28)
        {
            g.FillRectangle(hole, paper.X + 7, y, 7, 7);
            g.FillRectangle(hole, paper.Right - 14, y, 7, 7);
        }

        var textColor = C.Ink;
        LabFont.Draw(g, state.IsUnlocked ? "CLEARANCE GRANTED" : "CLEARANCE PENDING",
            paper.X + 26, paper.Y + 19, 1, state.IsUnlocked ? Color.FromArgb(43, 82, 66) : C.Oxide);
        LabFont.Draw(g, definition.Name.ToUpperInvariant(), paper.X + 26, paper.Y + 62, 2, textColor);
        LabFont.Draw(g, $"DIFFICULTY / {ProgressionRankLabel(definition.Rank)}", paper.X + 26,
            paper.Y + 103, 1, textColor);

        var lineY = paper.Y + 144;
        foreach (var line in WrapProgressionText(definition.Description, 31))
        {
            LabFont.Draw(g, line, paper.X + 26, lineY, 1, textColor);
            lineY += 27;
        }

        var rewardDefinitions = ProgressionCatalog.Perks
            .Where(perk => perk.RequiredAchievements.Contains(definition.Id))
            .ToArray();
        var rewards = rewardDefinitions
            .Select(perk => perk.AdditionalRequiredAchievement.HasValue
                ? $"{perk.Name.ToUpperInvariant()} / JOINT KEY"
                : perk.Name.ToUpperInvariant())
            .ToArray();

        // Keep rewards in their own footer block. Achievements can grant more than one
        // perk, so sharing the progress baseline caused the two labels to collide.
        var statusTop = state.IsUnlocked && state.UnlockedAtUtc.HasValue
            ? paper.Bottom - 57
            : paper.Bottom - 35;
        if (rewards.Length > 0)
        {
            const float rewardLineSpacing = 24;
            var firstRewardY = statusTop - 34 - (rewards.Length - 1) * rewardLineSpacing;
            var joint = rewardDefinitions.Any(perk => perk.AdditionalRequiredAchievement.HasValue);
            LabFont.Draw(g, joint ? "JOINT PERK CLEARANCE" :
                    rewards.Length == 1 ? "PERK CLEARANCE" : "PERK CLEARANCES",
                paper.X + 26, firstRewardY - 27, 1, Color.FromArgb(43, 82, 66));
            for (var index = 0; index < rewards.Length; index++)
                LabFont.Draw(g, $"+ {rewards[index]}", paper.X + 26,
                    firstRewardY + index * rewardLineSpacing, 1, Color.FromArgb(43, 82, 66));
        }

        if (state.IsUnlocked && state.UnlockedAtUtc.HasValue)
        {
            LabFont.Draw(g, "STAMPED", paper.X + 26, paper.Bottom - 57, 1, C.Oxide);
            LabFont.Draw(g, state.UnlockedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd"),
                paper.X + 26, paper.Bottom - 32, 1, textColor);
        }
        else
        {
            LabFont.Draw(g, AchievementProgressText(definition, state), paper.X + 26,
                paper.Bottom - 35, 1, C.Oxide);
        }
    }

    private void DrawPerkArchive(Graphics g, RectangleF archive, RectangleF dossier)
    {
        var definitions = ProgressionCatalog.Perks;
        var selection = Math.Clamp(_perkSelection, 0, Math.Max(0, definitions.Length - 1));
        var start = ProgressionWindowStart(selection, definitions.Length);
        for (var row = 0; row < ProgressionVisibleRows; row++)
        {
            var rect = new RectangleF(archive.X + 18, archive.Y + 18 + row * 57, archive.Width - 36, 50);
            _progressionRows[row] = rect;
            var index = start + row;
            if (index >= definitions.Length) continue;
            var definition = definitions[index];
            var available = definition.RequirementsMet(_settings.IsAchievementUnlocked);
            var equipped = _settings.HasEquippedPerk(definition.Id);
            DrawPerkSocket(g, rect, definition, available, equipped, index == selection,
                _hoverProgressionRow == row, index);
        }

        if (definitions.Length == 0) return;
        DrawPerkDossier(g, dossier, definitions[selection]);
    }

    private void DrawPerkSocket(Graphics g, RectangleF rect, PerkDefinition definition,
        bool available, bool equipped, bool focused, bool hovered, int index)
    {
        DrawCutPanel(g, rect,
            equipped ? Color.FromArgb(49, 53, 39) : Color.FromArgb(9, 15, 15),
            focused || hovered ? C.Signal : equipped ? C.Sick : C.Steel, 7, focused ? 3 : 2);
        var iconSocket = new RectangleF(rect.X + 9, rect.Y + 7, 36, 36);
        DrawCutPanel(g, iconSocket, Color.FromArgb(226, C.Ink),
            equipped ? C.Signal : available ? C.Sick : C.Steel, 5, equipped ? 2 : 1);
        DrawPerkGlyph(g, definition.Id, RectangleF.Inflate(iconSocket, -6, -6),
            equipped ? C.Signal : available ? C.Bone : C.Steel);
        LabFont.Draw(g, $"P{index + 1:00}", rect.X + 53, rect.Y + 18, 1, C.Steel);
        LabFont.Draw(g, definition.Name.ToUpperInvariant(), rect.X + 92, rect.Y + 11, 2,
            available ? C.Bone : C.Steel);
        LabFont.Draw(g, equipped ? "EQUIPPED" : available ? "AVAILABLE" : "LOCKED",
            rect.Right - 16, rect.Y + 18, 1,
            equipped ? C.Signal : available ? C.Sick : C.Oxide, LabTextAlign.Right);
        if (focused) DrawKeyboardFocusMarker(g, rect);
    }

    private void DrawPerkDossier(Graphics g, RectangleF rect, PerkDefinition definition)
    {
        var available = definition.RequirementsMet(_settings.IsAchievementUnlocked);
        var equipped = _settings.HasEquippedPerk(definition.Id);
        var requirements = definition.RequiredAchievements
            .Select(ProgressionCatalog.GetAchievement)
            .ToArray();
        LabFont.Draw(g, "SUBJECT MODIFICATION", rect.X + 27, rect.Y + 31, 1, C.Steel);
        LabFont.Draw(g, definition.Name.ToUpperInvariant(), rect.X + 27, rect.Y + 70, 3,
            available ? C.Bone : C.Steel);
        LabFont.Draw(g, definition.Activation == PerkActivation.Space ? "CHANNEL / ACTIVE" : "CHANNEL / PASSIVE",
            rect.X + 27, rect.Y + 120, 1, definition.Activation == PerkActivation.Space ? C.Signal : C.Sick);

        var lineY = rect.Y + 165;
        foreach (var line in WrapProgressionText(definition.Description, 29))
        {
            LabFont.Draw(g, line, rect.X + 27, lineY, 1, C.Sick);
            lineY += 27;
        }

        var requirementsY = requirements.Length > 1 ? rect.Y + 260 : rect.Y + 286;
        LabFont.Draw(g, requirements.Length == 1 ? "REQUIRES" : "REQUIRES BOTH",
            rect.X + 27, requirementsY, 1, C.Steel);
        for (var index = 0; index < requirements.Length; index++)
        {
            var cleared = _settings.IsAchievementUnlocked(requirements[index].Id);
            LabFont.Draw(g, requirements[index].Name.ToUpperInvariant(), rect.X + 27,
                requirementsY + 29 + index * 27, index == 0 && requirements.Length == 1 ? 2 : 1,
                cleared ? C.Signal : C.Oxide);
        }
        LabFont.Draw(g, definition.Activation == PerkActivation.Space
                ? "ONE ACTIVE CHANNEL MAY BE FITTED"
                : "ONE PASSIVE CHANNEL MAY BE FITTED",
            rect.X + 27, rect.Y + 354, 1, C.Oxide);

        _progressionToggleButton = new RectangleF(rect.X + 27, rect.Bottom - 61, rect.Width - 54, 42);
        DrawCutPanel(g, _progressionToggleButton,
            available ? Color.FromArgb(35, 42, 34) : Color.FromArgb(18, 22, 21),
            _hoverProgressionToggle ? C.Signal : available ? C.Sick : C.Steel, 8, 3);
        var action = !available ? "SOCKET LOCKED" : equipped ? "REMOVE PERK" : "EQUIP PERK";
        LabFont.Draw(g, action, _progressionToggleButton.X + 20,
            _progressionToggleButton.Y + 14, 1,
            available ? (_hoverProgressionToggle ? C.Signal : C.Bone) : C.Steel);
    }

    private void HandleProgressionKey(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            _audio.Play(AudioCue.Confirm);
            EnterTitle();
        }
        else if (e.KeyCode is Keys.A or Keys.Left)
            MoveProgressionTab(-1);
        else if (e.KeyCode is Keys.D or Keys.Right or Keys.Tab)
            MoveProgressionTab(1);
        else if (e.KeyCode is Keys.W or Keys.Up)
            MoveProgressionSelection(-1);
        else if (e.KeyCode is Keys.S or Keys.Down)
            MoveProgressionSelection(1);
        else if (e.KeyCode is Keys.Enter or Keys.Space && _progressionTab == 1)
            ToggleSelectedPerk();
        else return;
        ConsumeKey(e);
    }

    private void MoveProgressionTab(int direction)
    {
        _progressionTab = Wrap(_progressionTab + direction, 2);
        _audio.Play(AudioCue.Select);
        ResetHover();
    }

    private void MoveProgressionSelection(int direction)
    {
        if (_progressionTab == 0)
            _achievementSelection = Wrap(_achievementSelection + direction, ProgressionCatalog.Achievements.Length);
        else
            _perkSelection = Wrap(_perkSelection + direction, ProgressionCatalog.Perks.Length);
        _audio.Play(AudioCue.Select);
    }

    private void ToggleSelectedPerk()
    {
        if (ProgressionCatalog.Perks.Length == 0) return;
        var definition = ProgressionCatalog.Perks[Math.Clamp(_perkSelection, 0,
            ProgressionCatalog.Perks.Length - 1)];
        if (_settings.HasEquippedPerk(definition.Id))
        {
            _settings.UnequipPerk(definition.Id);
            _progressionNotice = $"{definition.Name.ToUpperInvariant()} REMOVED";
            _audio.Play(AudioCue.Confirm);
        }
        else
        {
            var result = _settings.EquipPerk(definition.Id);
            if (result == PerkEquipResult.RequiredAchievementLocked)
            {
                _progressionNotice = "SOCKET LOCKED / CLEAR ALL REQUIREMENTS";
                _audio.Play(AudioCue.Select);
                _progressionNoticeTimer = 2.5f;
                return;
            }
            _progressionNotice = result == PerkEquipResult.Equipped
                ? $"{definition.Name.ToUpperInvariant()} EQUIPPED"
                : "SOCKET STATE UNCHANGED";
            _audio.Play(result == PerkEquipResult.Equipped ? AudioCue.Confirm : AudioCue.Select);
        }
        _progressionNoticeTimer = 2.5f;
        SaveSettings();
    }

    private int ProgressionWindowStart(int selection, int count) =>
        Math.Clamp(selection - ProgressionVisibleRows / 2, 0, Math.Max(0, count - ProgressionVisibleRows));

    private int ProgressionIndexForRow(int row)
    {
        var count = _progressionTab == 0 ? ProgressionCatalog.Achievements.Length : ProgressionCatalog.Perks.Length;
        var selection = _progressionTab == 0 ? _achievementSelection : _perkSelection;
        var index = ProgressionWindowStart(selection, count) + row;
        return index < count ? index : -1;
    }

    private static string ProgressionRankLabel(AchievementRank rank) => rank switch
    {
        AchievementRank.Easy => "RANK I / EASY",
        AchievementRank.Moderate => "RANK II / MODERATE",
        AchievementRank.Hard => "RANK III / HARD",
        _ => "RANK IV / EXTREME"
    };

    private static Color ProgressionRankColor(AchievementRank rank) => rank switch
    {
        AchievementRank.Easy => C.Sick,
        AchievementRank.Moderate => C.Signal,
        AchievementRank.Hard => C.Oxide,
        _ => C.Red
    };

    private string AchievementProgressText(AchievementDefinition definition, AchievementProgressSnapshot state)
    {
        if (definition.ProgressUnit == AchievementProgressUnit.ConsecutiveWins)
            return $"CURRENT {_settings.CurrentWinStreak:00} / TARGET {definition.Target:00}";
        if (definition.ProgressUnit == AchievementProgressUnit.Seconds && state.Progress > 0)
            return $"OBSERVED {state.Progress:000} / {definition.Target:000} SEC";
        if (definition.ProgressUnit == AchievementProgressUnit.Percent && state.Progress > 0)
            return $"MAPPED {state.Progress:000}%";
        return "CONDITION NOT YET OBSERVED";
    }

    private static IEnumerable<string> WrapProgressionText(string text, int maximumCharacters)
    {
        var words = text.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = string.Empty;
        foreach (var word in words)
        {
            if (line.Length == 0)
            {
                line = word;
                continue;
            }
            if (line.Length + 1 + word.Length <= maximumCharacters)
            {
                line += " " + word;
                continue;
            }
            yield return line;
            line = word;
        }
        if (line.Length > 0) yield return line;
    }

    private void DrawAchievementToast(Graphics g)
    {
        if (!_achievementToast.HasValue || _missionDossierOpen) return;
        var definition = ProgressionCatalog.GetAchievement(_achievementToast.Value);
        var slide = _achievementToastTimer > 3.9f
            ? 1 - (_achievementToastTimer - 3.9f) / .3f
            : _achievementToastTimer < .35f ? _achievementToastTimer / .35f : 1;
        slide = Math.Clamp(slide, 0, 1);
        var rect = new RectangleF(839 + (1 - slide) * 405, 64, 385, 88);
        using var shadow = new SolidBrush(Color.FromArgb(180, Color.Black));
        using var paper = new SolidBrush(Color.FromArgb(207, 194, 147));
        g.FillRectangle(shadow, rect.X + 7, rect.Y + 8, rect.Width, rect.Height);
        g.FillRectangle(paper, rect);
        using var ink = new SolidBrush(C.Ink);
        for (var y = rect.Y + 11; y < rect.Bottom - 7; y += 18)
            g.FillRectangle(ink, rect.X + 8, y, 6, 6);
        LabFont.Draw(g, "ACHIEVEMENT CLEARED", rect.X + 28, rect.Y + 15, 1, C.Oxide);
        LabFont.Draw(g, definition.Name.ToUpperInvariant(), rect.X + 28, rect.Y + 45, 2, C.Ink);
        LabFont.Draw(g, ProgressionRankLabel(definition.Rank), rect.Right - 18, rect.Bottom - 17,
            1, C.Ink, LabTextAlign.Right);
    }
}
