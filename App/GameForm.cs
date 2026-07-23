namespace Dust;

// Numeric values are persisted in settings.json. Keep existing assignments stable
// and append new airframes so older profiles continue to load the same model.
internal enum DroneModel
{
    Mite = 0,
    Kite = 1,
    Triad = 2,
    Cicada = 3,
    Cradle = 4
}
internal enum DronePaintPart { Core, Frame }
internal enum ScreenMode
{
    Title,
    RunSettings,
    Customize,
    Settings,
    Achievements,
    Loading,
    Playing,
    Shop,
    Won,
    Failed,
    OnlineAccount,
    LobbyBrowser,
    LobbyRoom
}

internal sealed partial class GameForm : Form
{
    private static readonly Direction[] AllDirections = [Direction.Up, Direction.Right, Direction.Down, Direction.Left];

    private const int DesignWidth = 1280;
    private const int DesignHeight = 800;
    private const int CanvasWidth = 640;
    private const int CanvasHeight = 400;

    private static class C
    {
        public static readonly Color Void = Color.FromArgb(6, 9, 9);
        public static readonly Color Ink = Color.FromArgb(14, 21, 21);
        public static readonly Color Deep = Color.FromArgb(22, 31, 29);
        public static readonly Color Slab = Color.FromArgb(39, 50, 45);
        public static readonly Color Steel = Color.FromArgb(82, 94, 79);
        public static readonly Color Sick = Color.FromArgb(146, 157, 123);
        public static readonly Color Bone = Color.FromArgb(211, 208, 166);
        public static readonly Color Oxide = Color.FromArgb(158, 68, 48);
        public static readonly Color Signal = Color.FromArgb(220, 151, 72);
        public static readonly Color Red = Color.FromArgb(177, 48, 43);
    }

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Bitmap _canvas = new(CanvasWidth, CanvasHeight, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
    private DustRandom _random = new();
    private readonly GameSettings _settings;
    private readonly AudioManager _audio;
    private readonly LabCursor _labCursor;
    private readonly LabCursor _labActionCursor;
    private readonly HashSet<Point> _visited = [];
    private readonly List<Hollow> _hollows = [];
    private readonly Color[] _palette =
    [
        Color.FromArgb(119, 197, 152), // phosphor
        Color.FromArgb(188, 72, 57),   // arterial
        Color.FromArgb(211, 146, 62),  // iodine
        Color.FromArgb(79, 139, 178),  // coolant
        Color.FromArgb(128, 91, 145),  // bruised
        Color.FromArgb(181, 184, 151), // ceramic
        Color.FromArgb(220, 194, 85),  // hazard
        Color.FromArgb(89, 188, 190),  // sterile
        Color.FromArgb(177, 100, 64),  // rust
        Color.FromArgb(103, 142, 89),  // moss
        Color.FromArgb(194, 107, 137), // rose
        Color.FromArgb(89, 101, 100)   // graphite
    ];

    private ScreenMode _mode = ScreenMode.Title;
    private DroneModel _drone = DroneModel.Mite;
    private DronePaintPart _paintPart = DronePaintPart.Core;
    private Color _playerColor = Color.FromArgb(119, 197, 152);
    private Color _playerFrameColor = Color.FromArgb(181, 184, 151);
    private Maze? _maze;
    private Point _playerCell;
    private Point _exitCell;
    private PointF _visualCell;
    private PointF _previousVisualCell;
    private PointF _moveFrom;
    private PointF _moveTo;
    private PointF _cameraCell;
    private float _moveProgress = 1f;
    private float _droneBank;
    private float _dronePitch;
    private int _steps;
    private int _level = 1;
    private DateTime _startedAt;
    private TimeSpan _wonTime;
    private RectangleF _mazeRect;
    private float _cellSize;
    private RectangleF _againButton;
    private RectangleF _menuButton;
    private RectangleF _backButton;
    private readonly RectangleF[] _titleButtons = new RectangleF[5];
    private readonly RectangleF[] _droneButtons = new RectangleF[5];
    private readonly RectangleF[] _paintPartButtons = new RectangleF[2];
    private readonly RectangleF[] _colorButtons = new RectangleF[12];
    private readonly RectangleF[] _settingsRows = new RectangleF[4];
    private readonly RectangleF[] _settingsDecreaseButtons = new RectangleF[4];
    private readonly RectangleF[] _settingsIncreaseButtons = new RectangleF[4];
    private RectangleF _closeButton;
    private RectangleF _minButton;
    private int _menuSelection;
    private int _customizeSection;
    private int _customizeIndex;
    private int _settingsSelection;
    private int _hoverMenu = -1;
    private int _hoverDrone = -1;
    private int _hoverPaintPart = -1;
    private int _hoverColor = -1;
    private int _hoverSetting = -1;
    private int _hoverOverlay = -1;
    private bool _hoverBack;
    private Point _dragOrigin;
    private bool _dragging;
    private Rectangle _windowedBounds;
    private float _time;
    private float _impactPulse;
    private Point _impactCell;
    private float _transferPulse;
    private int _damageTaken;
    private int _totalDamageSustained;
    private float _hitEffect;
    private float _invulnerability;
    private bool _teleportDone;
    private bool _failurePending;
    private bool _pendingWin;
    private Point _playerPreviousCell;
    private float _warningFlash;
    private float _warningSoundCooldown;
    private CancellationTokenSource? _loadingCancellation;
    private int _loadingSerial;
    private float _loadingAge;
    private float _loadingProgress;
    private float _loadingDisplayProgress;
    private string _loadingStage = "MOUNTING FIELD CASSETTE";
    private bool _loadingFault;
    private bool _resourcesDisposed;

    public GameForm()
    {
        _settings = GameSettingsStore.Load();
        var customization = _settings.GetDroneCustomization();
        _drone = customization.Model;
        _playerColor = customization.CoreColor;
        _playerFrameColor = customization.FrameColor;
        _audio = new AudioManager(_settings.Volume);
        InitializeOnlineUi();
        InitializeProgression();
        _labCursor = LabCursor.Create(active: false);
        _labActionCursor = LabCursor.Create(active: true);
        Cursor = _labCursor.Cursor;
        Text = "Dust";
        ClientSize = SettingsCatalog.Resolutions[_settings.ResolutionIndex].ClientSize;
        MinimumSize = new Size(CanvasWidth, CanvasHeight);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = C.Void;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        _timer.Tick += (_, _) => TickGame();
        _timer.Start();
        Paint += PaintScene;
        KeyDown += HandleKeyDown;
        KeyPress += HandleOnlineKeyPress;
        MouseMove += HandleMouseMove;
        MouseWheel += HandleMouseWheel;
        MouseDown += HandleMouseDown;
        MouseDoubleClick += (_, e) =>
        {
            if (TryWindowToScene(e.Location, out var scene) && scene.Y < 30)
                ToggleFullscreen();
        };
        MouseUp += (_, _) => _dragging = false;
        MouseLeave += (_, _) => ResetHover();
        FormClosing += (_, _) =>
        {
            CloseMissionDossier(playSound: false);
            if (_mode is ScreenMode.Playing or ScreenMode.Shop) RecordAchievementAbandonment();
            _loadingCancellation?.Cancel();
            BeginOnlineShutdown();
        };
        Shown += (_, _) =>
        {
            _windowedBounds = Bounds;
            ApplyDisplaySettings(recenter: false);
            RequestMenuMusic();
        };
        FormClosed += (_, _) => SaveSettings();
    }

    private void TickGame()
    {
        const float deltaTime = .016f;
        _time += deltaTime;
        ProcessOnlineUiQueue();
        _audio.UpdateMusic();
        TickOnlineGameplay(deltaTime);
        if (_mode == ScreenMode.Playing && _missionDossierOpen &&
            !IsOnlineGameplayActive)
        {
            // The paper file is a true safe pause: no enemies, mission timers,
            // perks, cooldowns, chase clocks, or achievement clocks advance.
            Invalidate();
            return;
        }
        UpdateAchievementTracking(deltaTime);
        var onlineShopOverlay = IsOnlineGameplayActive && _mode == ScreenMode.Shop;
        if (onlineShopOverlay) UpdateShop(deltaTime);
        if (_mode != ScreenMode.Playing && !onlineShopOverlay)
        {
            if (_mode == ScreenMode.Shop)
                UpdateShop(deltaTime);
            if (_mode == ScreenMode.Loading)
            {
                _loadingAge += deltaTime;
                _loadingDisplayProgress += (_loadingProgress - _loadingDisplayProgress) * .1f;
            }
            if (_mode == ScreenMode.Won)
            {
                _transferPulse = Math.Max(0, _transferPulse - .025f);
                UpdateResultAnimation(deltaTime);
            }
            Invalidate();
            return;
        }

        _previousVisualCell = _visualCell;
        if (_moveProgress < 1f)
        {
            _moveProgress = Math.Min(1f, _moveProgress + .13f);
            var eased = 1f - MathF.Pow(1f - _moveProgress, 3);
            _visualCell = new PointF(
                _moveFrom.X + (_moveTo.X - _moveFrom.X) * eased,
                _moveFrom.Y + (_moveTo.Y - _moveFrom.Y) * eased);
        }
        if (!_movementArrivalHandled) AdvancePlayerTraversalArrivals();

        var frameMoveX = _visualCell.X - _previousVisualCell.X;
        var frameMoveY = _visualCell.Y - _previousVisualCell.Y;
        var frameTravel = MathF.Sqrt(frameMoveX * frameMoveX + frameMoveY * frameMoveY);
        var tiltImpulse = Math.Clamp(frameTravel / .09f, 0, 1);
        var targetBank = frameTravel > .0001f ? frameMoveX / frameTravel * tiltImpulse : 0;
        var targetPitch = frameTravel > .0001f ? frameMoveY / frameTravel * tiltImpulse : 0;
        _droneBank += (targetBank - _droneBank) * .34f;
        _dronePitch += (targetPitch - _dronePitch) * .34f;
        if (Math.Abs(_droneBank) < .002f) _droneBank = 0;
        if (Math.Abs(_dronePitch) < .002f) _dronePitch = 0;

        _cameraCell = new PointF(
            _cameraCell.X + (_visualCell.X - _cameraCell.X) * .16f,
            _cameraCell.Y + (_visualCell.Y - _cameraCell.Y) * .16f);

        _impactPulse = Math.Max(0, _impactPulse - .07f);
        _transferPulse = Math.Max(0, _transferPulse - .025f);
        _warningFlash = Math.Max(0, _warningFlash - deltaTime);
        _warningSoundCooldown = Math.Max(0, _warningSoundCooldown - deltaTime);
        _invulnerability = Math.Max(0, _invulnerability - deltaTime);
        UpdatePerks(deltaTime);
        if (_hitEffect > 0)
        {
            _hitEffect = Math.Max(0, _hitEffect - deltaTime);
            if (_failurePending)
            {
                if (_hitEffect <= 0)
                {
                    if (IsOnlineGameplayActive) HandleOnlineLocalDefeat();
                    else EnterFailure();
                }
            }
            else if (!_teleportDone && _hitEffect <= .55f)
            {
                TeleportPlayerToSafety();
            }
        }
        if (_mode != ScreenMode.Playing && !onlineShopOverlay)
        {
            Invalidate();
            return;
        }
        UpdateHollows(deltaTime);
        UpdateSentries(deltaTime);
        UpdateMissionState(deltaTime);
        CheckHollowCollision();
        CheckOnlineRemoteHollowCollisions();
        if (_pendingWin && (!IsOnlineGameplayActive || IsOnlineSimulationHost) &&
            _mode == ScreenMode.Playing && _hitEffect <= 0 &&
            _moveProgress >= 1 && _playerCell == _exitCell)
            CompleteWin();
        Invalidate();
    }

    private PointF CellCenter(Point cell) => CellCenter(new PointF(cell.X, cell.Y));
    private PointF CellCenter(PointF cell)
    {
        var camera = GetRenderCamera();
        return new PointF(
            _mazeRect.X + _mazeRect.Width / 2 + (cell.X - camera.X) * _cellSize,
            _mazeRect.Y + _mazeRect.Height / 2 + (cell.Y - camera.Y) * _cellSize);
    }

    private PointF GetRenderCamera()
    {
        if (_maze is null || _cellSize <= 0 || _mazeRect.IsEmpty) return _cameraCell;
        var halfColumns = _mazeRect.Width / _cellSize / 2;
        var halfRows = _mazeRect.Height / _cellSize / 2;
        var minX = halfColumns - .5f;
        var maxX = _maze.Width - .5f - halfColumns;
        var minY = halfRows - .5f;
        var maxY = _maze.Height - .5f - halfRows;
        var x = minX <= maxX ? Math.Clamp(_cameraCell.X, minX, maxX) : (_maze.Width - 1) / 2f;
        var y = minY <= maxY ? Math.Clamp(_cameraCell.Y, minY, maxY) : (_maze.Height - 1) / 2f;
        return new PointF(x, y);
    }

    private RectangleF CanvasDestination()
    {
        var scale = Math.Min(ClientSize.Width / (float)CanvasWidth, ClientSize.Height / (float)CanvasHeight);
        var width = CanvasWidth * scale;
        var height = CanvasHeight * scale;
        return new RectangleF((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
    }

    private PointF WindowToScene(Point point)
    {
        var destination = CanvasDestination();
        return new PointF(
            (point.X - destination.X) / destination.Width * DesignWidth,
            (point.Y - destination.Y) / destination.Height * DesignHeight);
    }

    private bool TryWindowToScene(Point point, out PointF scene)
    {
        var destination = CanvasDestination();
        if (destination.Width <= 0 || destination.Height <= 0 || !destination.Contains(point))
        {
            scene = PointF.Empty;
            return false;
        }

        scene = new PointF(
            (point.X - destination.X) / destination.Width * DesignWidth,
            (point.Y - destination.Y) / destination.Height * DesignHeight);
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _resourcesDisposed = true;
            _loadingCancellation?.Cancel();
            _loadingCancellation?.Dispose();
            _loadingCancellation = null;
            _timer.Dispose();
            _canvas.Dispose();
            _onlineClient.Dispose();
            _audio.Dispose();
            _labActionCursor.Dispose();
            _labCursor.Dispose();
        }
        base.Dispose(disposing);
    }
}
