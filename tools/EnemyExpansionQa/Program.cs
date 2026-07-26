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

        var maze = FieldObject(gameType, form, "_maze");
        var mazeType = maze.GetType();
        var directionType = assembly.GetType("Dust.Direction", true)!;

        var triangle = FindHollow(hollows, "Triangle");
        var triangleCell = Property<Point>(triangle, "Cell");
        SetField(gameType, form, "_playerCell", triangleCell);
        SetField(gameType, form, "_visualCell", new PointF(triangleCell.X, triangleCell.Y));
        Invoke(gameType, form, "UpdateHollowPerception", triangle);
        Require(Property<bool>(triangle, "TriangleSplit"),
            "Triangle did not split when it detected a player.");
        var triangleMembers = ((IEnumerable)Property(
                triangle, "TriangleMembers")).Cast<object>().ToArray();
        Require(triangleMembers.Length == 3,
            "A split Triangle did not create three simulated members.");
        for (var tick = 0; tick < 28; tick++)
            Invoke(gameType, form, "UpdateTriangleMembers", triangle, .12f);
        var movedMemberCells = ((IEnumerable)Property(
                triangle, "TriangleMembers")).Cast<object>()
            .Select(member => Property<PointF>(member, "VisualCell"))
            .ToArray();
        Require(movedMemberCells.Distinct().Count() >= 2 &&
                movedMemberCells.Any(member =>
                    Math.Abs(member.X - triangleCell.X) > .5f ||
                    Math.Abs(member.Y - triangleCell.Y) > .5f),
            "Triangle shards remained a cosmetic orbit instead of navigating independently.");
        Require((bool)Invoke(gameType, form, "HollowMakesContact", triangle,
                    movedMemberCells[0], movedMemberCells[0])!,
            "An independently moving Triangle member did not retain contact damage.");

        var rooms = ((IEnumerable)Property(maze, "Rooms")).Cast<object>().ToArray();
        var revealedRoomIds = FieldObject(gameType, form, "_revealedRoomIds");
        var addRevealedRoom = revealedRoomIds.GetType().GetMethod("Add")!;
        foreach (var room in rooms)
            addRevealedRoom.Invoke(revealedRoomIds, [Property<int>(room, "Id")]);
        var encirclementMember = ((IEnumerable)Property(
                triangle, "TriangleMembers")).Cast<object>()
            .OrderBy(member => Property<int>(member, "Index"))
            .First();
        var encircledInsideRevealedRoom = false;
        foreach (var room in rooms)
        {
            var roomId = Property<int>(room, "Id");
            foreach (var target in ((IEnumerable)Property(room, "Cells")).Cast<Point>())
            {
                SetField(gameType, form, "_playerCell", target);
                SetField(gameType, form, "_visualCell", new PointF(target.X, target.Y));
                for (var directionIndex = 0; directionIndex < 8; directionIndex++)
                {
                    SetProperty(triangle, "TriangleOrbitAngle",
                        directionIndex * MathF.PI / 4);
                    var encirclement = (Point)Invoke(gameType, form,
                        "FindTriangleEncirclementTarget", triangle,
                        encirclementMember)!;
                    var encirclementRoom = mazeType.GetMethod("GetRoomAt")!
                        .Invoke(maze, [encirclement]);
                    if (encirclementRoom is null ||
                        Property<int>(encirclementRoom, "Id") != roomId)
                        continue;
                    encircledInsideRevealedRoom = true;
                    break;
                }
                if (encircledInsideRevealedRoom) break;
            }
            if (encircledInsideRevealedRoom) break;
        }
        Require(encircledInsideRevealedRoom,
            "Triangle shards refused to encircle a player inside a revealed storage room.");
        SetField(gameType, form, "_playerCell", triangleCell);
        SetField(gameType, form, "_visualCell",
            new PointF(triangleCell.X, triangleCell.Y));

        SetProperty(triangle, "HasSight", false);
        Invoke(gameType, form, "BeginTriangleReform", triangle);
        for (var tick = 0; tick < 600 &&
             Property<bool>(triangle, "TriangleSplit"); tick++)
            Invoke(gameType, form, "UpdateTriangleMembers", triangle, .10f);
        Require(!Property<bool>(triangle, "TriangleSplit") &&
                ((IEnumerable)Property(triangle, "TriangleMembers"))
                .Cast<object>().Count() == 0,
            "Triangle members did not navigate back together and reform.");

        var camera = FindHollow(hollows, "Camera");
        var cameraCell = Property<Point>(camera, "Cell");
        bool CameraWall(string direction) => (bool)mazeType.GetMethod("HasWall")!
            .Invoke(maze, [cameraCell.X, cameraCell.Y,
                Enum.Parse(directionType, direction)])!;
        Require(CameraWall("Up") && CameraWall("Right") ||
                CameraWall("Right") && CameraWall("Down") ||
                CameraWall("Down") && CameraWall("Left") ||
                CameraWall("Left") && CameraWall("Up"),
            "Camera did not spawn at a perpendicular maze-wall junction.");
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
        var completeHollowRoster = hollows.Cast<object>().ToArray();
        hollows.Clear();
        hollows.Add(star);
        hollows.Add(square);
        Point? starApproachCell = null;
        Point? starContactCell = null;
        var sentryCells = ((IEnumerable)FieldObject(gameType, form, "_sentries"))
            .Cast<object>()
            .Select(sentry => Property<Point>(sentry, "Cell"))
            .ToHashSet();
        for (var y = 0; y < Property<int>(maze, "Height") && starApproachCell is null; y++)
        for (var x = 0; x < Property<int>(maze, "Width") && starApproachCell is null; x++)
        foreach (var direction in Enum.GetValues(directionType).Cast<object>())
        {
            if (!(bool)mazeType.GetMethod("CanMove")!
                    .Invoke(maze, [new Point(x, y), direction])!)
                continue;
            var contact = direction.ToString() switch
            {
                "Up" => new Point(x, y - 1),
                "Right" => new Point(x + 1, y),
                "Down" => new Point(x, y + 1),
                _ => new Point(x - 1, y)
            };
            var approach = new Point(x, y);
            if (sentryCells.Contains(approach) || sentryCells.Contains(contact) ||
                mazeType.GetMethod("GetRoomAt")!.Invoke(maze, [approach]) is not null ||
                mazeType.GetMethod("GetRoomAt")!.Invoke(maze, [contact]) is not null)
                continue;
            PlaceHollow(star, approach);
            PlaceHollow(square, contact);
            var next = (Point?)Invoke(gameType, form,
                "FindNextPathStep", star, contact);
            if (next != contact) continue;
            starApproachCell = approach;
            starContactCell = contact;
            break;
        }
        Require(starApproachCell.HasValue && starContactCell.HasValue,
            "A Star could not route into an occupied hostile cell to make contact.");

        var approachCell = starApproachCell.GetValueOrDefault();
        var contactCell = starContactCell.GetValueOrDefault();
        PlaceHollow(star, approachCell);
        PlaceHollow(square, contactCell);
        SetProperty(star, "Empowered", false);
        SetProperty(square, "Empowered", false);
        Invoke(gameType, form, "UpdateEnemyEmpowerment");
        Require(!Property<bool>(star, "Empowered"),
            "A solitary Star empowered itself.");
        Require(!Property<bool>(square, "Empowered"),
            "A Star empowered an adjacent enemy without physically touching it.");

        Invoke(gameType, form, "StartHollowMove", star, contactCell);
        Invoke(gameType, form, "AdvanceHollow", star,
            Property<float>(star, "MoveDuration") * .5f);
        Invoke(gameType, form, "UpdateEnemyEmpowerment");
        Require(Property<bool>(square, "Empowered"),
            "A moving Star failed to empower the Square when their bodies touched.");
        Require((int)Invoke(gameType, form, "HollowContactDamage", square)! == 2,
            "An empowered Square did not deal two integrity hits.");
        PlaceHollow(star, new Point(-20, -20));
        Invoke(gameType, form, "UpdateEnemyEmpowerment");
        Require(Property<bool>(square, "Empowered"),
            "Star empowerment expired after physical contact ended.");

        var qaSentries = (IList)FieldObject(gameType, form, "_sentries");
        if (qaSentries.Count > 0)
        {
            var contactSentry = qaSentries[0]!;
            SetProperty(contactSentry, "Empowered", false);
            SetProperty(contactSentry, "Phase", Enum.Parse(
                assembly.GetType("Dust.SentryPhase", true)!, "Scanning"));
            PlaceHollow(star, Property<Point>(contactSentry, "Cell"));
            Invoke(gameType, form, "UpdateEnemyEmpowerment");
            Require(Property<bool>(contactSentry, "Empowered"),
                "Physical Star contact did not permanently empower a Turret.");
            SetProperty(contactSentry, "Empowered", false);
            SetProperty(contactSentry, "Phase", Enum.Parse(
                assembly.GetType("Dust.SentryPhase", true)!, "Buried"));
            Invoke(gameType, form, "UpdateEnemyEmpowerment");
            Require(!Property<bool>(contactSentry, "Empowered"),
                "A Star touched and empowered a fully buried Turret.");
            SetProperty(contactSentry, "Phase", Enum.Parse(
                assembly.GetType("Dust.SentryPhase", true)!, "Scanning"));
        }

        PlaceHollow(star, contactCell);
        SetProperty(star, "Empowered", false);
        var secondStar = Activator.CreateInstance(hollowType)!;
        SetProperty(secondStar, "Type", Enum.Parse(hollowKind, "Star"));
        PlaceHollow(secondStar, contactCell);
        SetProperty(secondStar, "Empowered", false);
        hollows.Add(secondStar);
        Invoke(gameType, form, "UpdateEnemyEmpowerment");
        Require(Property<bool>(star, "Empowered") &&
                Property<bool>(secondStar, "Empowered"),
            "Two touching Stars did not empower one another.");
        hollows.Remove(secondStar);

        PlaceHollow(triangle, new Point(contactCell.X + 4, contactCell.Y + 4));
        SetProperty(triangle, "Empowered", false);
        Invoke(gameType, form, "BeginTriangleSplit", triangle);
        var splitMembers = ((IEnumerable)Property(triangle, "TriangleMembers"))
            .Cast<object>()
            .OrderBy(member => Property<int>(member, "Index"))
            .ToArray();
        PlaceTriangleMember(splitMembers[0], contactCell);
        PlaceTriangleMember(splitMembers[1], new Point(contactCell.X + 6, contactCell.Y));
        PlaceTriangleMember(splitMembers[2], new Point(contactCell.X, contactCell.Y + 6));
        PlaceHollow(star, contactCell);
        hollows.Add(triangle);
        Invoke(gameType, form, "UpdateEnemyEmpowerment");
        Require(Property<bool>(triangle, "Empowered"),
            "A Star touching one split Triangle shard did not empower its parent enemy.");
        SetProperty(triangle, "TriangleRallyCell",
            new Point(contactCell.X + 4, contactCell.Y + 4));
        Invoke(gameType, form, "CompleteTriangleReform", triangle);

        hollows.Clear();
        foreach (var hollow in completeHollowRoster)
            hollows.Add(hollow);

        SetProperty(star, "State", Enum.Parse(
            assembly.GetType("Dust.HollowState", true)!, "Chase"));
        SetProperty(star, "HasSight", true);
        SetProperty(star, "TargetPlayerId", string.Empty);
        SetField(gameType, form, "_camouflageTimer", 1f);
        Invoke(gameType, form, "UpdateHollowPerception", star);
        Require(Property(star, "State").ToString() == "Search" &&
                Property<float>(star, "SearchTimer") < 0,
            "A Star abandoned pursuit immediately after losing line of sight.");
        SetField(gameType, form, "_camouflageTimer", 0f);

        var width = Property<int>(maze, "Width");
        var height = Property<int>(maze, "Height");
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
        Invoke(gameType, form, "BeginTriangleSplit", triangle);
        for (var tick = 0; tick < 12; tick++)
            Invoke(gameType, form, "UpdateTriangleMembers", triangle, .10f);
        SaveFrame(gameType, form, Path.Combine(outputDirectory, "triangle-split.png"));
        VerifyTriangleMemberCornerPresentation(
            gameType, form, triangle, screenMode, lobbyStateType,
            lobbySettings, lobbyPlayers);
        Invoke(gameType, form, "BeginTriangleSplit", triangle);

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
        Require(Property<int>(snapshot, "ProtocolVersion") == 5,
            "Expanded enemy and inventory checkpoints were not isolated behind protocol 5.");
        var snapshotTriangle = ((IEnumerable)Property(snapshot, "Hollows"))
            .Cast<object>().First(value => Property<int>(value, "Type") == 3);
        Require(((IEnumerable)Property(snapshotTriangle, "TriangleMembers"))
                .Cast<object>().Count() == 3,
            "The authoritative checkpoint omitted split Triangle member state.");
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
            "Enemy expansion QA passed: independent Triangle split/reform, junction Cameras, " +
            "permanent contact-only Star empowerment and pursuit memory, compact dynamic-wall checkpoint, " +
            "and rendering.");
    }

    private static void VerifyTriangleMemberCornerPresentation(
        Type gameType,
        Form form,
        object triangle,
        Type screenMode,
        Type lobbyStateType,
        object lobbySettings,
        IList lobbyPlayers)
    {
        var activeLobby = Activator.CreateInstance(lobbyStateType,
        [
            "enemy-qa", "ENEMY QA", "other-host", 4, "running",
            2L, 2L, 1, lobbySettings, lobbyPlayers, 919L
        ])!;
        SetField(gameType, form, "_onlineLobby", activeLobby);
        SetField(gameType, form, "_onlinePlayerId", "qa-player");
        SetField(gameType, form, "_onlineMatchActive", true);
        SetField(gameType, form, "_mode", Enum.Parse(screenMode, "Playing"));

        SetProperty(triangle, "PresentationReady", true);
        SetProperty(triangle, "PresentationCell", Property<PointF>(triangle, "VisualCell"));
        SetProperty(triangle, "PreviousPresentationCell",
            Property<PointF>(triangle, "VisualCell"));
        var member = ((IEnumerable)Property(triangle, "TriangleMembers"))
            .Cast<object>()
            .OrderBy(candidate => Property<int>(candidate, "Index"))
            .First();
        SetProperty(member, "PresentationReady", true);
        SetProperty(member, "PresentationCell", new PointF(5, 4));
        SetProperty(member, "PreviousPresentationCell", new PointF(5, 4));
        SetProperty(member, "PresentationSnapshotAge", 0f);
        SetProperty(member, "MoveFrom", new PointF(5, 5));
        SetProperty(member, "MoveTo", new PointF(6, 5));
        SetProperty(member, "MoveProgress", .5f);
        SetProperty(member, "VisualCell", new PointF(5.5f, 5));

        Invoke(gameType, form, "UpdateOnlineHollowPresentation", .016f);
        var presented = Property<PointF>(member, "PresentationCell");
        Require(Math.Abs(presented.X - 5) < .0001f && presented.Y > 4,
            "A guest Triangle shard cut diagonally through a corridor turn.");

        SetField(gameType, form, "_onlineMatchActive", false);
        SetField(gameType, form, "_onlineLobby", null);
        SetField(gameType, form, "_onlinePlayerId", null);
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

    private static void PlaceHollow(object hollow, Point cell)
    {
        var visual = new PointF(cell.X, cell.Y);
        SetProperty(hollow, "Cell", cell);
        SetProperty(hollow, "TargetCell", cell);
        SetProperty(hollow, "PreviousCell", cell);
        SetProperty(hollow, "VisualCell", visual);
        SetProperty(hollow, "PreviousVisualCell", visual);
        SetProperty(hollow, "MoveFrom", visual);
        SetProperty(hollow, "MoveTo", visual);
        SetProperty(hollow, "MoveProgress", 1f);
    }

    private static void PlaceTriangleMember(object member, Point cell)
    {
        var visual = new PointF(cell.X, cell.Y);
        SetProperty(member, "Cell", cell);
        SetProperty(member, "TargetCell", cell);
        SetProperty(member, "PreviousCell", cell);
        SetProperty(member, "VisualCell", visual);
        SetProperty(member, "PreviousVisualCell", visual);
        SetProperty(member, "MoveFrom", visual);
        SetProperty(member, "MoveTo", visual);
        SetProperty(member, "MoveProgress", 1f);
    }

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
