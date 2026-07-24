using System.Collections;
using System.Reflection;
using System.Text.Json;

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
            : Path.Combine(AppContext.BaseDirectory, "enemy-expansion"));
        Directory.CreateDirectory(outputDirectory);
        Environment.SetEnvironmentVariable("DUST_SETTINGS_FILE",
            Path.Combine(outputDirectory, "settings.json"));

        var assembly = Assembly.Load("Dust");
        var gameType = assembly.GetType("Dust.GameForm", true)!;
        var hollowType = assembly.GetType("Dust.Hollow", true)!;
        var hollowKind = assembly.GetType("Dust.HollowType", true)!;
        var screenMode = assembly.GetType("Dust.ScreenMode", true)!;

        using var form = (Form)Activator.CreateInstance(gameType)!;
        form.ClientSize = new Size(1280, 800);
        Field<System.Windows.Forms.Timer>(gameType, form, "_timer").Stop();

        Invoke(gameType, form, "OpenRunSettings");
        SaveFrame(gameType, form, Path.Combine(outputDirectory, "run-settings.png"));

        var lobbyPlayerType = assembly.GetType("Dust.OnlineLobbyPlayer", true)!;
        var lobbyPlayersType = typeof(List<>).MakeGenericType(lobbyPlayerType);
        var lobbyPlayers = (IList)Activator.CreateInstance(lobbyPlayersType)!;
        lobbyPlayers.Add(Activator.CreateInstance(
            lobbyPlayerType, ["qa-player", "QA", 0, true])!);
        var lobbySettingsType = assembly.GetType("Dust.OnlineLobbySettings", true)!;
        var lobbySettings = lobbySettingsType.GetProperty(
            "Default", StaticFlags)!.GetValue(null)!;
        var lobbyStateType = assembly.GetType("Dust.OnlineLobbyState", true)!;
        var lobbyState = Activator.CreateInstance(lobbyStateType,
        [
            "enemy-qa", "ENEMY QA", "qa-player", 4, "open",
            1L, 1L, 1, lobbySettings, lobbyPlayers, null
        ])!;
        SetField(gameType, form, "_onlineLobby", lobbyState);
        SetField(gameType, form, "_onlinePlayerId", "qa-player");
        SetField(gameType, form, "_onlineUsername", "QA");
        SetField(gameType, form, "_mode", Enum.Parse(screenMode, "LobbyRoom"));
        SaveFrame(gameType, form, Path.Combine(outputDirectory, "online-run-settings.png"));
        SetField(gameType, form, "_onlineLobby", null);
        SetField(gameType, form, "_onlinePlayerId", null);

        Invoke(gameType, form, "InitializeGameState", CancellationToken.None);
        SetField(gameType, form, "_mode", Enum.Parse(screenMode, "Playing"));
        SetField(gameType, form, "_startedAt", DateTime.Now);

        var hollows = (IList)FieldObject(gameType, form, "_hollows");
        var kinds = hollows.Cast<object>()
            .Select(hollow => Property(hollow, "Type").ToString()!)
            .ToHashSet(StringComparer.Ordinal);
        var expected = new[] { "Square", "Diamond", "Hex", "Triangle", "Camera", "Star" };
        Require(expected.All(kinds.Contains),
            $"Default all-type roster was incomplete: {string.Join(", ", kinds)}.");
        Require(((IList)FieldObject(gameType, form, "_sentries")).Count > 0,
            "The default all-type roster did not include the Turret/Sentry.");

        var triangle = FindHollow(hollows, "Triangle");
        var triangleCell = Property<Point>(triangle, "Cell");
        SetField(gameType, form, "_playerCell", triangleCell);
        SetField(gameType, form, "_visualCell", new PointF(triangleCell.X, triangleCell.Y));
        Invoke(gameType, form, "UpdateHollowPerception", triangle);
        Require(Property<bool>(triangle, "TriangleSplit"),
            "Triangle did not split when it detected a player.");
        var members = (Array)Invoke(gameType, form, "TriangleMemberPositions", triangle)!;
        Require(members.Length == 3,
            "A split Triangle did not expose three independently collidable members.");
        foreach (PointF member in members)
        {
            var dx = member.X - triangleCell.X;
            var dy = member.Y - triangleCell.Y;
            Require(dx * dx + dy * dy <= .091f,
                "A split Triangle member escaped its wall-safe orbit.");
        }

        var camera = FindHollow(hollows, "Camera");
        var square = FindHollow(hollows, "Square");
        var cameraVisual = Property<PointF>(camera, "VisualCell");
        var responseCell = new Point(
            (int)MathF.Round(cameraVisual.X + 1),
            (int)MathF.Round(cameraVisual.Y));
        SetProperty(square, "Cell", responseCell);
        SetProperty(square, "TargetCell", responseCell);
        SetProperty(square, "VisualCell", new PointF(responseCell.X, responseCell.Y));
        SetProperty(camera, "Empowered", false);
        Invoke(gameType, form, "DispatchCameraDistress",
            camera, "qa-player", new PointF(triangleCell.X + 2, triangleCell.Y));
        Require(Property(square, "State").ToString() == "Search",
            "Camera distress did not interrupt a nearby patrol.");

        var star = FindHollow(hollows, "Star");
        foreach (var hollow in hollows.Cast<object>())
            SetProperty(hollow, "VisualCell", new PointF(1, 1));
        SetProperty(star, "VisualCell", new PointF(20, 20));
        Invoke(gameType, form, "UpdateEnemyEmpowerment");
        Require(!Property<bool>(star, "Empowered"),
            "A solitary Star empowered itself.");
        SetProperty(square, "VisualCell", new PointF(21, 20));
        Invoke(gameType, form, "UpdateEnemyEmpowerment");
        Require(Property<bool>(square, "Empowered"),
            "A nearby Square was not empowered by a Star.");
        Require((int)Invoke(gameType, form, "HollowContactDamage", square)! == 2,
            "An empowered Square did not deal two integrity hits.");

        var secondStar = Activator.CreateInstance(hollowType)!;
        SetProperty(secondStar, "Type", Enum.Parse(hollowKind, "Star"));
        SetProperty(secondStar, "Cell", new Point(21, 20));
        SetProperty(secondStar, "TargetCell", new Point(21, 20));
        SetProperty(secondStar, "VisualCell", new PointF(21, 20));
        SetProperty(secondStar, "PreviousVisualCell", new PointF(21, 20));
        SetProperty(secondStar, "MoveFrom", new PointF(21, 20));
        SetProperty(secondStar, "MoveTo", new PointF(21, 20));
        SetProperty(secondStar, "MoveProgress", 1f);
        hollows.Add(secondStar);
        Invoke(gameType, form, "UpdateEnemyEmpowerment");
        Require(Property<bool>(star, "Empowered") &&
                Property<bool>(secondStar, "Empowered"),
            "Two distinct Stars did not empower one another.");
        hollows.Remove(secondStar);

        var maze = FieldObject(gameType, form, "_maze");
        var mazeType = maze.GetType();
        var width = Property<int>(maze, "Width");
        var height = Property<int>(maze, "Height");
        var directionType = assembly.GetType("Dust.Direction", true)!;
        var destroyed = false;
        for (var y = 0; y < height && !destroyed; y++)
        for (var x = 0; x < width && !destroyed; x++)
        for (var directionIndex = 0; directionIndex < 4 && !destroyed; directionIndex++)
        {
            var direction = Enum.ToObject(directionType, directionIndex);
            var hasWall = (bool)mazeType.GetMethod("HasWall")!
                .Invoke(maze, [x, y, direction])!;
            if (!hasWall) continue;
            destroyed = (bool)mazeType.GetMethod("TryDestroyWall")!
                .Invoke(maze, [new Point(x, y), direction])!;
        }
        Require(destroyed, "No internal wall could be destroyed.");
        Require(((IEnumerable)Property(maze, "DestroyedWalls")).Cast<object>().Any(),
            "Destroyed wall state was not recorded for an authority checkpoint.");

        ArrangeEnemyPortrait(gameType, form, hollows);
        SetProperty(triangle, "TriangleSplit", false);
        SaveFrame(gameType, form, Path.Combine(outputDirectory, "triangle-intact.png"));
        SetProperty(triangle, "TriangleSplit", true);
        SaveFrame(gameType, form, Path.Combine(outputDirectory, "triangle-split.png"));

        foreach (var hollow in hollows.Cast<object>())
            SetProperty(hollow, "Empowered", true);
        var portraitCenter = Field<Point>(gameType, form, "_playerCell");
        Invoke(gameType, form, "FireHollowProjectile", triangle,
            Enum.Parse(assembly.GetType("Dust.EnemyProjectileKind", true)!, "Triangle"),
            new PointF(portraitCenter.X, portraitCenter.Y));
        Invoke(gameType, form, "FireHollowProjectile", star,
            Enum.Parse(assembly.GetType("Dust.EnemyProjectileKind", true)!, "Star"),
            new PointF(portraitCenter.X, portraitCenter.Y));
        var sentries = (IList)FieldObject(gameType, form, "_sentries");
        if (sentries.Count > 0)
        {
            SetProperty(sentries[0]!, "Empowered", true);
            Invoke(gameType, form, "FireSentryProjectile", sentries[0]!);
        }
        var projectiles = ((IEnumerable)FieldObject(
                gameType, form, "_sentryProjectiles"))
            .Cast<object>().ToArray();
        var triangleShot = projectiles.First(projectile =>
            Property(projectile, "Kind").ToString() == "Triangle");
        var starShot = projectiles.First(projectile =>
            Property(projectile, "Kind").ToString() == "Star");
        Require(!Property<bool>(triangleShot, "IgnoreWalls"),
            "Triangle projectiles incorrectly pierced walls.");
        Require(Property<bool>(starShot, "IgnoreWalls"),
            "Empowered Star projectiles did not pierce walls.");
        Require(sentries.Count == 0 || projectiles.Any(projectile =>
                Property(projectile, "Kind").ToString() == "Sentry" &&
                Property<bool>(projectile, "DestroyWalls")),
            "An empowered Turret round did not carry wall-destruction authority.");
        for (var phase = 0; phase < 3; phase++)
        {
            SetField(gameType, form, "_time", phase * .14f);
            SaveFrame(gameType, form,
                Path.Combine(outputDirectory, $"empowered-phase-{phase + 1}.png"));
        }
        SaveFrame(gameType, form, Path.Combine(outputDirectory, "new-enemies.png"));

        var snapshot = Invoke(gameType, form, "BuildOnlineSnapshot")!;
        Require(Property<int>(snapshot, "ProtocolVersion") == 3,
            "Expanded enemy checkpoints were not isolated behind protocol 3.");
        Require(!string.IsNullOrEmpty((string)Property(snapshot, "DestroyedWallBits")),
            "Destroyed walls were missing from the online checkpoint.");

        var strictnessType = assembly.GetType("Dust.MazeStrictness", true)!;
        object NewMaze(int widthValue, int heightValue, int seed) =>
            Activator.CreateInstance(
                mazeType,
                InstanceFlags,
                binder: null,
                args:
                [
                    widthValue,
                    heightValue,
                    new Random(seed),
                    0,
                    Enum.Parse(strictnessType, "Normal")
                ],
                culture: null)!;

        var roundTripSource = NewMaze(29, 21, 441);
        var roundTripTarget = NewMaze(29, 21, 441);
        Point? roundTripCell = null;
        object? roundTripDirection = null;
        var right = Enum.Parse(directionType, "Right");
        var down = Enum.Parse(directionType, "Down");
        for (var y = 0; y < 21 && roundTripCell is null; y++)
        for (var x = 0; x < 29 && roundTripCell is null; x++)
        {
            foreach (var direction in new[] { right, down })
            {
                var canStayInside = direction.ToString() == "Right" ? x < 28 : y < 20;
                if (!canStayInside ||
                    !(bool)mazeType.GetMethod("HasWall")!
                        .Invoke(roundTripSource, [x, y, direction])!)
                    continue;
                if (!(bool)mazeType.GetMethod("TryDestroyWall")!
                        .Invoke(roundTripSource, [new Point(x, y), direction])!)
                    continue;
                roundTripCell = new Point(x, y);
                roundTripDirection = direction;
                break;
            }
        }
        Require(roundTripCell.HasValue && roundTripDirection is not null,
            "Could not prepare a deterministic destroyed-wall round trip.");
        var verifiedRoundTripCell = roundTripCell.GetValueOrDefault();
        SetField(gameType, form, "_maze", roundTripSource);
        var roundTripSnapshot = Invoke(gameType, form, "BuildOnlineSnapshot")!;
        var roundTripBits = (string)Property(roundTripSnapshot, "DestroyedWallBits");
        SetField(gameType, form, "_maze", roundTripTarget);
        Invoke(gameType, form, "ApplyDestroyedWallBits", roundTripBits);
        Require(!(bool)mazeType.GetMethod("HasWall")!.Invoke(
                roundTripTarget,
                [verifiedRoundTripCell.X, verifiedRoundTripCell.Y, roundTripDirection])!,
            "Destroyed-wall bitset decode did not reopen the authoritative edge.");
        Require(((IEnumerable)Property(roundTripTarget, "DestroyedWalls"))
                .Cast<object>().Any(),
            "Destroyed-wall bitset decode was not retained for host migration.");

        var largeMaze = NewMaze(81, 61, 117);
        for (var y = 0; y < 61; y++)
        for (var x = 0; x < 81; x++)
        {
            if (x < 80)
                mazeType.GetMethod("TryDestroyWall")!
                    .Invoke(largeMaze, [new Point(x, y), right]);
            if (y < 60)
                mazeType.GetMethod("TryDestroyWall")!
                    .Invoke(largeMaze, [new Point(x, y), down]);
        }
        SetField(gameType, form, "_maze", largeMaze);
        var worstCaseSnapshot = Invoke(gameType, form, "BuildOnlineSnapshot")!;
        var worstCaseJson = JsonSerializer.SerializeToUtf8Bytes(worstCaseSnapshot);
        var wallBits = (string)Property(worstCaseSnapshot, "DestroyedWallBits");
        Require(wallBits.Length < 2_000,
            $"The Large-map destroyed-wall bitset was unexpectedly large ({wallBits.Length}).");
        Require(worstCaseJson.Length < 64 * 1024,
            $"Worst-case Large checkpoint exceeded 64 KiB ({worstCaseJson.Length} bytes).");
        File.WriteAllText(Path.Combine(outputDirectory, "worst-checkpoint-size.txt"),
            $"{worstCaseJson.Length} bytes{Environment.NewLine}" +
            $"{wallBits.Length} base64 characters for destroyed walls{Environment.NewLine}");
        Console.WriteLine(
            "Enemy expansion QA passed: seven selectable types, Triangle split, Camera distress, " +
            "Star empowerment, two-hit Square, compact dynamic-wall checkpoint, and rendering.");
    }

    private static void ArrangeEnemyPortrait(Type gameType, object form, IList hollows)
    {
        var maze = FieldObject(gameType, form, "_maze");
        var width = Property<int>(maze, "Width");
        var height = Property<int>(maze, "Height");
        var center = new Point(
            Math.Clamp(width / 2, 4, width - 5),
            Math.Clamp(height / 2, 4, height - 5));
        var offsets = new[]
        {
            new Point(-3, -2), new Point(0, -2), new Point(3, -2),
            new Point(-3, 2), new Point(0, 2), new Point(3, 2)
        };
        var index = 0;
        foreach (var hollow in hollows.Cast<object>())
        {
            if (index >= offsets.Length) break;
            var cell = new Point(center.X + offsets[index].X, center.Y + offsets[index].Y);
            SetProperty(hollow, "Cell", cell);
            SetProperty(hollow, "TargetCell", cell);
            SetProperty(hollow, "VisualCell", new PointF(cell.X, cell.Y));
            SetProperty(hollow, "PreviousVisualCell", new PointF(cell.X, cell.Y));
            SetProperty(hollow, "MoveFrom", new PointF(cell.X, cell.Y));
            SetProperty(hollow, "MoveTo", new PointF(cell.X, cell.Y));
            SetProperty(hollow, "MoveProgress", 1f);
            SetProperty(hollow, "Empowered", index % 2 == 0);
            index++;
        }
        SetField(gameType, form, "_playerCell", center);
        SetField(gameType, form, "_playerPreviousCell", center);
        SetField(gameType, form, "_visualCell", new PointF(center.X, center.Y));
        SetField(gameType, form, "_previousVisualCell", new PointF(center.X, center.Y));
        SetField(gameType, form, "_cameraCell", new PointF(center.X, center.Y));
        SetField(gameType, form, "_moveFrom", new PointF(center.X, center.Y));
        SetField(gameType, form, "_moveTo", new PointF(center.X, center.Y));
        SetField(gameType, form, "_moveProgress", 1f);

        var sentries = (IList)FieldObject(gameType, form, "_sentries");
        if (sentries.Count > 0)
        {
            var sentryCell = new Point(center.X, center.Y + 3);
            SetProperty(sentries[0]!, "Cell", sentryCell);
            SetProperty(sentries[0]!, "PreviousCell", sentryCell);
        }

        var rooms = (IEnumerable)Property(maze, "Rooms");
        var revealed = FieldObject(gameType, form, "_revealedRoomIds");
        var add = revealed.GetType().GetMethod("Add")!;
        foreach (var room in rooms)
            add.Invoke(revealed, [Property<int>(room!, "Id")]);
    }

    private static object FindHollow(IList hollows, string type) =>
        hollows.Cast<object>().First(hollow =>
            Property(hollow, "Type").ToString() == type);

    private static void SaveFrame(Type gameType, Form form, string path)
    {
        using var bitmap = new Bitmap(1280, 800);
        using var graphics = Graphics.FromImage(bitmap);
        using var paintArgs = new PaintEventArgs(
            graphics, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        gameType.GetMethod("PaintScene", InstanceFlags)!.Invoke(form, [form, paintArgs]);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static object? Invoke(Type type, object instance, string method, params object?[] args)
    {
        var candidates = type.GetMethods(InstanceFlags | StaticFlags)
            .Where(candidate => candidate.Name == method &&
                                candidate.GetParameters().Length == args.Length)
            .ToArray();
        if (candidates.Length == 0)
            throw new MissingMethodException(type.FullName, method);
        return candidates[0].Invoke(candidates[0].IsStatic ? null : instance, args);
    }

    private static object FieldObject(Type type, object instance, string name) =>
        type.GetField(name, InstanceFlags)!.GetValue(instance)!;

    private static T Field<T>(Type type, object instance, string name) =>
        (T)FieldObject(type, instance, name);

    private static void SetField(Type type, object instance, string name, object? value) =>
        type.GetField(name, InstanceFlags)!.SetValue(instance, value);

    private static object Property(object instance, string name) =>
        instance.GetType().GetProperty(name, InstanceFlags)!.GetValue(instance)!;

    private static T Property<T>(object instance, string name) =>
        (T)Property(instance, name);

    private static void SetProperty(object instance, string name, object? value) =>
        instance.GetType().GetProperty(name, InstanceFlags)!.SetValue(instance, value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
