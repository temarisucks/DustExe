namespace Dust;

internal sealed partial class GameForm
{
    private readonly List<RoomProp> _roomProps = [];
    private readonly List<RoomSalvage> _roomSalvage = [];
    private readonly List<ShopStockItem> _shopStock = [];
    private readonly RectangleF[] _shopCommandButtons = new RectangleF[4];
    private readonly RectangleF[] _shopListRows = new RectangleF[6];
    private ShopKiosk? _shopKiosk;
    private ShopPage _shopPage;
    private int _shopCommandSelection;
    private int _shopListSelection;
    private int _hoverShopCommand = -1;
    private int _hoverShopRow = -1;
    private DateTime _shopEnteredAt;
    private string _shopDialogue = string.Empty;
    private int _shopDialogueVisible;
    private float _shopDialogueAccumulator;
    private int _shopProtectionCharges;
    private int _shopRepairReserve;

    private bool ShopDialogueReady => _shopDialogueVisible >= _shopDialogue.Length;
    private long AvailableShopCredits => _settings.TotalCredits > long.MaxValue - _fieldCredits
        ? long.MaxValue
        : _settings.TotalCredits + _fieldCredits;

    private void SetupCargoRoomContents()
    {
        _roomProps.Clear();
        _roomSalvage.Clear();
        _shopStock.Clear();
        _shopKiosk = null;
        _shopPage = ShopPage.Commands;
        _shopCommandSelection = 0;
        _shopListSelection = 0;
        if (_maze is null || _maze.Rooms.Count == 0) return;

        // Every cassette has one mobile reclamation kiosk. Its room and cell are
        // derived from the plate number so room dressing is stable for this maze.
        var shopRoom = _maze.Rooms[PositiveHash(_level * 43 + _maze.Width * 7) % _maze.Rooms.Count];
        var occupied = _cargoItems.Select(item => item.Cell)
            .Concat(_creditPickups.Select(item => item.Cell))
            .Concat(_circuitSwitches.Select(item => item.Cell))
            .ToHashSet();
        var kioskCandidates = shopRoom.Cells
            .Where(cell => cell != shopRoom.DoorCell && !occupied.Contains(cell))
            .OrderByDescending(cell => IsRoomPerimeterCell(shopRoom, cell))
            .ThenByDescending(cell => Manhattan(cell, shopRoom.DoorCell))
            .ThenBy(cell => PositiveHash(cell.X * 73856093 ^ cell.Y * 19349663 ^ _level))
            .ToList();
        var kioskCell = kioskCandidates
            .Where(cell => occupied.All(other => Manhattan(cell, other) > 1))
            .Cast<Point?>()
            .FirstOrDefault() ??
            kioskCandidates.Cast<Point?>().FirstOrDefault();
        if (kioskCell is { } selectedKioskCell)
        {
            _shopKiosk = new ShopKiosk { RoomId = shopRoom.Id, Cell = selectedKioskCell };
            occupied.Add(selectedKioskCell);
            foreach (var apronCell in shopRoom.Cells.Where(
                         cell => Manhattan(cell, selectedKioskCell) == 1))
                occupied.Add(apronCell);
        }

        foreach (var room in _maze.Rooms)
        {
            var candidates = room.Cells
                .Where(cell => cell != room.DoorCell && !occupied.Contains(cell) &&
                               Manhattan(cell, room.DoorCell) > 1 && IsRoomPerimeterCell(room, cell))
                .OrderBy(cell => PositiveHash(cell.X * 92821 ^ cell.Y * 68917 ^ room.Id * 1327 ^ _level * 31))
                .ToList();
            var propCount = Math.Clamp(room.Cells.Count / 7, 3, 6);
            for (var index = 0; index < Math.Min(propCount, candidates.Count); index++)
            {
                var cell = candidates[index];
                var hash = PositiveHash(cell.X * 31337 ^ cell.Y * 7919 ^ room.Id * 101);
                _roomProps.Add(new RoomProp
                {
                    RoomId = room.Id,
                    Cell = cell,
                    Kind = (RoomPropKind)(hash % Enum.GetValues<RoomPropKind>().Length),
                    Variant = (hash / 17) % 4
                });
                occupied.Add(cell);
            }

            // Salvage is small, non-blocking, and collected by crossing its tile.
            // It becomes actual inventory rather than immediately turning into cash.
            var salvageCount = 1 + ((room.Id + _level) & 1);
            var salvageCandidates = room.Cells
                .Where(cell => cell != room.DoorCell && !occupied.Contains(cell))
                .OrderBy(cell => PositiveHash(cell.X * 19937 ^ cell.Y * 44497 ^ room.Id * 353))
                .Take(salvageCount)
                .ToList();
            foreach (var cell in salvageCandidates)
            {
                var hash = PositiveHash(cell.X * 541 ^ cell.Y * 1297 ^ room.Id * 4051 ^ _level);
                var kind = (SalvageKind)(hash % Enum.GetValues<SalvageKind>().Length);
                _roomSalvage.Add(new RoomSalvage
                {
                    RoomId = room.Id,
                    Cell = cell,
                    Kind = kind,
                    Value = 24 + (hash % 5) * 9
                });
                occupied.Add(cell);
            }
        }

        _shopStock.Add(new ShopStockItem
        {
            Kind = ShopItemKind.FramePatch,
            Name = "FRAME PATCH",
            Description = "Repairs 1 damage; excess banks for a later hit.",
            Price = 90,
            StartingStock = 2,
            Stock = 2
        });
        _shopStock.Add(new ShopStockItem
        {
            Kind = ShopItemKind.ReconstructionGel,
            Name = "RECON GEL",
            Description = "Repairs 2 damage; excess banks for later hits.",
            Price = 165,
            StartingStock = 1,
            Stock = 1
        });
        _shopStock.Add(new ShopStockItem
        {
            Kind = ShopItemKind.AegisFuse,
            Name = "AEGIS FUSE",
            Description = "A one-use ward that cancels the next damaging hit.",
            Price = 145,
            StartingStock = 2,
            Stock = 2
        });
    }

    private static int PositiveHash(int value) => value == int.MinValue ? int.MaxValue : Math.Abs(value);

    private static bool IsRoomPerimeterCell(CargoRoom room, Point cell) =>
        !room.Contains(new Point(cell.X, cell.Y - 1)) ||
        !room.Contains(new Point(cell.X + 1, cell.Y)) ||
        !room.Contains(new Point(cell.X, cell.Y + 1)) ||
        !room.Contains(new Point(cell.X - 1, cell.Y));

    private bool TryOpenShopAtPlayer()
    {
        if (_mode != ScreenMode.Playing || _shopKiosk is null ||
            !IsShopKioskInRange(_playerCell) || _moveProgress < 1 || _hitEffect > 0) return false;
        CloseMissionDossier(playSound: false);
        ResetMissionDossier();
        _shopEnteredAt = DateTime.Now;
        _shopPage = ShopPage.Commands;
        _shopCommandSelection = 0;
        _shopListSelection = 0;
        _mode = ScreenMode.Shop;
        StartShopDialogue("There you are, little drone.\nSet your cargo down. Nothing hunts inside my counter-light.");
        _audio.Play(AudioCue.Confirm);
        ResetHover();
        return true;
    }

    private bool IsShopKioskInRange(Point playerCell) =>
        _shopKiosk is not null &&
        CanInteractWithMissionCell(playerCell, _shopKiosk.Cell);

    private void LeaveShop()
    {
        if (_mode != ScreenMode.Shop) return;
        var paused = DateTime.Now - _shopEnteredAt;
        if (!IsOnlineGameplayActive) _startedAt += paused;
        // Hit-window achievements use wall-clock timestamps. Shift previous
        // hits by the same safe-room pause so their timing matches the mission clock.
        if (!IsOnlineGameplayActive)
            for (var index = 0; index < _runHitTimes.Count; index++)
                _runHitTimes[index] = _runHitTimes[index].Add(paused);
        RelayOnlineShopLeave();
        _mode = ScreenMode.Playing;
        _shopPage = ShopPage.Commands;
        _shopDialogue = string.Empty;
        _shopDialogueVisible = 0;
        ResetHover();
        _audio.Play(AudioCue.Confirm);
    }

    private void StartShopDialogue(string text)
    {
        _shopDialogue = text;
        _shopDialogueVisible = 0;
        _shopDialogueAccumulator = 0;
    }

    private void UpdateShop(float deltaTime)
    {
        if (_mode != ScreenMode.Shop || ShopDialogueReady) return;
        _shopDialogueAccumulator += deltaTime * 34f;
        var characters = Math.Min(_shopDialogue.Length - _shopDialogueVisible,
            (int)_shopDialogueAccumulator);
        if (characters <= 0) return;
        _shopDialogueAccumulator -= characters;
        for (var index = 0; index < characters; index++)
        {
            _shopDialogueVisible++;
            _audio.Play(AudioCue.ShopVoice);
        }
    }

    private void HandleShopKey(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            if (_shopPage == ShopPage.Commands) LeaveShop();
            else ReturnToShopCommands();
            ConsumeKey(e);
            return;
        }
        if (!ShopDialogueReady)
        {
            ConsumeKey(e);
            return;
        }

        if (_shopPage == ShopPage.Commands)
        {
            if (e.KeyCode is Keys.A or Keys.Left or Keys.W or Keys.Up)
                MoveShopCommand(-1);
            else if (e.KeyCode is Keys.D or Keys.Right or Keys.S or Keys.Down)
                MoveShopCommand(1);
            else if (e.KeyCode is Keys.Enter or Keys.Space or Keys.E)
                ActivateShopCommand();
            else return;
        }
        else
        {
            if (e.KeyCode is Keys.W or Keys.Up)
                MoveShopList(-1);
            else if (e.KeyCode is Keys.S or Keys.Down)
                MoveShopList(1);
            else if (e.KeyCode is Keys.Enter or Keys.Space or Keys.E)
                ActivateShopListSelection();
            else return;
        }
        ConsumeKey(e);
    }

    private void MoveShopCommand(int delta)
    {
        _shopCommandSelection = Wrap(_shopCommandSelection + delta, 4);
        _audio.Play(AudioCue.Select);
    }

    private void MoveShopList(int delta)
    {
        var count = ShopListCount();
        if (count <= 0) return;
        _shopListSelection = Wrap(_shopListSelection + delta, count);
        _audio.Play(AudioCue.Select);
    }

    private int ShopListCount() => _shopPage switch
    {
        ShopPage.Buy => _shopStock.Count,
        ShopPage.Sell => SellInventory().Count,
        ShopPage.Talk => ShopTopics.Length,
        _ => 0
    };

    private void ActivateShopCommand()
    {
        _audio.Play(AudioCue.Confirm);
        switch (_shopCommandSelection)
        {
            case 0:
                _shopPage = ShopPage.Buy;
                _shopListSelection = 0;
                StartShopDialogue("Repairs, wards, little mercies. Limited stock. Choose carefully.");
                break;
            case 1:
                _shopPage = ShopPage.Sell;
                _shopListSelection = 0;
                StartShopDialogue(SellInventory().Count > 0
                    ? "You dragged useful pieces through the dark. I will take them."
                    : "Your salvage rack is empty. The floor keeps what you ignored.");
                break;
            case 2:
                _shopPage = ShopPage.Talk;
                _shopListSelection = 0;
                StartShopDialogue("Ask. The walls already heard you thinking.");
                break;
            default:
                LeaveShop();
                break;
        }
        ResetHover();
    }

    private void ActivateShopListSelection()
    {
        switch (_shopPage)
        {
            case ShopPage.Buy:
                BuySelectedShopItem();
                break;
            case ShopPage.Sell:
                SellSelectedSalvage();
                break;
            case ShopPage.Talk:
                TalkAboutSelectedTopic();
                break;
        }
    }

    private void ReturnToShopCommands()
    {
        _shopPage = ShopPage.Commands;
        _shopListSelection = 0;
        StartShopDialogue("Still here, drone. Buy, sell, ask, or go.");
        ResetHover();
        _audio.Play(AudioCue.Confirm);
    }

    private void BuySelectedShopItem()
    {
        if (IsOnlineGameplayActive && !IsOnlineSimulationHost)
        {
            if (_shopStock.Count == 0) return;
            RelayOnlineShopPurchase(
                Math.Clamp(_shopListSelection, 0, _shopStock.Count - 1));
            return;
        }
        if (_shopStock.Count == 0) return;
        var item = _shopStock[Math.Clamp(_shopListSelection, 0, _shopStock.Count - 1)];
        if (item.Stock <= 0)
        {
            StartShopDialogue("Empty hook. I told you the stock was finite.");
            _audio.Play(AudioCue.Select);
            return;
        }
        if (!TrySpendShopCredits(item.Price))
        {
            StartShopDialogue("Your account is lighter than your request.");
            _audio.Play(AudioCue.Select);
            return;
        }

        item.Stock--;
        switch (item.Kind)
        {
            case ShopItemKind.FramePatch:
            {
                var repair = ApplyPurchasedRepair(1);
                StartShopDialogue(repair.Applied > 0
                    ? "One fracture closed. Try not to make it sentimental."
                    : "Patch banked. It will wake after the next fracture.");
                break;
            }
            case ShopItemKind.ReconstructionGel:
            {
                var repair = ApplyPurchasedRepair(2);
                StartShopDialogue(repair.Applied == 0
                    ? "Gel banked. Two fractures may borrow its memory."
                    : repair.Banked > 0
                        ? "One fracture forgotten. The remaining gel is banked."
                        : "The gel remembers the shape your frame forgot.");
                break;
            }
            case ShopItemKind.AegisFuse:
                _shopProtectionCharges++;
                StartShopDialogue("Ward armed. The next hit belongs to the fuse, not you.");
                break;
        }
        SaveSettings();
        _audio.Play(AudioCue.Confirm);
    }

    private bool TrySpendShopCredits(int price)
    {
        if (price <= 0) return true;
        if (AvailableShopCredits < price) return false;

        // Credits recovered on this plate are real currency at the kiosk. Spend
        // them first so the same credits are not also paid out on the report.
        var fieldSpend = Math.Min(_fieldCredits, price);
        _fieldCredits -= fieldSpend;
        _settings.TotalCredits -= price - fieldSpend;
        return true;
    }

    private (int Applied, int Banked) ApplyPurchasedRepair(int repairPoints)
    {
        repairPoints = Math.Max(0, repairPoints);
        var applied = Math.Min(_damageTaken, repairPoints);
        var banked = repairPoints - applied;
        _damageTaken -= applied;
        _shopRepairReserve += banked;
        return (applied, banked);
    }

    private bool TryConsumeShopRepairReserve()
    {
        if (_shopRepairReserve <= 0 || _damageTaken <= 0) return false;
        _shopRepairReserve--;
        _damageTaken--;
        _missionNotice = "BANKED REPAIR DEPLOYED / FRAME RESTORED";
        _missionNoticeTimer = 2.2f;
        return true;
    }

    private List<(SalvageKind Kind, int Count, int Value)> SellInventory() => _roomSalvage
        .Where(item => item.Collected && !item.Sold)
        .GroupBy(item => item.Kind)
        .Select(group => (group.Key, group.Count(), group.Sum(item => item.Value)))
        .OrderBy(group => (int)group.Key)
        .ToList();

    private void SellSelectedSalvage()
    {
        var inventory = SellInventory();
        if (inventory.Count == 0)
        {
            StartShopDialogue("Bring me something the facility no longer deserves.");
            _audio.Play(AudioCue.Select);
            return;
        }
        var offer = inventory[Math.Clamp(_shopListSelection, 0, inventory.Count - 1)];
        if (IsOnlineGameplayActive && !IsOnlineSimulationHost)
        {
            RelayOnlineShopSale(offer.Kind);
            return;
        }
        foreach (var salvage in _roomSalvage.Where(item =>
                     item.Collected && !item.Sold && item.Kind == offer.Kind))
            salvage.Sold = true;
        _settings.AwardCredits(offer.Value);
        SaveSettings();
        _shopListSelection = Math.Min(_shopListSelection, Math.Max(0, SellInventory().Count - 1));
        StartShopDialogue($"{SalvageName(offer.Kind)}. {offer.Value:000} credits. Fair enough for something stolen twice.");
        _audio.Play(AudioCue.Collect);
    }

    private static readonly (string Topic, string Dialogue)[] ShopTopics =
    [
        ("YOU", "A silhouette is safer than a name. The eyes are enough for business."),
        ("THE FACILITY", "It calls this a test. Tests usually know what answer they want."),
        ("THE CARGO", "Your manifest is not the whole inventory. It never is, little drone."),
        ("THE HOLLOWS", "They cannot cross my counter-light. They remember being customers.")
    ];

    private void TalkAboutSelectedTopic()
    {
        var topic = ShopTopics[Math.Clamp(_shopListSelection, 0, ShopTopics.Length - 1)];
        StartShopDialogue(topic.Dialogue);
        _audio.Play(AudioCue.Confirm);
    }

    private void CollectRoomSalvageAt(Point cell)
    {
        var salvage = _roomSalvage.FirstOrDefault(item =>
            !item.Collected && !item.Sold && item.Cell == cell);
        if (salvage is null) return;
        salvage.Collected = true;
        _missionNotice = $"SALVAGE / {SalvageName(salvage.Kind)}";
        _missionNoticeTimer = 2.1f;
        _audio.Play(AudioCue.Collect);
    }

    private bool TryConsumeShopProtection()
    {
        if (_shopProtectionCharges <= 0) return false;
        _shopProtectionCharges--;
        _invulnerability = Math.Max(_invulnerability, 1.25f);
        _impactCell = new Point((int)MathF.Round(_visualCell.X), (int)MathF.Round(_visualCell.Y));
        _impactPulse = 1;
        _missionNotice = "AEGIS FUSE SPENT / DAMAGE NULL";
        _missionNoticeTimer = 2.2f;
        _audio.Play(AudioCue.Confirm);
        return true;
    }

    private static string SalvageName(SalvageKind kind) => kind switch
    {
        SalvageKind.CopperSpool => "COPPER SPOOL",
        SalvageKind.OpticShard => "OPTIC SHARD",
        SalvageKind.ServoClutch => "SERVO CLUTCH",
        _ => "MEMORY FOIL"
    };
}
