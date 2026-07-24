using System.Collections;
using System.Globalization;
using System.Reflection;

internal static class Program
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int DefaultSeedCount = 320;
    private static readonly string[] ExpectedSides = ["Up", "Right", "Down", "Left"];

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var outputDirectory = Path.GetFullPath(args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "storage-fixture-integration"));
        var seedCount = args.Length > 1
            ? int.Parse(args[1], CultureInfo.InvariantCulture)
            : DefaultSeedCount;
        if (seedCount < 32)
            throw new ArgumentOutOfRangeException(nameof(args),
                "Storage fixture QA needs at least 32 generated seeds.");

        Directory.CreateDirectory(outputDirectory);
        Environment.SetEnvironmentVariable("DUST_SETTINGS_FILE",
            Path.Combine(outputDirectory, "qa-settings.json"));

        var gameAssembly = Assembly.Load("Dust");
        var gameType = gameAssembly.GetType("Dust.GameForm", throwOnError: true)!;
        var randomType = gameAssembly.GetType("Dust.DustRandom", throwOnError: true)!;
        var screenModeType = gameAssembly.GetType("Dust.ScreenMode", throwOnError: true)!;
        var playingMode = Enum.Parse(screenModeType, "Playing");
        var initialize = gameType.GetMethod("InitializeGameState", InstanceFlags)!;

        using var form = (Form)Activator.CreateInstance(gameType)!;
        form.ClientSize = new Size(1280, 800);
        Field<System.Windows.Forms.Timer>(gameType, form, "_timer").Stop();

        var roomShapes = new HashSet<string>(StringComparer.Ordinal);
        var fixtureCoverage = new Dictionary<string, HashSet<string>>(
            StringComparer.Ordinal);
        var fixtureCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var capturedShapes = new HashSet<string>(StringComparer.Ordinal);
        var capturedSides = new HashSet<string>(StringComparer.Ordinal);
        var generatedRooms = 0;
        var validatedFixtures = 0;
        var freestandingDirectives = 0;
        var capturedFreestandingDirective = false;

        for (var sample = 0; sample < seedCount; sample++)
        {
            var seed = unchecked(0x4455535400000001L + sample * 0x1F123BB5L);
            var random = Activator.CreateInstance(
                randomType,
                InstanceFlags,
                binder: null,
                args: [seed],
                culture: CultureInfo.InvariantCulture)!;
            SetField(gameType, form, "_random", random);
            initialize.Invoke(form, [CancellationToken.None]);
            SetField(gameType, form, "_mode", playingMode);
            SetField(gameType, form, "_startedAt", DateTime.Now);

            var maze = FieldObject(gameType, form, "_maze");
            var rooms = ((IEnumerable)Property<object>(maze, "Rooms"))
                .Cast<object>()
                .ToDictionary(room => Property<int>(room, "Id"));
            generatedRooms += rooms.Count;
            foreach (var room in rooms.Values)
                roomShapes.Add(Property<object>(room, "Shape").ToString()!);

            var fixtures = CollectFixtures(gameAssembly, gameType, form);
            foreach (var fixture in fixtures)
            {
                var wallMounted = ValidateFixture(
                    gameType, form, maze, rooms, fixture, sample, seed);
                validatedFixtures++;
                fixtureCounts[fixture.Kind] =
                    fixtureCounts.GetValueOrDefault(fixture.Kind) + 1;
                if (!wallMounted)
                {
                    freestandingDirectives++;
                    if (!capturedFreestandingDirective)
                    {
                        SaveRoomFrame(
                            gameType,
                            form,
                            rooms[fixture.RoomId],
                            fixture,
                            Path.Combine(outputDirectory,
                                "directive-freestanding.png"));
                        capturedFreestandingDirective = true;
                    }
                    continue;
                }
                if (!fixtureCoverage.TryGetValue(fixture.Kind, out var sides))
                {
                    sides = new HashSet<string>(StringComparer.Ordinal);
                    fixtureCoverage.Add(fixture.Kind, sides);
                }
                sides.Add(fixture.WallSide);
            }

            foreach (var room in rooms.Values)
            {
                var shape = Property<object>(room, "Shape").ToString()!;
                if (capturedShapes.Contains(shape)) continue;
                var roomFixtures = fixtures
                    .Where(fixture => fixture.RoomId == Property<int>(room, "Id"))
                    .ToArray();
                if (roomFixtures.Length == 0) continue;
                var focus = roomFixtures.FirstOrDefault(fixture =>
                                IsWallMounted(room, fixture)) ??
                            roomFixtures[0];
                SaveRoomFrame(
                    gameType,
                    form,
                    room,
                    focus,
                    Path.Combine(outputDirectory,
                        $"room-{FileToken(shape)}.png"));
                capturedShapes.Add(shape);
            }

            foreach (var fixture in fixtures)
            {
                if (!IsWallMounted(rooms[fixture.RoomId], fixture)) continue;
                if (capturedSides.Contains(fixture.WallSide)) continue;
                var room = rooms[fixture.RoomId];
                SaveRoomFrame(
                    gameType,
                    form,
                    room,
                    fixture,
                    Path.Combine(outputDirectory,
                        $"fixture-{FileToken(fixture.WallSide)}.png"));
                capturedSides.Add(fixture.WallSide);
            }
        }

        var cargoRoomShapeType =
            gameAssembly.GetType("Dust.CargoRoomShape", throwOnError: true)!;
        RequireExactCoverage(
            "generated cargo-room shapes",
            roomShapes,
            Enum.GetNames(cargoRoomShapeType));

        var expectedFixtureKinds = ExpectedFixtureKinds(gameAssembly);
        RequireExactCoverage(
            "generated fixture kinds",
            fixtureCoverage.Keys,
            expectedFixtureKinds);
        foreach (var kind in expectedFixtureKinds)
        {
            RequireExactCoverage(
                $"{kind} wall orientations",
                fixtureCoverage[kind],
                ExpectedSides);
        }

        RequireExactCoverage(
            "representative room screenshots",
            capturedShapes,
            Enum.GetNames(cargoRoomShapeType));
        RequireExactCoverage(
            "representative wall-side screenshots",
            capturedSides,
            ExpectedSides);
        Require(freestandingDirectives > 0 && capturedFreestandingDirective,
            "The seed sweep did not exercise the freestanding directive fallback.");

        var summary = new List<string>
        {
            "STORAGE FIXTURE QA PASSED",
            $"Seeds: {seedCount}",
            $"Rooms: {generatedRooms}",
            $"Fixtures: {validatedFixtures}",
            $"Freestanding directive fallbacks: {freestandingDirectives}",
            $"Shapes: {string.Join(", ", roomShapes.OrderBy(value => value))}",
            ""
        };
        summary.AddRange(expectedFixtureKinds.Select(kind =>
            $"{kind}: {fixtureCounts[kind]} placements / " +
            string.Join(", ", fixtureCoverage[kind].OrderBy(SideOrder))));
        File.WriteAllLines(Path.Combine(outputDirectory, "coverage.txt"), summary);

        Console.WriteLine(
            $"QA passed: {validatedFixtures} fixtures across {generatedRooms} " +
            $"square, rectangle, and L-shaped rooms from {seedCount} seeds.");
        Console.WriteLine(
            "Every fixture kind covered Up, Right, Down, and Left sealed walls; " +
            $"screenshots and coverage report written to {outputDirectory}.");
    }

    private static IReadOnlyList<FixtureSample> CollectFixtures(
        Assembly gameAssembly,
        Type gameType,
        object form)
    {
        var fixtures = new List<FixtureSample>();

        foreach (var prop in ((IEnumerable)FieldObject(
                     gameType, form, "_roomProps")).Cast<object>())
        {
            fixtures.Add(ReadFixture(
                prop,
                $"PROP/{Property<object>(prop, "Kind")}"));
        }

        var kiosk = gameType.GetField("_shopKiosk", InstanceFlags)!.GetValue(form);
        if (kiosk is not null)
            fixtures.Add(ReadFixture(kiosk, "KIOSK"));

        foreach (var circuitSwitch in ((IEnumerable)FieldObject(
                     gameType, form, "_circuitSwitches")).Cast<object>())
        {
            fixtures.Add(ReadFixture(circuitSwitch, "SWITCH"));
        }

        foreach (var directive in ((IEnumerable)FieldObject(
                     gameType, form, "_fieldDirectives")).Cast<object>())
        {
            var kind = Property<object>(directive, "Kind").ToString()!;
            foreach (var node in ((IEnumerable)Property<object>(
                         directive, "Nodes")).Cast<object>())
            {
                fixtures.Add(ReadFixture(node, $"DIRECTIVE/{kind}"));
            }
        }

        return fixtures;
    }

    private static FixtureSample ReadFixture(object instance, string kind)
    {
        var wallSideProperty =
            instance.GetType().GetProperty("WallSide", InstanceFlags);
        Require(wallSideProperty is not null,
            $"{instance.GetType().Name} is missing its required WallSide.");
        var wallSide = wallSideProperty!.GetValue(instance)?.ToString();
        Require(!string.IsNullOrWhiteSpace(wallSide),
            $"{instance.GetType().Name} has no usable WallSide.");
        return new FixtureSample(
            kind,
            Property<int>(instance, "RoomId"),
            Property<Point>(instance, "Cell"),
            wallSide!,
            instance);
    }

    private static bool ValidateFixture(
        Type gameType,
        object form,
        object maze,
        IReadOnlyDictionary<int, object> rooms,
        FixtureSample fixture,
        int sample,
        long seed)
    {
        Require(rooms.TryGetValue(fixture.RoomId, out var room),
            FailurePrefix(fixture, sample, seed) +
            $"references missing room {fixture.RoomId}.");
        Require(Property<bool>(room!, "Contains", fixture.Cell),
            FailurePrefix(fixture, sample, seed) +
            $"cell {fixture.Cell} is outside its room.");
        Require(fixture.Cell != Property<Point>(room!, "DoorCell"),
            FailurePrefix(fixture, sample, seed) +
            "was mounted on the room's door cell.");

        var wallMounted = IsWallMounted(room!, fixture);
        if (!wallMounted)
        {
            Require(fixture.Kind.StartsWith(
                    "DIRECTIVE/", StringComparison.Ordinal),
                FailurePrefix(fixture, sample, seed) +
                "is an interior fixture, but only field directives may use the " +
                "freestanding capacity fallback.");
            Require(ExpectedSides.Contains(
                    fixture.WallSide, StringComparer.Ordinal),
                FailurePrefix(fixture, sample, seed) +
                $"has invalid freestanding orientation {fixture.WallSide}.");
            ValidateFreestandingDirectivePose(
                gameType, form, room!, fixture, sample, seed);
            return false;
        }

        var outside = Step(fixture.Cell, fixture.WallSide);
        Require(!Property<bool>(room!, "Contains", outside),
            FailurePrefix(fixture, sample, seed) +
            $"faces {fixture.WallSide}, but {outside} is still inside the room.");

        var mazeType = maze.GetType();
        var directionType = mazeType.Assembly.GetType(
            "Dust.Direction", throwOnError: true)!;
        var direction = Enum.Parse(directionType, fixture.WallSide);
        var hasWall = (bool)mazeType.GetMethod(
                "HasWall", InstanceFlags)!
            .Invoke(maze, [fixture.Cell.X, fixture.Cell.Y, direction])!;
        Require(hasWall,
            FailurePrefix(fixture, sample, seed) +
            $"faces an opening at {fixture.Cell} toward {fixture.WallSide}.");

        var outsideRoom = mazeType.GetMethod("GetRoomAt", InstanceFlags)!
            .Invoke(maze, [outside]);
        Require(outsideRoom is null,
            FailurePrefix(fixture, sample, seed) +
            $"faces directly into another storage room at {outside}.");

        var doorOutward = Property<object>(room!, "DoorOutwardDirection").ToString();
        Require(fixture.Cell != Property<Point>(room!, "DoorCell") ||
                fixture.WallSide != doorOutward,
            FailurePrefix(fixture, sample, seed) +
            "was mounted across the room's doorway.");
        return true;
    }

    private static bool IsWallMounted(object room, FixtureSample fixture) =>
        !Property<bool>(room, "Contains", Step(fixture.Cell, fixture.WallSide));

    private static void ValidateFreestandingDirectivePose(
        Type gameType,
        object form,
        object room,
        FixtureSample fixture,
        int sample,
        long seed)
    {
        foreach (var side in ExpectedSides)
            Require(Property<bool>(room, "Contains", Step(fixture.Cell, side)),
                FailurePrefix(fixture, sample, seed) +
                "was classified freestanding despite touching the room perimeter.");

        // Interior capacity fallbacks keep their chosen rotation but must not
        // inherit the 20%-of-a-tile inward shift used by wall mounts.
        SetField(gameType, form, "_cellSize", 74f);
        SetField(gameType, form, "_mazeRect",
            new RectangleF(23, 52, 1234, 694));
        SetField(gameType, form, "_cameraCell",
            new PointF(fixture.Cell.X, fixture.Cell.Y));
        var expectedCenter = (PointF)gameType.GetMethod(
                "CellCenter",
                InstanceFlags,
                binder: null,
                types: [typeof(Point)],
                modifiers: null)!
            .Invoke(form, [fixture.Cell])!;
        var pose = gameType.GetMethod("GetFieldDirectivePose", InstanceFlags)!
            .Invoke(form, [fixture.Instance])!;
        var actualCenter = Property<PointF>(pose, "Center");
        Require(Math.Abs(expectedCenter.X - actualCenter.X) < .001f &&
                Math.Abs(expectedCenter.Y - actualCenter.Y) < .001f,
            FailurePrefix(fixture, sample, seed) +
            $"freestanding pose shifted from tile center {expectedCenter} to " +
            $"{actualCenter}.");
    }

    private static string FailurePrefix(
        FixtureSample fixture,
        int sample,
        long seed) =>
        $"Seed sample {sample} ({seed}), {fixture.Kind} in room " +
        $"{fixture.RoomId} at {fixture.Cell}: ";

    private static Point Step(Point cell, string side) => side switch
    {
        "Up" => new Point(cell.X, cell.Y - 1),
        "Right" => new Point(cell.X + 1, cell.Y),
        "Down" => new Point(cell.X, cell.Y + 1),
        "Left" => new Point(cell.X - 1, cell.Y),
        _ => throw new InvalidOperationException($"Unknown wall side {side}.")
    };

    private static string[] ExpectedFixtureKinds(Assembly gameAssembly)
    {
        var propKindType =
            gameAssembly.GetType("Dust.RoomPropKind", throwOnError: true)!;
        var directiveKindType =
            gameAssembly.GetType("Dust.FieldDirectiveKind", throwOnError: true)!;
        return Enum.GetNames(propKindType)
            .Select(kind => $"PROP/{kind}")
            .Concat(["KIOSK", "SWITCH"])
            .Concat(Enum.GetNames(directiveKindType)
                .Select(kind => $"DIRECTIVE/{kind}"))
            .ToArray();
    }

    private static void SaveRoomFrame(
        Type gameType,
        Form form,
        object room,
        FixtureSample focus,
        string path)
    {
        var roomId = Property<int>(room, "Id");
        var revealed = FieldObject(gameType, form, "_revealedRoomIds");
        revealed.GetType().GetMethod("Clear")!.Invoke(revealed, null);
        revealed.GetType().GetMethod("Add")!.Invoke(revealed, [roomId]);
        ((IList)FieldObject(gameType, form, "_hollows")).Clear();
        ((IList)FieldObject(gameType, form, "_sentries")).Clear();

        var roomCells = ((IEnumerable)Property<object>(room, "Cells"))
            .Cast<Point>()
            .OrderByDescending(cell =>
                Math.Abs(cell.X - focus.Cell.X) + Math.Abs(cell.Y - focus.Cell.Y))
            .ToArray();
        var drone = roomCells.FirstOrDefault(cell =>
            cell != focus.Cell &&
            Math.Abs(cell.X - focus.Cell.X) + Math.Abs(cell.Y - focus.Cell.Y) <= 3);
        if (drone == Point.Empty || drone == focus.Cell)
            drone = roomCells.First(cell => cell != focus.Cell);

        var focusPoint = new PointF(focus.Cell.X, focus.Cell.Y);
        SetField(gameType, form, "_cameraCell", focusPoint);
        SetField(gameType, form, "_visualCell", new PointF(drone.X, drone.Y));
        SetField(gameType, form, "_previousVisualCell", new PointF(drone.X, drone.Y));
        SetField(gameType, form, "_moveFrom", new PointF(drone.X, drone.Y));
        SetField(gameType, form, "_moveTo", new PointF(drone.X, drone.Y));
        SetField(gameType, form, "_playerCell", drone);
        SetField(gameType, form, "_playerPreviousCell", drone);
        SetField(gameType, form, "_moveProgress", 1f);
        SetField(gameType, form, "_missionNoticeTimer", 0f);
        SetField(gameType, form, "_missionDossierOpen", false);
        SetField(gameType, form, "_time", 2.4f);

        using var bitmap = new Bitmap(1280, 800);
        using var graphics = Graphics.FromImage(bitmap);
        using var paintArgs = new PaintEventArgs(
            graphics, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        gameType.GetMethod("PaintScene", InstanceFlags)!
            .Invoke(form, [form, paintArgs]);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static int SideOrder(string value) =>
        Array.IndexOf(ExpectedSides, value);

    private static string FileToken(string value) => value switch
    {
        "LShape" => "l-shape",
        _ => value.ToLowerInvariant()
    };

    private static void RequireExactCoverage(
        string label,
        IEnumerable<string> actualValues,
        IEnumerable<string> expectedValues)
    {
        var actual = actualValues.ToHashSet(StringComparer.Ordinal);
        var expected = expectedValues.ToHashSet(StringComparer.Ordinal);
        var missing = expected.Except(actual).OrderBy(value => value).ToArray();
        var unexpected = actual.Except(expected).OrderBy(value => value).ToArray();
        Require(missing.Length == 0 && unexpected.Length == 0,
            $"{label} mismatch. Missing [{string.Join(", ", missing)}]; " +
            $"unexpected [{string.Join(", ", unexpected)}].");
    }

    private static object FieldObject(Type type, object instance, string name) =>
        type.GetField(name, InstanceFlags)!.GetValue(instance)!;

    private static T Field<T>(Type type, object instance, string name) =>
        (T)FieldObject(type, instance, name);

    private static void SetField(Type type, object instance, string name, object value) =>
        type.GetField(name, InstanceFlags)!.SetValue(instance, value);

    private static T Property<T>(object instance, string name) =>
        (T)instance.GetType().GetProperty(name, InstanceFlags)!.GetValue(instance)!;

    private static T Property<T>(
        object instance,
        string methodName,
        params object[] arguments) =>
        (T)instance.GetType().GetMethod(methodName, InstanceFlags)!
            .Invoke(instance, arguments)!;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record FixtureSample(
        string Kind,
        int RoomId,
        Point Cell,
        string WallSide,
        object Instance);
}
