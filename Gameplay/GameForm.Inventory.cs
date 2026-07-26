namespace Dust;

internal sealed partial class GameForm
{
    private int _framePatchInventory;
    private int _reconstructionGelInventory;
    private bool _shopProtectionArmed;
    private bool _inventoryOpen;
    private int _inventorySelection;
    private DateTime _inventoryOpenedAt;
    private RectangleF _inventoryButton;
    private RectangleF _inventoryPanel;
    private RectangleF _inventoryCloseButton;
    private RectangleF _inventoryUseButton;
    private readonly RectangleF[] _inventoryRows = new RectangleF[3];
    private bool _hoverInventory;
    private bool _hoverInventoryClose;
    private bool _hoverInventoryUse;
    private int _hoverInventoryRow = -1;

    private bool HasActivePerkEquipped =>
        _settings.Progression.EquippedPerks.Any(id =>
            ProgressionCatalog.TryGetPerk(id, out var definition) &&
            definition.Activation == PerkActivation.Space);

    private int InventoryCount(ShopItemKind kind) => kind switch
    {
        ShopItemKind.FramePatch => _framePatchInventory,
        ShopItemKind.ReconstructionGel => _reconstructionGelInventory,
        ShopItemKind.AegisFuse => _shopProtectionCharges,
        _ => 0
    };

    private void AddInventoryItem(ShopItemKind kind)
    {
        switch (kind)
        {
            case ShopItemKind.FramePatch:
                _framePatchInventory = Math.Min(99, _framePatchInventory + 1);
                break;
            case ShopItemKind.ReconstructionGel:
                _reconstructionGelInventory = Math.Min(99, _reconstructionGelInventory + 1);
                break;
            case ShopItemKind.AegisFuse:
                _shopProtectionCharges = Math.Min(99, _shopProtectionCharges + 1);
                break;
        }
    }

    private void ResetInventoryRunState()
    {
        _framePatchInventory = 0;
        _reconstructionGelInventory = 0;
        _shopProtectionCharges = 0;
        _shopProtectionArmed = false;
        ResetInventoryOverlay();
    }

    private void OpenInventory()
    {
        if (_mode != ScreenMode.Playing || _inventoryOpen) return;
        CloseMissionDossier(playSound: false);
        _inventoryOpen = true;
        _inventoryOpenedAt = DateTime.Now;
        _inventorySelection = Math.Clamp(_inventorySelection, 0, 2);
        ResetHover();
        _audio.Play(AudioCue.Confirm);
    }

    private void CloseInventory(bool playSound = true)
    {
        if (!_inventoryOpen) return;
        if (!IsOnlineGameplayActive && _inventoryOpenedAt != default)
        {
            var paused = DateTime.Now - _inventoryOpenedAt;
            _startedAt += paused;
            for (var index = 0; index < _runHitTimes.Count; index++)
                _runHitTimes[index] = _runHitTimes[index].Add(paused);
        }

        _inventoryOpen = false;
        _inventoryOpenedAt = default;
        _inventoryPanel = RectangleF.Empty;
        _inventoryCloseButton = RectangleF.Empty;
        _inventoryUseButton = RectangleF.Empty;
        Array.Fill(_inventoryRows, RectangleF.Empty);
        ResetHover();
        if (playSound) _audio.Play(AudioCue.Confirm);
    }

    private void ResetInventoryOverlay()
    {
        _inventoryOpen = false;
        _inventorySelection = 0;
        _inventoryOpenedAt = default;
        _inventoryButton = RectangleF.Empty;
        _inventoryPanel = RectangleF.Empty;
        _inventoryCloseButton = RectangleF.Empty;
        _inventoryUseButton = RectangleF.Empty;
        Array.Fill(_inventoryRows, RectangleF.Empty);
        _hoverInventory = false;
        _hoverInventoryClose = false;
        _hoverInventoryUse = false;
        _hoverInventoryRow = -1;
    }

    private void HandleInventoryKey(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Escape or Keys.I)
            CloseInventory();
        else if (e.KeyCode is Keys.W or Keys.Up)
            MoveInventorySelection(-1);
        else if (e.KeyCode is Keys.S or Keys.Down)
            MoveInventorySelection(1);
        else if (e.KeyCode == Keys.J && HasActivePerkEquipped)
        {
            _inventorySelection = 2;
            TryActivateDefensiveItem();
        }
        else if (e.KeyCode is Keys.Enter or Keys.E ||
                 e.KeyCode == Keys.Space &&
                 (_inventorySelection != 2 || !HasActivePerkEquipped))
            UseSelectedInventoryItem();
        else return;
        ConsumeKey(e);
    }

    private void MoveInventorySelection(int delta)
    {
        _inventorySelection = Wrap(_inventorySelection + delta, _inventoryRows.Length);
        _audio.Play(AudioCue.Select);
    }

    private void UseSelectedInventoryItem()
    {
        var kind = _inventorySelection switch
        {
            0 => ShopItemKind.FramePatch,
            1 => ShopItemKind.ReconstructionGel,
            _ => ShopItemKind.AegisFuse
        };
        if (kind == ShopItemKind.AegisFuse)
            TryActivateDefensiveItem();
        else
            TryUseHealingItem(kind);
    }

    private bool TryUseHealingItem(ShopItemKind kind)
    {
        if (kind is not (ShopItemKind.FramePatch or ShopItemKind.ReconstructionGel))
            return false;
        if (_mode != ScreenMode.Playing || _hitEffect > 0 || _pendingWin ||
            IsOnlineGameplayActive && _onlineLocalDefeated)
            return true;
        if (InventoryCount(kind) <= 0)
        {
            SetPerkNotice("INVENTORY SLOT EMPTY");
            _audio.Play(AudioCue.Select);
            return true;
        }
        if (_damageTaken <= 0)
        {
            SetPerkNotice("FRAME INTEGRITY ALREADY FULL");
            _audio.Play(AudioCue.Select);
            return true;
        }
        if (RelayOnlineInventoryUse(kind)) return true;

        var restored = ApplyLocalHealingItem(kind);
        SetPerkNotice(restored > 0
            ? $"FRAME RESTORED / +{restored:00} INTEGRITY"
            : "FRAME INTEGRITY ALREADY FULL");
        _audio.Play(restored > 0 ? AudioCue.Confirm : AudioCue.Select);
        return true;
    }

    private int ApplyLocalHealingItem(ShopItemKind kind)
    {
        var available = InventoryCount(kind);
        if (available <= 0 || _damageTaken <= 0) return 0;
        var repair = kind == ShopItemKind.ReconstructionGel ? 2 : 1;
        if (kind == ShopItemKind.ReconstructionGel) _reconstructionGelInventory--;
        else _framePatchInventory--;
        var before = _damageTaken;
        _damageTaken = Math.Max(0, _damageTaken - repair);
        _impactCell = new Point(
            (int)MathF.Round(_visualCell.X),
            (int)MathF.Round(_visualCell.Y));
        _impactPulse = 1;
        return before - _damageTaken;
    }

    /// <summary>
    /// Arms a carried defensive fuse. Space owns this action when no active perk
    /// is fitted; otherwise the active perk retains Space and the fuse moves to J.
    /// </summary>
    private bool TryActivateDefensiveItem()
    {
        if (_mode != ScreenMode.Playing || _hitEffect > 0 || _pendingWin ||
            IsOnlineGameplayActive && _onlineLocalDefeated)
            return true;
        if (_shopProtectionArmed)
        {
            SetPerkNotice("AEGIS WARD ALREADY ARMED");
            _audio.Play(AudioCue.Select);
            return true;
        }
        if (_shopProtectionCharges <= 0)
        {
            SetPerkNotice("NO AEGIS FUSE IN INVENTORY");
            _audio.Play(AudioCue.Select);
            return true;
        }
        if (RelayOnlineDefensiveItemActivation()) return true;

        _shopProtectionCharges--;
        _shopProtectionArmed = true;
        SetPerkNotice("AEGIS WARD ARMED / NEXT HIT NULL");
        _audio.Play(AudioCue.Confirm);
        return true;
    }

    private void HandleInventoryMouseMove(PointF hit)
    {
        _hoverInventoryClose = _inventoryCloseButton.Contains(hit);
        _hoverInventoryUse = _inventoryUseButton.Contains(hit);
        for (var index = 0; index < _inventoryRows.Length; index++)
            if (_inventoryRows[index].Contains(hit)) _hoverInventoryRow = index;
    }

    private bool HandleInventoryMouseDown(PointF hit)
    {
        if (_inventoryCloseButton.Contains(hit))
        {
            CloseInventory();
            return true;
        }
        for (var index = 0; index < _inventoryRows.Length; index++)
        {
            if (!_inventoryRows[index].Contains(hit)) continue;
            _inventorySelection = index;
            _audio.Play(AudioCue.Select);
            return true;
        }
        if (!_inventoryUseButton.Contains(hit)) return false;
        UseSelectedInventoryItem();
        return true;
    }
}
