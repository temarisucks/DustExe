using System.Collections;
using System.Reflection;

internal static class Program
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var outputDirectory = Path.GetFullPath(args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "pause-integration"));
        Directory.CreateDirectory(outputDirectory);
        Environment.SetEnvironmentVariable(
            "DUST_SETTINGS_FILE",
            Path.Combine(outputDirectory, "settings.json"));

        var gameAssembly = Assembly.Load("Dust");
        var gameType = gameAssembly.GetType("Dust.GameForm", throwOnError: true)!;
        var screenModeType = gameAssembly.GetType(
            "Dust.ScreenMode", throwOnError: true)!;
        var playingMode = Enum.Parse(screenModeType, "Playing");

        using var form = (Form)Activator.CreateInstance(gameType)!;
        form.ClientSize = new Size(1280, 800);
        Field<System.Windows.Forms.Timer>(gameType, form, "_timer").Stop();
        Invoke(gameType, form, "InitializeGameState", CancellationToken.None);
        SetField(gameType, form, "_mode", playingMode);
        SetField(gameType, form, "_startedAt", DateTime.Now - TimeSpan.FromSeconds(30));
        Invoke(gameType, form, "BeginAchievementRun");

        // Escape opens a navigable pause instead of abandoning or regenerating
        // the live run.
        Press(gameType, form, Keys.Escape);
        Require(Field<bool>(gameType, form, "_pauseMenuOpen"),
            "Escape did not open the pause console.");
        SaveFrame(gameType, form, Path.Combine(outputDirectory, "pause-offline.png"));

        SetField(gameType, form, "_warningFlash", 1f);
        SetField(gameType, form, "_invulnerability", 2f);
        var beforeElapsed = (TimeSpan)Invoke(
            gameType, form, "CurrentMissionElapsed")!;
        Thread.Sleep(45);
        Invoke(gameType, form, "TickGame");
        var afterElapsed = (TimeSpan)Invoke(
            gameType, form, "CurrentMissionElapsed")!;
        Require(Math.Abs((afterElapsed - beforeElapsed).TotalMilliseconds) < 15,
            "The offline mission clock advanced while paused.");
        Require(Math.Abs(Field<float>(gameType, form, "_warningFlash") - 1f) < .0001f &&
                Math.Abs(Field<float>(gameType, form, "_invulnerability") - 2f) < .0001f,
            "Offline gameplay timers advanced while paused.");

        SetField(gameType, form, "_pauseSelection", 1);
        Press(gameType, form, Keys.Enter);
        Require(Field<bool>(gameType, form, "_pauseSettingsOpen"),
            "The Settings pause cartridge did not open.");
        Invoke(gameType, form, "TickGame");
        Require(Math.Abs(Field<float>(gameType, form, "_warningFlash") - 1f) < .0001f,
            "Settings entered from offline pause resumed gameplay.");
        SaveFrame(gameType, form, Path.Combine(outputDirectory, "pause-settings.png"));
        Press(gameType, form, Keys.Escape);
        Require(!Field<bool>(gameType, form, "_pauseSettingsOpen") &&
                Field<bool>(gameType, form, "_pauseMenuOpen"),
            "Escape did not return from Settings to the pause console.");
        Press(gameType, form, Keys.Escape);
        Require(!Field<bool>(gameType, form, "_pauseMenuOpen"),
            "Escape did not resume from the pause console.");

        // R is no longer a hidden live-run reset shortcut.
        var mazeBeforeR = Field<object>(gameType, form, "_maze");
        Press(gameType, form, Keys.R);
        Require(ReferenceEquals(mazeBeforeR, Field<object>(gameType, form, "_maze")) &&
                Equals(Field<object>(gameType, form, "_mode"), playingMode),
            "R still reset the live plate.");

        // Model an active online run without a connected authority socket. Even
        // in that degraded state the local gameplay loop must pass through the
        // pause overlay rather than taking the offline freeze branch.
        var lobbyState = CreateLobbyState(gameAssembly);
        SetField(gameType, form, "_onlineMatchActive", true);
        SetField(gameType, form, "_onlineLobby", lobbyState);
        SetField(gameType, form, "_onlinePlayerId", "player-a");
        SetField(gameType, form, "_onlineUsername", "ALPHA");
        Press(gameType, form, Keys.Escape);
        SetField(gameType, form, "_warningFlash", 1f);
        SetField(gameType, form, "_invulnerability", 2f);
        Invoke(gameType, form, "TickGame");
        Require(Field<float>(gameType, form, "_warningFlash") < 1f &&
                Field<float>(gameType, form, "_invulnerability") < 2f,
            "The online simulation froze behind the pause overlay.");
        SaveFrame(gameType, form, Path.Combine(outputDirectory, "pause-online.png"));

        SetField(gameType, form, "_pauseSelection", 1);
        Press(gameType, form, Keys.Enter);
        SetField(gameType, form, "_warningFlash", 1f);
        Invoke(gameType, form, "TickGame");
        Require(Field<float>(gameType, form, "_warningFlash") < 1f,
            "Settings entered from online pause froze the shared run.");
        Press(gameType, form, Keys.Escape);
        Press(gameType, form, Keys.Escape);

        // Quit to Menu is immediate locally even when no server can answer the
        // leave request. The network notice is deliberately best-effort.
        Press(gameType, form, Keys.Escape);
        SetField(gameType, form, "_pauseSelection", 2);
        Press(gameType, form, Keys.Enter);
        Require(!Field<bool>(gameType, form, "_onlineMatchActive") &&
                Field<object?>(gameType, form, "_onlineLobby") is null &&
                Equals(Field<object>(gameType, form, "_mode"),
                    Enum.Parse(screenModeType, "Title")) &&
                !Field<bool>(gameType, form, "_pauseMenuOpen"),
            "Online Quit to Menu waited on the server or left gameplay active.");

        // Any terminal navigation must clear latent pause/settings state.
        SetField(gameType, form, "_mode", playingMode);
        Press(gameType, form, Keys.Escape);
        Invoke(gameType, form, "EnterTitle", false);
        Require(!Field<bool>(gameType, form, "_pauseMenuOpen") &&
                !Field<bool>(gameType, form, "_pauseSettingsOpen"),
            "Returning to the title left a latent pause overlay.");

        Console.WriteLine(
            $"Pause QA passed: keyboard routing, offline freeze, online live simulation, settings return, terminal cleanup, and rendering. Output: {outputDirectory}");
    }

    private static object CreateLobbyState(Assembly gameAssembly)
    {
        var lobbyPlayerType = gameAssembly.GetType(
            "Dust.OnlineLobbyPlayer", throwOnError: true)!;
        var rosterType = typeof(List<>).MakeGenericType(lobbyPlayerType);
        var roster = (IList)Activator.CreateInstance(rosterType)!;
        roster.Add(Activator.CreateInstance(lobbyPlayerType,
            ["player-a", "ALPHA", 0, true])!);

        var settingsType = gameAssembly.GetType(
            "Dust.OnlineLobbySettings", throwOnError: true)!;
        var settings = settingsType.GetProperty("Default", StaticFlags)!
            .GetValue(null)!;
        var stateType = gameAssembly.GetType(
            "Dust.OnlineLobbyState", throwOnError: true)!;
        return Activator.CreateInstance(stateType,
        [
            "pause-qa", "PAUSE QA", "player-a", 4, "inGame",
            1L, 1L, 1, settings, roster, (long?)424242
        ])!;
    }

    private static void Press(Type type, object instance, Keys key) =>
        Invoke(type, instance, "HandleKeyDown", instance,
            new KeyEventArgs(key));

    private static void SaveFrame(Type gameType, Form form, string path)
    {
        using var bitmap = new Bitmap(1280, 800);
        using var graphics = Graphics.FromImage(bitmap);
        using var args = new PaintEventArgs(
            graphics, new Rectangle(Point.Empty, bitmap.Size));
        Invoke(gameType, form, "PaintScene", form, args);
        bitmap.Save(path);
    }

    private static T Field<T>(Type type, object instance, string name) =>
        (T)type.GetField(name, InstanceFlags)!.GetValue(instance)!;

    private static void SetField(
        Type type,
        object instance,
        string name,
        object? value) =>
        type.GetField(name, InstanceFlags)!.SetValue(instance, value);

    private static object? Invoke(
        Type type,
        object instance,
        string name,
        params object?[] arguments) =>
        type.GetMethod(name, InstanceFlags)!.Invoke(instance, arguments);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
