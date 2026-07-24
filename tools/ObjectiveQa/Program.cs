using System.Collections;
using System.Reflection;
using System.Text.Json;

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
        VerifyFieldDirectives(gameType, form, outputDirectory);
        VerifyShopVisuals(gameType, form, outputDirectory);
        VerifyMultiplayerObjectiveGenerationAndSmoothing(
            gameAssembly, gameType, outputDirectory);

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
            "character audio, personal directives, shop visuals, transfer lock, and door states.");
    }

    private static void VerifyFieldDirectives(
        Type gameType,
        Form form,
        string outputDirectory)
    {
        var directives = ((IEnumerable)FieldObject(gameType, form, "_fieldDirectives"))
            .Cast<object>()
            .ToArray();
        Require(directives.Length == 3,
            $"A medium offline run should have 3 field contracts; found {directives.Length}.");
        Require(directives.Select(item => Property<object>(item, "Kind").ToString())
                .Distinct(StringComparer.Ordinal).Count() == directives.Length,
            "The medium contract set did not contain distinct directive types.");

        var allNodes = directives
            .SelectMany(directive =>
                ((IEnumerable)Property<object>(directive, "Nodes")).Cast<object>())
            .ToArray();
        Require(allNodes.Length >= 5,
            "The personal contract set did not add enough objective nodes.");
        Require(allNodes.Select(node => Property<Point>(node, "Cell")).Distinct().Count() ==
                allNodes.Length,
            "Two personal objective nodes overlap the same cell.");

        var revealedRooms = FieldObject(gameType, form, "_revealedRoomIds");
        foreach (var node in allNodes)
            AddHashSetValue(revealedRooms, Property<int>(node, "RoomId"));

        var firstDirective = directives[0];
        var firstNode = ((IEnumerable)Property<object>(firstDirective, "Nodes"))
            .Cast<object>()
            .First();
        var cell = Property<Point>(firstNode, "Cell");
        SetView(gameType, form, new PointF(cell.X, cell.Y),
            new PointF(cell.X, cell.Y));
        SaveFrame(gameType, form,
            Path.Combine(outputDirectory, "field-directive-open.png"));
        var interacted = (bool)gameType
            .GetMethod("TryActivateFieldDirective", InstanceFlags)!
            .Invoke(form, null)!;
        Require(interacted && Property<int>(firstDirective, "ActivatedMask") != 0,
            "The first personal objective node did not accept interaction.");
        SaveFrame(gameType, form,
            Path.Combine(outputDirectory, "field-directive-activated.png"));

        SetField(gameType, form, "_missionDossierOpen", true);
        SetField(gameType, form, "_missionDossierOpenedAt", DateTime.Now);
        SaveFrame(gameType, form,
            Path.Combine(outputDirectory, "personal-dossier.png"));
        SetField(gameType, form, "_missionDossierOpen", false);
        SetField(gameType, form, "_missionDossierOpenedAt", default(DateTime));
    }

    private static void VerifyShopVisuals(
        Type gameType,
        Form form,
        string outputDirectory)
    {
        var kiosk = gameType.GetField("_shopKiosk", InstanceFlags)!.GetValue(form);
        Require(kiosk is not null, "The generated plate did not contain a reclamation kiosk.");
        var cell = Property<Point>(kiosk!, "Cell");
        var roomId = Property<int>(kiosk!, "RoomId");
        var maze = FieldObject(gameType, form, "_maze");
        var room = ((IEnumerable)maze.GetType().GetProperty("Rooms")!.GetValue(maze)!)
            .Cast<object>()
            .Single(candidate => Property<int>(candidate, "Id") == roomId);
        var adjacentCell = ((IEnumerable)Property<object>(room, "Cells"))
            .Cast<Point>()
            .Where(candidate => Math.Abs(candidate.X - cell.X) +
                                Math.Abs(candidate.Y - cell.Y) == 1)
            .OrderBy(candidate => candidate.Y)
            .ThenBy(candidate => candidate.X)
            .First();
        AddHashSetValue(FieldObject(gameType, form, "_revealedRoomIds"), roomId);
        SetView(gameType, form, new PointF(adjacentCell.X, adjacentCell.Y),
            new PointF(adjacentCell.X, adjacentCell.Y));
        SaveFrame(gameType, form,
            Path.Combine(outputDirectory, "shop-kiosk-world.png"));

        var opened = (bool)gameType.GetMethod("TryOpenShopAtPlayer", InstanceFlags)!
            .Invoke(form, null)!;
        Require(opened, "The reclamation kiosk did not open from a connected adjacent cell.");
        SaveFrame(gameType, form,
            Path.Combine(outputDirectory, "shopkeeper-portrait.png"));
        gameType.GetMethod("LeaveShop", InstanceFlags)!.Invoke(form, null);
    }

    private static void VerifyMultiplayerObjectiveGenerationAndSmoothing(
        Assembly gameAssembly,
        Type gameType,
        string outputDirectory)
    {
        using var form = (Form)Activator.CreateInstance(gameType)!;
        Field<System.Windows.Forms.Timer>(gameType, form, "_timer").Stop();

        var lobbyPlayerType = gameAssembly.GetType(
            "Dust.OnlineLobbyPlayer", throwOnError: true)!;
        var rosterType = typeof(List<>).MakeGenericType(lobbyPlayerType);
        var roster = (IList)Activator.CreateInstance(rosterType)!;
        roster.Add(Activator.CreateInstance(lobbyPlayerType,
            ["player-a", "ALPHA", 0, true])!);
        roster.Add(Activator.CreateInstance(lobbyPlayerType,
            ["player-b", "BETA", 1, true])!);

        var runPlayerType = gameAssembly.GetType(
            "Dust.OnlineRunPlayer", throwOnError: true)!;
        var runRosterType = typeof(List<>).MakeGenericType(runPlayerType);
        var runRoster = (IList)Activator.CreateInstance(runRosterType)!;
        runRoster.Add(Activator.CreateInstance(runPlayerType,
            ["player-a", "ALPHA", 0])!);
        runRoster.Add(Activator.CreateInstance(runPlayerType,
            ["player-b", "BETA", 1])!);
        runRoster.Add(Activator.CreateInstance(runPlayerType,
            ["player-c", "GAMMA", 2])!);
        runRoster.Add(Activator.CreateInstance(runPlayerType,
            ["player-d", "DELTA", 3])!);

        var lobbySettingsType = gameAssembly.GetType(
            "Dust.OnlineLobbySettings", throwOnError: true)!;
        var lobbySettings = lobbySettingsType
            .GetProperty("Default", StaticFlags)!
            .GetValue(null)!;
        var lobbyStateType = gameAssembly.GetType(
            "Dust.OnlineLobbyState", throwOnError: true)!;
        var lobbyState = Activator.CreateInstance(lobbyStateType,
        [
            "qa-lobby", "QA", "player-a", 4, "running",
            1L, 1L, 1, lobbySettings, roster, (long?)424242
        ])!;
        SetProperty(lobbyState, "RunStartPlayers", runRoster);

        SetField(gameType, form, "_onlineMatchActive", true);
        SetField(gameType, form, "_onlineLobby", lobbyState);
        SetField(gameType, form, "_onlinePlayerId", "player-a");
        SetField(gameType, form, "_onlineUsername", "ALPHA");
        SetField(gameType, form, "_onlineRunSeed", 424242L);
        SetField(gameType, form, "_activeRunSettings",
            lobbySettingsType.GetMethod("Snapshot", InstanceFlags)!
                .Invoke(lobbySettings, null)!);
        gameType.GetMethod("CaptureOnlineObjectiveRoster", InstanceFlags)!
            .Invoke(form, [lobbyState]);

        var initialize = gameType.GetMethod("InitializeGameState", InstanceFlags)!;
        initialize.Invoke(form, [CancellationToken.None]);
        var firstSignature = OnlineDirectiveSignature(gameType, form);
        var directives = ((IEnumerable)FieldObject(
            gameType, form, "_fieldDirectives")).Cast<object>().ToArray();
        Require(directives.Length == 12,
            $"Four medium-map players should receive 12 contracts; found {directives.Length}.");
        var ownerCounts = directives.GroupBy(item =>
                Property<string>(item, "AssignedPlayerId"))
            .ToDictionary(group => group.Key, group => group.Count(),
                StringComparer.Ordinal);
        Require(new[] { "player-a", "player-b", "player-c", "player-d" }
                .All(owner => ownerCounts.GetValueOrDefault(owner) == 3),
            "The immutable four-player run roster was not preserved after departures.");

        initialize.Invoke(form, [CancellationToken.None]);
        Require(firstSignature == OnlineDirectiveSignature(gameType, form),
            "The same seed and run-start roster generated different personal contracts.");

        var runSnapshotType = gameAssembly.GetType(
            "Dust.RunSettingsSnapshot", throwOnError: true)!;
        var mapSizeType = gameAssembly.GetType("Dust.RunMapSize", throwOnError: true)!;
        var strictnessType = gameAssembly.GetType(
            "Dust.MazeStrictness", throwOnError: true)!;
        var hollowAmountType = gameAssembly.GetType(
            "Dust.RunHollowAmount", throwOnError: true)!;
        var hollowTypesType = gameAssembly.GetType(
            "Dust.RunHollowTypes", throwOnError: true)!;
        foreach (var quota in new[]
                 {
                     (Map: "Small", PerOwner: 2),
                     (Map: "Large", PerOwner: 4)
                 })
        {
            var runSnapshot = Activator.CreateInstance(runSnapshotType,
            [
                Enum.Parse(mapSizeType, quota.Map),
                Enum.Parse(strictnessType, "Normal"),
                Enum.Parse(hollowAmountType, "Normal"),
                Enum.Parse(hollowTypesType, "All"),
                true
            ])!;
            SetField(gameType, form, "_activeRunSettings", runSnapshot);
            for (var sample = 0; sample < 6; sample++)
            {
                SetField(gameType, form, "_onlineRunSeed",
                    500000L + sample * 7919L + quota.PerOwner);
                initialize.Invoke(form, [CancellationToken.None]);
                var sampleDirectives = ((IEnumerable)FieldObject(
                        gameType, form, "_fieldDirectives"))
                    .Cast<object>()
                    .ToArray();
                Require(sampleDirectives.Length == quota.PerOwner * 4,
                    $"{quota.Map} sample {sample} generated " +
                    $"{sampleDirectives.Length} contracts instead of {quota.PerOwner * 4}.");
                var sampleOwnerCounts = sampleDirectives.GroupBy(item =>
                        Property<string>(item, "AssignedPlayerId"))
                    .ToDictionary(group => group.Key, group => group.Count(),
                        StringComparer.Ordinal);
                Require(new[] { "player-a", "player-b", "player-c", "player-d" }
                        .All(owner =>
                            sampleOwnerCounts.GetValueOrDefault(owner) == quota.PerOwner),
                    $"{quota.Map} sample {sample} did not give every player the full quota.");
            }
        }

        var snapshot = gameType.GetMethod("BuildOnlineSnapshot", InstanceFlags)!
            .Invoke(form, null)!;
        Require(Property<int>(snapshot, "ProtocolVersion") == 2,
            "The personal-objective gameplay protocol was not bumped to version 2.");
        var snapshotDirectives = ((IEnumerable)Property<object>(
            snapshot, "FieldDirectives")).Cast<object>().ToArray();
        Require(snapshotDirectives.Length == 16,
            "The online checkpoint omitted personal contract state.");
        var snapshotJson = JsonSerializer.Serialize(snapshot, snapshot.GetType());
        Require(snapshotJson.Length < 64 * 1024,
            $"The gameplay checkpoint exceeded 64 KiB ({snapshotJson.Length} bytes).");

        var hollows = ((IEnumerable)FieldObject(gameType, form, "_hollows"))
            .Cast<object>().ToArray();
        Require(hollows.Length > 0,
            "The smoothing probe could not find a generated Hollow.");
        var hollow = hollows[0];
        SetProperty(hollow, "MoveFrom", new PointF(3, 3));
        SetProperty(hollow, "MoveTo", new PointF(4, 3));
        SetProperty(hollow, "MoveProgress", .1f);
        SetProperty(hollow, "VisualCell", new PointF(3.1f, 3));
        SetProperty(hollow, "PresentationReady", false);
        gameType.GetMethod("RetargetOnlineHollowPresentation", InstanceFlags)!
            .Invoke(form, [hollow]);
        SetProperty(hollow, "MoveProgress", .7f);
        SetProperty(hollow, "VisualCell", new PointF(3.7f, 3));
        gameType.GetMethod("RetargetOnlineHollowPresentation", InstanceFlags)!
            .Invoke(form, [hollow]);
        gameType.GetMethod("UpdateOnlineHollowPresentation", InstanceFlags)!
            .Invoke(form, [.04f]);
        var presented = Property<PointF>(hollow, "PresentationCell");
        var authoritative = Property<PointF>(hollow, "VisualCell");
        Require(presented.X > 3.1f && presented.X < authoritative.X,
            "The guest Hollow presentation snapped instead of interpolating.");
        Require(Math.Abs(authoritative.X - 3.7f) < .0001f,
            "Presentation smoothing altered authoritative Hollow state.");

        var screenModeType = gameAssembly.GetType(
            "Dust.ScreenMode", throwOnError: true)!;
        SetField(gameType, form, "_mode", Enum.Parse(screenModeType, "Playing"));
        SetField(gameType, form, "_onlineLocalDefeated", true);
        SetField(gameType, form, "_onlineCompletionApplied", false);
        SetField(gameType, form, "_onlineRunCompletedAsCasualty", false);
        SetField(gameType, form, "_runProgressFinalized", true);
        gameType.GetMethod("ApplyOnlineCompletion", InstanceFlags)!
            .Invoke(form, [1234L]);
        Require(FieldObject(gameType, form, "_mode").ToString() == "Failed",
            "A defeated online player received the normal win report.");
        Require(Field<bool>(gameType, form, "_onlineRunCompletedAsCasualty"),
            "A defeated online player was not marked as a run casualty.");
        Require(Field<int>(gameType, form, "_jobPay") == 0,
            "A defeated online player received completion pay.");
        var casualtySnapshot = gameType.GetMethod(
                "BuildOnlineSnapshot", InstanceFlags)!
            .Invoke(form, null)!;
        Require(Property<bool>(casualtySnapshot, "RunCompleted") &&
                !Property<bool>(casualtySnapshot, "RunFailed"),
            "A casualty host did not preserve the team's completed run state.");

        File.WriteAllText(Path.Combine(outputDirectory, "online-checkpoint-size.txt"),
            $"{snapshotJson.Length} bytes{Environment.NewLine}");
    }

    private static string OnlineDirectiveSignature(Type gameType, object form)
    {
        var directives = ((IEnumerable)FieldObject(
                gameType, form, "_fieldDirectives"))
            .Cast<object>()
            .OrderBy(item => Property<int>(item, "Id"));
        return string.Join("|", directives.Select(directive =>
        {
            var nodes = ((IEnumerable)Property<object>(directive, "Nodes"))
                .Cast<object>()
                .Select(node =>
                {
                    var cell = Property<Point>(node, "Cell");
                    return $"{Property<int>(node, "RoomId")}:{cell.X},{cell.Y}";
                });
            return $"{Property<int>(directive, "Id")}:" +
                   $"{Property<object>(directive, "Kind")}:" +
                   $"{Property<string>(directive, "AssignedPlayerId")}:" +
                   string.Join(";", nodes);
        }));
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
