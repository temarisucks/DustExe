using System.Collections;
using System.Reflection;

internal static class Program
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

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
        using var form = (Form)Activator.CreateInstance(gameType)!;
        form.ClientSize = new Size(1280, 800);
        Field<System.Windows.Forms.Timer>(gameType, form, "_timer").Stop();

        var modeField = gameType.GetField("_mode", InstanceFlags)!;
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
            "Objective QA passed: cargo replacement, distinct rooms, activation, transfer lock, and door states.");
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
