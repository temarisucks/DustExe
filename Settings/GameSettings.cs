using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dust;

internal sealed class GameSettings
{
    internal const int DefaultDroneCoreArgb = unchecked((int)0xFF77C598);
    internal const int DefaultDroneFrameArgb = unchecked((int)0xFFB5B897);
    internal const string DefaultOnlineServerUrl =
        "wss://dustexe-production.up.railway.app/ws";
    private const string OnlineServerOverrideVariable = "DUST_ONLINE_SERVER_URL";

    public int Brightness { get; set; } = 100;
    public int Volume { get; set; } = 80;
    public int ResolutionIndex { get; set; }
    public bool Fullscreen { get; set; }
    public long TotalCredits { get; set; }
    public DroneModel DroneModel { get; set; } = global::Dust.DroneModel.Mite;
    public int DroneCoreArgb { get; set; } = DefaultDroneCoreArgb;
    public int DroneFrameArgb { get; set; } = DefaultDroneFrameArgb;
    public string OnlineServerUrl { get; set; } = DefaultOnlineServerUrl;
    public string LastOnlineUsername { get; set; } = string.Empty;
    public ProgressionProfile Progression { get; set; } = new();

    [JsonIgnore]
    public int CurrentWinStreak => Progression.CurrentWinStreak;

    [JsonIgnore]
    public int BestWinStreak => Progression.BestWinStreak;

    public void Normalize()
    {
        Brightness = Math.Clamp(Brightness, 50, 150);
        Volume = Math.Clamp(Volume, 0, 100);
        ResolutionIndex = Math.Clamp(ResolutionIndex, 0, SettingsCatalog.Resolutions.Length - 1);
        TotalCredits = Math.Max(0, TotalCredits);

        if (!Enum.IsDefined(DroneModel))
            DroneModel = global::Dust.DroneModel.Mite;

        DroneCoreArgb = NormalizeOpaqueArgb(DroneCoreArgb, DefaultDroneCoreArgb);
        DroneFrameArgb = NormalizeOpaqueArgb(DroneFrameArgb, DefaultDroneFrameArgb);
        // Ordinary players always use Dust's production relay. Keeping the
        // serialized value normalized also migrates older localhost or
        // accidentally incomplete addresses on the next settings save.
        OnlineServerUrl = DefaultOnlineServerUrl;
        LastOnlineUsername = NormalizeOnlineUsername(LastOnlineUsername);
        Progression ??= new ProgressionProfile();
        Progression.Normalize();
    }

    public DroneCustomization GetDroneCustomization()
    {
        Normalize();
        return new DroneCustomization(
            DroneModel,
            Color.FromArgb(DroneCoreArgb),
            Color.FromArgb(DroneFrameArgb));
    }

    public void SetDroneCustomization(DroneModel model, Color coreColor, Color frameColor)
    {
        DroneModel = model;
        DroneCoreArgb = NormalizeOpaqueArgb(coreColor.ToArgb(), DefaultDroneCoreArgb);
        DroneFrameArgb = NormalizeOpaqueArgb(frameColor.ToArgb(), DefaultDroneFrameArgb);
        Normalize();
    }

    public long AwardCredits(long amount)
    {
        Normalize();
        if (amount <= 0) return TotalCredits;

        TotalCredits = amount > long.MaxValue - TotalCredits
            ? long.MaxValue
            : TotalCredits + amount;
        return TotalCredits;
    }

    public bool IsAchievementUnlocked(AchievementId id) => Progression.IsAchievementUnlocked(id);

    public AchievementProgressSnapshot GetAchievementState(AchievementId id) =>
        Progression.GetAchievementState(id);

    public AchievementProgressSnapshot[] GetAchievementStates() => Progression.GetAchievementStates();

    public bool UnlockAchievement(AchievementId id, DateTimeOffset? unlockedAtUtc = null) =>
        Progression.UnlockAchievement(id, unlockedAtUtc);

    public AchievementProgressUpdate SetAchievementProgress(
        AchievementId id,
        double progress,
        bool onlyIncrease = true) =>
        Progression.UpdateProgress(id, progress, onlyIncrease);

    public AchievementProgressUpdate AdvanceAchievementProgress(AchievementId id, double amount = 1) =>
        Progression.AdvanceProgress(id, amount);

    public IReadOnlyList<AchievementId> RecordMazeWin() => Progression.RecordMazeWin();

    public void ResetWinStreak() => Progression.ResetWinStreak();

    public bool HasEquippedPerk(PerkId id) => Progression.HasEquippedPerk(id);

    public PerkEquipResult EquipPerk(PerkId id) => Progression.EquipPerk(id);

    public bool UnequipPerk(PerkId id) => Progression.UnequipPerk(id);

    private static int NormalizeOpaqueArgb(int argb, int fallback)
    {
        var alpha = unchecked((uint)argb) >> 24;
        return alpha == byte.MaxValue ? argb : fallback;
    }

    internal static string ResolveOnlineServerUrl()
    {
        var candidate = (Environment.GetEnvironmentVariable(OnlineServerOverrideVariable)
                         ?? string.Empty).Trim();
        if (candidate.Length > 256 ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("ws" or "wss") ||
            uri.Scheme == "ws" && !uri.IsLoopback)
            return DefaultOnlineServerUrl;
        return uri.AbsoluteUri.TrimEnd('/');
    }

    private static string NormalizeOnlineUsername(string? value)
    {
        var candidate = new string((value ?? string.Empty)
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
            .Take(20)
            .ToArray());
        return candidate;
    }
}

internal readonly record struct DroneCustomization(
    DroneModel Model,
    Color CoreColor,
    Color FrameColor);

internal readonly record struct DisplayResolution(int Width, int Height)
{
    public Size ClientSize => new(Width, Height);
    public string Label => $"{Width}X{Height}";
}

internal static class SettingsCatalog
{
    public static readonly DisplayResolution[] Resolutions =
    [
        new(1280, 800),
        new(1600, 900),
        new(1920, 1080)
    ];
}

internal static class GameSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly object FileWriteSync = new();
    private static long _saveGeneration;
    private static long _latestWrittenGeneration;

    public static GameSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new GameSettings();
            var settings = JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(SettingsPath)) ?? new GameSettings();
            settings.Normalize();
            return settings;
        }
        catch
        {
            return new GameSettings();
        }
    }

    public static void Save(GameSettings settings)
    {
        try
        {
            var payload = CapturePayload(settings);
            var generation = Interlocked.Increment(ref _saveGeneration);
            WritePayload(payload, generation);
        }
        catch
        {
            // A read-only profile should not stop the game from running.
        }
    }

    /// <summary>
    /// Captures a consistent profile on the UI thread, then performs only the
    /// filesystem work in the background. Generation checks keep a slower old
    /// write from replacing a newer synchronous menu/profile save.
    /// </summary>
    public static void QueueSave(GameSettings settings)
    {
        try
        {
            var payload = CapturePayload(settings);
            var generation = Interlocked.Increment(ref _saveGeneration);
            _ = Task.Run(() => WritePayload(payload, generation));
        }
        catch
        {
            // Saving is best effort; gameplay and result presentation continue.
        }
    }

    private static string CapturePayload(GameSettings settings)
    {
        settings.Normalize();
        return JsonSerializer.Serialize(settings, JsonOptions);
    }

    private static void WritePayload(string payload, long generation)
    {
        try
        {
            lock (FileWriteSync)
            {
                if (generation < _latestWrittenGeneration) return;
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(SettingsPath, payload);
                _latestWrittenGeneration = generation;
            }
        }
        catch
        {
            // A read-only profile should not stop the game from running.
        }
    }

    private static string SettingsPath
    {
        get
        {
            var testOverride = Environment.GetEnvironmentVariable("DUST_SETTINGS_FILE");
            if (!string.IsNullOrWhiteSpace(testOverride) && Path.IsPathFullyQualified(testOverride))
                return testOverride;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dust", "settings.json");
        }
    }
}
