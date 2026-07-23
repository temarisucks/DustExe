using System.Collections;
using System.Reflection;

internal static class Program
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private const string ProductionOnlineEndpoint =
        "wss://dustexe-production.up.railway.app/ws";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var outputDirectory = Path.GetFullPath(args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "objective-integration"));
        Directory.CreateDirectory(outputDirectory);
        Environment.SetEnvironmentVariable("DUST_SETTINGS_FILE",
            Path.Combine(outputDirectory, "qa-settings.json"));

        var gameAssembly = Assembly.Load("Dust");
        var gameType = gameAssembly.GetType("Dust.GameForm", throwOnError: true)!;
        VerifyOnlineEndpoint(gameAssembly);
        using var form = (Form)Activator.CreateInstance(gameType)!;
        form.ClientSize = new Size(1280, 800);
        Field<System.Windows.Forms.Timer>(gameType, form, "_timer").Stop();

        var modeField = gameType.GetField("_mode", InstanceFlags)!;
        gameType.GetMethod("OpenOnlinePlay", InstanceFlags)!.Invoke(form, null);
        SaveFrame(gameType, form, Path.Combine(outputDirectory, "online-account.png"));
        VerifyOnlineAccountNavigation(gameType, form);

        var playingMode = Enum.Parse(modeField.FieldType, "Playing");
        modeField.SetValue(form, playingMode);
        SetField(gameType, form, "_startedAt", DateTime.Now);

        var initialize = gameType.GetMethod("InitializeGameState", InstanceFlags)!;
        var hasCircuitField = gameType.GetField("_hasCircuitObjective", InstanceFlags)!;
        for (var attempt = 0; attempt < 30 && !(bool)hasCircuitField.GetValue(form)!; attempt++)
        {
            initialize.Invoke(form, [CancellationToken.None]);
            modeField.SetValue(form, playingMode);
            SetField(gameType, form, "_startedAt", DateTime.Now);
        }
        Require((bool)hasCircuitField.GetValue(form)!,
            "Could not generate a circuit contract in 30 attempts.");

        ((IList)FieldObject(gameType, form, "_hollows")).Clear();
        ((IList)FieldObject(gameType, form, "_sentries")).Clear();

        VerifyDetectionFeedback(gameType, form);
        SaveFrame(gameType, form, Path.Combine(outputDirectory, "detection-warning.png"));
        VerifyCharacterAudio(gameAssembly, gameType, form);

        var switches = ((IEnumerable)FieldObject(gameType, form, "_circuitSwitches"))
            .Cast<object>().ToArray();
        Require(switches.Length == 2, $"Expected two circuit switches; found {switches.Length}.");
        Require(Property<int>(switches[0], "RoomId") != Property<int>(switches[1], "RoomId"),
            "Circuit switches were not assigned to separate rooms.");
        Require(Field<int>(gameType, form, "_cargoRequired") == 0,
            "A first-plate circuit order did not replace both required cargo cases.");

        var revealedRooms = FieldObject(gameType, form, "_revealedRoomIds");
        var firstSwitch = switches[0];
        var switchCell = Property<Point>(firstSwitch, "Cell");
        AddHashSetValue(revealedRooms, Property<int>(firstSwitch, "RoomId"));

        var exitCell = Field<Point>(gameType, form, "_exitCell");
        SetView(gameType, form, new PointF(exitCell.X, exitCell.Y),
            new PointF(exitCell.X, exitCell.Y + 2));
        SaveFrame(gameType, form, Path.Combine(outputDirectory, "exit-locked.png"));

        foreach (var circuitSwitch in switches) SetProperty(circuitSwitch, "Activated", true);
        Require(Property<bool>(gameType, form, "CircuitObjectiveComplete"),
            "Circuit did not complete after both switches were activated.");
        SaveFrame(gameType, form, Path.Combine(outputDirectory, "exit-ready.png"));

        SetProperty(firstSwitch, "Activated", false);
        SetView(gameType, form, new PointF(switchCell.X, switchCell.Y),
            new PointF(switchCell.X, switchCell.Y + 1.75f));
        SaveFrame(gameType, form, Path.Combine(outputDirectory, "switch-open.png"));

        SetView(gameType, form, new PointF(switchCell.X, switchCell.Y),
            new PointF(switchCell.X, switchCell.Y));
        var activated = (bool)gameType.GetMethod("TryActivateCircuitSwitch", InstanceFlags)!
            .Invoke(form, null)!;
        Require(activated && Property<bool>(firstSwitch, "Activated"),
            "The switch could not be activated from its own cell.");
        SaveFrame(gameType, form, Path.Combine(outputDirectory, "switch-closed.png"));

        var maze = FieldObject(gameType, form, "_maze");
        var room = ((IEnumerable)maze.GetType().GetProperty("Rooms")!.GetValue(maze)!)
            .Cast<object>().First();
        var doorCell = Property<Point>(room, "DoorCell");
        var approachCell = Property<Point>(room, "DoorApproachCell");
        var doorCenter = new PointF((doorCell.X + approachCell.X) / 2f,
            (doorCell.Y + approachCell.Y) / 2f);
        var away = new PointF(
            approachCell.X + (approachCell.X - doorCell.X) * 1.5f,
            approachCell.Y + (approachCell.Y - doorCell.Y) * 1.5f);
        SetView(gameType, form, doorCenter, away);
        SaveFrame(gameType, form, Path.Combine(outputDirectory, "door-closed.png"));

        var roomId = Property<int>(room, "Id");
        AddHashSetValue(revealedRooms, roomId);
        var doorProgress = (IDictionary)FieldObject(gameType, form, "_roomDoorOpenProgress");
        doorProgress[roomId] = .55f;
        SaveFrame(gameType, form, Path.Combine(outputDirectory, "door-opening.png"));
        doorProgress[roomId] = 1f;
        SaveFrame(gameType, form, Path.Combine(outputDirectory, "door-open.png"));

        Console.WriteLine(
            "QA passed: embedded endpoint, account navigation, detection feedback, " +
            "character audio, objectives, transfer lock, and door states.");
    }

    private static void VerifyOnlineEndpoint(Assembly gameAssembly)
    {
        var settingsType = gameAssembly.GetType("Dust.GameSettings", throwOnError: true)!;
        var resolver = settingsType.GetMethod("ResolveOnlineServerUrl", StaticFlags)!;
        Environment.SetEnvironmentVariable("DUST_ONLINE_SERVER_URL", null);
        Require((string)resolver.Invoke(null, null)! == ProductionOnlineEndpoint,
            "The default online endpoint is not the embedded Railway relay.");

        Environment.SetEnvironmentVariable(
            "DUST_ONLINE_SERVER_URL", "ws://127.0.0.1:5077/ws");
        Require((string)resolver.Invoke(null, null)! == "ws://127.0.0.1:5077/ws",
            "The developer loopback override was rejected.");

        Environment.SetEnvironmentVariable(
            "DUST_ONLINE_SERVER_URL", "ws://example.com/ws");
        Require((string)resolver.Invoke(null, null)! == ProductionOnlineEndpoint,
            "An insecure remote developer override was accepted.");
        Environment.SetEnvironmentVariable("DUST_ONLINE_SERVER_URL", null);
    }

    private static void VerifyOnlineAccountNavigation(Type gameType, object form)
    {
        Require(Field<Array>(gameType, form, "_onlineAccountFields").Length == 2,
            "The player-facing account screen still has a server-address field.");
        Require(Field<string>(gameType, form, "_onlineServerAddress") == ProductionOnlineEndpoint,
            "The account screen did not resolve the embedded production endpoint.");

        var handler = gameType.GetMethod("HandleOnlineAccountKey", InstanceFlags)!;
        SetField(gameType, form, "_onlineAccountFocus", 0);
        handler.Invoke(form, [new KeyEventArgs(Keys.Shift | Keys.Tab)]);
        Require(Field<int>(gameType, form, "_onlineAccountFocus") == 4,
            "Account Shift+Tab navigation did not wrap to the Back button.");

        SetField(gameType, form, "_onlineAccountFocus", 0);
        handler.Invoke(form, [new KeyEventArgs(Keys.Enter)]);
        Require(Field<int>(gameType, form, "_onlineAccountFocus") == 1,
            "Enter did not advance from username to password.");
        handler.Invoke(form, [new KeyEventArgs(Keys.Enter)]);
        Require(Field<int>(gameType, form, "_onlineAccountFocus") == 2,
            "Enter did not advance from password to Sign Up.");

        SetField(gameType, form, "_onlineAccountFocus", 0);
        for (var expected = 1; expected <= 4; expected++)
        {
            handler.Invoke(form, [new KeyEventArgs(Keys.Tab)]);
            Require(Field<int>(gameType, form, "_onlineAccountFocus") == expected,
                $"Account Tab navigation did not reach focus index {expected}.");
        }
        handler.Invoke(form, [new KeyEventArgs(Keys.Tab)]);
        Require(Field<int>(gameType, form, "_onlineAccountFocus") == 0,
            "Account Tab navigation did not wrap to the username field.");
    }

    private static void VerifyDetectionFeedback(Type gameType, object form)
    {
        SetField(gameType, form, "_warningFlash", 0f);
        SetField(gameType, form, "_warningSoundCooldown", 0f);
        gameType.GetMethod("TriggerOnlineDetectionWarning", InstanceFlags)!
            .Invoke(form, [string.Empty]);
        Require(Field<float>(gameType, form, "_warningFlash") > .8f,
            "An offline Hollow detection did not trigger the exclamation flash.");
        Require(Field<float>(gameType, form, "_warningSoundCooldown") > 0,
            "An offline Hollow detection did not trigger the caught audio path.");
    }

    private static void VerifyCharacterAudio(Assembly gameAssembly, Type gameType, object form)
    {
        var resultLines = (IList)FieldObject(gameType, form, "_resultLines");
        resultLines.Clear();
        resultLines.Add("REPORT AUDIO");
        SetField(gameType, form, "_resultAge", 0f);
        SetField(gameType, form, "_resultAnimationTimestamp", 0L);
        gameType.GetMethod("UpdateResultAnimation", InstanceFlags)!
            .Invoke(form, [.12f]);

        var audio = FieldObject(gameType, form, "_audio");
        var audioType = audio.GetType();
        var cueType = gameAssembly.GetType("Dust.AudioCue", throwOnError: true)!;
        var typeCue = Enum.Parse(cueType, "Type");

        var sounds = (IDictionary)FieldObject(audioType, audio, "_sounds");
        var soundAsset = sounds[typeCue]!;
        var mixer = FieldObject(soundAsset.GetType(), soundAsset, "_mixer");
        var output = FieldObject(soundAsset.GetType(), soundAsset, "_mixerOutput");
        if (mixer is null || output is null)
            throw new InvalidOperationException(
                "The report type cue did not initialize its polyphonic mixer.");

        var inputs = ((IEnumerable)mixer.GetType().GetProperty("MixerInputs")!
            .GetValue(mixer)!).Cast<object>().Count();
        Require(inputs > 1,
            "Rapid report type cues did not overlap in the polyphonic mixer.");
    }

    private static void SaveFrame(Type gameType, Form form, string path)
    {
        using var bitmap = new Bitmap(1280, 800);
        using var graphics = Graphics.FromImage(bitmap);
        using var paintArgs = new PaintEventArgs(graphics, new Rectangle(0, 0, 1280, 800));
        gameType.GetMethod("PaintScene", InstanceFlags)!.Invoke(form, [form, paintArgs]);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void SetView(Type gameType, object form, PointF camera, PointF drone)
    {
        var droneCell = new Point((int)MathF.Round(drone.X), (int)MathF.Round(drone.Y));
        SetField(gameType, form, "_cameraCell", camera);
        SetField(gameType, form, "_visualCell", drone);
        SetField(gameType, form, "_previousVisualCell", drone);
        SetField(gameType, form, "_moveFrom", drone);
        SetField(gameType, form, "_moveTo", drone);
        SetField(gameType, form, "_playerCell", droneCell);
        SetField(gameType, form, "_playerPreviousCell", droneCell);
        SetField(gameType, form, "_moveProgress", 1f);
    }

    private static object FieldObject(Type type, object instance, string name) =>
        type.GetField(name, InstanceFlags)!.GetValue(instance)!;

    private static T Field<T>(Type type, object instance, string name) =>
        (T)FieldObject(type, instance, name);

    private static void SetField(Type type, object instance, string name, object value) =>
        type.GetField(name, InstanceFlags)!.SetValue(instance, value);

    private static T Property<T>(object instance, string name) =>
        (T)instance.GetType().GetProperty(name, InstanceFlags)!.GetValue(instance)!;

    private static T Property<T>(Type type, object instance, string name) =>
        (T)type.GetProperty(name, InstanceFlags)!.GetValue(instance)!;

    private static void SetProperty(object instance, string name, object value) =>
        instance.GetType().GetProperty(name, InstanceFlags)!.SetValue(instance, value);

    private static void AddHashSetValue(object set, int value) =>
        set.GetType().GetMethod("Add")!.Invoke(set, [value]);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
