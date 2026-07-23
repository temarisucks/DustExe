using System.Drawing.Drawing2D;

namespace Dust;

internal sealed partial class GameForm
{
    private void DrawTrialFeed(Graphics g)
    {
        if (_maze is null) return;
        _cellSize = 74f;
        _mazeRect = new RectangleF(23, 52, DesignWidth - 46, DesignHeight - 106);
        DrawChamber(g);
        DrawTelemetry(g);
        if (_mode == ScreenMode.Playing && _missionDossierOpen) DrawMissionDossier(g);
        else if (_mode == ScreenMode.Won) DrawOutcome(g);
        else if (_mode == ScreenMode.Failed) DrawFailureOverlay(g);
    }

    private void DrawChamber(Graphics g)
    {
        if (_maze is null) return;
        using var frameShadow = new SolidBrush(Color.Black);
        g.FillRectangle(frameShadow, _mazeRect.X + 9, _mazeRect.Y + 10, _mazeRect.Width, _mazeRect.Height);
        using var chamberVoid = new SolidBrush(C.Void);
        g.FillRectangle(chamberVoid, _mazeRect);

        var state = g.Save();
        g.SetClip(_mazeRect);
        var camera = GetRenderCamera();
        var halfColumns = _mazeRect.Width / _cellSize / 2f;
        var halfRows = _mazeRect.Height / _cellSize / 2f;
        var minX = Math.Max(0, (int)MathF.Floor(camera.X - halfColumns) - 1);
        var maxX = Math.Min(_maze.Width - 1, (int)MathF.Ceiling(camera.X + halfColumns) + 1);
        var minY = Math.Max(0, (int)MathF.Floor(camera.Y - halfRows) - 1);
        var maxY = Math.Min(_maze.Height - 1, (int)MathF.Ceiling(camera.Y + halfRows) + 1);

        using var floorA = new SolidBrush(Color.FromArgb(43, 51, 45));
        using var floorB = new SolidBrush(Color.FromArgb(37, 45, 41));
        using var floorC = new SolidBrush(Color.FromArgb(32, 40, 38));
        using var seam = new Pen(Color.FromArgb(59, 67, 57), 1);
        using var shade1 = new SolidBrush(Color.FromArgb(22, 0, 3, 3));
        using var shade2 = new SolidBrush(Color.FromArgb(55, 0, 3, 3));
        using var shade3 = new SolidBrush(Color.FromArgb(96, 0, 3, 3));
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        {
            var center = CellCenter(new PointF(x, y));
            var tile = new RectangleF(center.X - _cellSize / 2, center.Y - _cellSize / 2, _cellSize + 1, _cellSize + 1);
            var variant = Math.Abs(x * 31 + y * 17) % 3;
            g.FillRectangle(variant == 0 ? floorA : variant == 1 ? floorB : floorC, tile);
            g.DrawRectangle(seam, tile.X, tile.Y, tile.Width, tile.Height);
            DrawFloorModule(g, x, y, center, _cellSize, _maze.GetOpeningMask(x, y));
            if (_settings.HasEquippedPerk(PerkId.Retracer) && _visited.Contains(new Point(x, y)))
            {
                using var trace = new SolidBrush(Color.FromArgb(34, _playerColor));
                var traceWidth = Math.Max(4, (int)(_cellSize * .18f));
                g.FillRectangle(trace, center.X - traceWidth, center.Y + _cellSize * .22f, traceWidth * 2, 3);
                if (((x + y) & 1) == 0) g.FillRectangle(trace, center.X + traceWidth / 2f, center.Y + _cellSize * .22f - 5, 3, 8);
            }

            var distance = MathF.Sqrt(MathF.Pow(x - _visualCell.X, 2) + MathF.Pow(y - _visualCell.Y, 2));
            if (distance > 7) g.FillRectangle(shade3, tile);
            else if (distance > 5.4f) g.FillRectangle(shade2, tile);
            else if (distance > 3.8f) g.FillRectangle(shade1, tile);
        }

        DrawRetracerTrail(g);
        DrawHollowVisionCones(g);
        DrawSentryVision(g);
        DrawDroneGroundShadow(g, CellCenter(_visualCell), _cellSize * .29f,
            _droneBank, _dronePitch, PlayerShadowAlpha());
        DrawRoomShrouds(g);
        DrawCargoRoomContents(g);
        DrawCircuitSwitches(g);
        DrawSurvivorObjective(g);
        DrawMissionObjects(g);
        DrawWallNetwork(g, minX, maxX, minY, maxY);
        DrawRoomDoorSignals(g);

        DrawReceiver(g);
        DrawHollows(g);
        DrawSentries(g);
        DrawOnlineRemotePlayers(g);
        DrawCarriedCargoRack(g);
        if (_moveProgress < 1)
            DrawDrone(g, _drone, _playerColor, _playerFrameColor, CellCenter(_moveFrom), _cellSize * .27f,
                (int)(55 * (1 - _moveProgress)), drawShadow: false, drawBrackets: false,
                bank: _droneBank * .35f, pitch: _dronePitch * .35f);
        if (_onlineLocalDefeated)
        {
            // A defeated online drone remains in the checkpoint but leaves the
            // active camera feed while the surviving crew continues the plate.
        }
        else if (_hitEffect > 0)
            DrawDestabilizedDrone(g);
        else
            DrawDrone(g, _drone, _playerColor, _playerFrameColor, CellCenter(_visualCell), _cellSize * .29f,
                PlayerDroneAlpha(),
                drawShadow: false, drawBrackets: true, bank: _droneBank, pitch: _dronePitch,
                showDamage: true);
        DrawPlayerPerkWorldEffects(g);
        DrawDetectionWarning(g);
        if (_impactPulse > 0)
        {
            var impact = CellCenter(_impactCell);
            var extent = 19 + (1 - _impactPulse) * 18;
            using var impactPen = new Pen(Color.FromArgb((int)(_impactPulse * 170), C.Oxide), 3);
            g.DrawRectangle(impactPen, impact.X - extent, impact.Y - extent, extent * 2, extent * 2);
        }
        DrawChamberOcclusion(g);
        DrawLensFlaws(g);
        g.Restore(state);
        DrawFeedFrame(g);
    }

    private void DrawFloorModule(Graphics g, int x, int y, PointF center, float size, int openingMask)
    {
        var hash = Math.Abs(x * 92821 ^ y * 68917);
        var unit = Math.Max(2, (int)(size / 18));
        var openingCount = 0;
        for (var bit = 0; bit < 4; bit++) if ((openingMask & (1 << bit)) != 0) openingCount++;
        using var dark = new SolidBrush(Color.FromArgb(21, 28, 27));
        using var metal = new SolidBrush(Color.FromArgb(75, 84, 70));
        using var oxide = new SolidBrush(Color.FromArgb(104, 51, 39));
        if (openingCount == 1 && hash % 3 != 0)
        {
            // Dead ends terminate in restraint/drain plates.
            g.FillRectangle(dark, center.X - unit * 4, center.Y - unit * 2, unit * 8, unit * 4);
            for (var i = -3; i <= 3; i += 2)
                g.FillRectangle(metal, center.X + i * unit, center.Y - unit * 2, unit / 2f, unit * 4);
            return;
        }
        if (openingCount >= 3 && hash % 4 == 0)
        {
            // Junction apertures quietly track the carrier.
            g.FillRectangle(dark, center.X - unit * 3, center.Y - unit * 2, unit * 6, unit * 4);
            g.FillRectangle(metal, center.X - unit * 2, center.Y - unit, unit * 4, unit * 2);
            using var iris = new SolidBrush(((int)(_time * 3 + x + y) & 1) == 0 ? C.Oxide : C.Ink);
            g.FillRectangle(iris, center.X - unit / 2f, center.Y - unit / 2f, unit, unit);
            return;
        }
        switch (hash % 17)
        {
            case 0: // drain
                g.FillRectangle(dark, center.X - unit * 4, center.Y - unit * 2, unit * 8, unit * 4);
                for (var i = -3; i <= 3; i += 2)
                    g.FillRectangle(metal, center.X + i * unit, center.Y - unit * 2, unit / 2f, unit * 4);
                break;
            case 1: // sample residue
                g.FillRectangle(oxide, center.X - unit * 2, center.Y + unit, unit * 3, unit);
                g.FillRectangle(oxide, center.X, center.Y + unit * 2, unit * 3, unit);
                g.FillRectangle(dark, center.X - unit * 3, center.Y, unit, unit);
                break;
            case 2: // service hatch
                using (var hatchPen = new Pen(Color.FromArgb(75, 84, 70), 2))
                    g.DrawRectangle(hatchPen, center.X - unit * 4, center.Y - unit * 4, unit * 8, unit * 8);
                g.FillRectangle(dark, center.X - unit, center.Y - unit * 3, unit * 2, unit);
                break;
            case 3: // datum stencil
                LabFont.Draw(g, $"{(x + y) % 100:00}", center.X, center.Y - 4, 1, Color.FromArgb(86, 91, 70), LabTextAlign.Center, 0);
                break;
            default:
                g.FillRectangle(metal, center.X - unit / 2f, center.Y - unit / 2f, unit, unit);
                break;
        }
    }

    private void DrawWallNetwork(Graphics g, int minX, int maxX, int minY, int maxY)
    {
        if (_maze is null) return;
        var runs = BuildWallRuns(minX, maxX, minY, maxY);
        using var outerPath = new GraphicsPath(FillMode.Winding);
        using var corePath = new GraphicsPath(FillMode.Winding);
        using var facePath = new GraphicsPath(FillMode.Winding);
        using var lipPath = new GraphicsPath(FillMode.Winding);
        foreach (var run in runs)
        {
            AddWallLayer(outerPath, run, 24);
            AddWallLayer(corePath, run, 18);
            AddWallLayer(facePath, run, 14);
            AddWallHighlight(lipPath, run);
        }

        using var outer = new SolidBrush(Color.FromArgb(5, 9, 10));
        using var core = new SolidBrush(Color.FromArgb(82, 80, 65));
        using var face = new SolidBrush(Color.FromArgb(183, 177, 139));
        using var lip = new SolidBrush(Color.FromArgb(220, 205, 157));
        using var bolt = new SolidBrush(Color.FromArgb(45, 43, 36));
        using var chip = new SolidBrush(Color.FromArgb(102, 83, 61));
        g.FillPath(outer, outerPath);
        g.FillPath(core, corePath);
        g.FillPath(face, facePath);
        g.FillPath(lip, lipPath);

        // Hardware belongs to the continuous run, not to individual maze cells.
        foreach (var run in runs)
        {
            var length = run.Horizontal ? Math.Abs(run.X2 - run.X1) : Math.Abs(run.Y2 - run.Y1);
            for (var position = 31f; position < length - 20; position += 103f)
            {
                if (run.Horizontal) g.FillRectangle(bolt, Math.Min(run.X1, run.X2) + position - 2, run.Y1 - 2, 4, 4);
                else g.FillRectangle(bolt, run.X1 - 2, Math.Min(run.Y1, run.Y2) + position - 2, 4, 4);
            }
            if (length > 58 && (((int)(run.X1 + run.Y1) / 7) & 3) == 1)
            {
                if (run.Horizontal) g.FillRectangle(chip, Math.Min(run.X1, run.X2) + length * .63f, run.Y1 + 3, 9, 4);
                else g.FillRectangle(chip, run.X1 + 3, Math.Min(run.Y1, run.Y2) + length * .63f, 4, 9);
            }
        }
    }

    private List<WallRun> BuildWallRuns(int minX, int maxX, int minY, int maxY)
    {
        var runs = new List<WallRun>();
        if (_maze is null) return runs;
        var firstX = Math.Max(0, minX);
        var lastX = Math.Min(_maze.Width - 1, maxX);
        var firstY = Math.Max(0, minY);
        var lastY = Math.Min(_maze.Height - 1, maxY);

        for (var gridY = Math.Max(0, minY); gridY <= Math.Min(_maze.Height, maxY + 1); gridY++)
        {
            var x = firstX;
            while (x <= lastX)
            {
                if (!HasHorizontalWall(x, gridY)) { x++; continue; }
                var start = x;
                while (x <= lastX && HasHorizontalWall(x, gridY)) x++;
                var from = GridPoint(start, gridY);
                var to = GridPoint(x, gridY);
                runs.Add(new WallRun(true, from.X, from.Y, to.X, to.Y));
            }
        }

        for (var gridX = Math.Max(0, minX); gridX <= Math.Min(_maze.Width, maxX + 1); gridX++)
        {
            var y = firstY;
            while (y <= lastY)
            {
                if (!HasVerticalWall(gridX, y)) { y++; continue; }
                var start = y;
                while (y <= lastY && HasVerticalWall(gridX, y)) y++;
                var from = GridPoint(gridX, start);
                var to = GridPoint(gridX, y);
                runs.Add(new WallRun(false, from.X, from.Y, to.X, to.Y));
            }
        }
        return runs;
    }

    private bool HasHorizontalWall(int x, int gridY)
    {
        if (_maze is null || x < 0 || x >= _maze.Width) return false;
        if (gridY == 0) return _maze.HasWall(x, 0, Direction.Up);
        if (gridY == _maze.Height) return _maze.HasWall(x, _maze.Height - 1, Direction.Down);
        return gridY > 0 && gridY < _maze.Height && _maze.HasWall(x, gridY, Direction.Up);
    }

    private bool HasVerticalWall(int gridX, int y)
    {
        if (_maze is null || y < 0 || y >= _maze.Height) return false;
        if (gridX == 0) return _maze.HasWall(0, y, Direction.Left);
        if (gridX == _maze.Width) return _maze.HasWall(_maze.Width - 1, y, Direction.Right);
        return gridX > 0 && gridX < _maze.Width && _maze.HasWall(gridX, y, Direction.Left);
    }

    private PointF GridPoint(int gridX, int gridY) => CellCenter(new PointF(gridX - .5f, gridY - .5f));

    private static void AddWallLayer(GraphicsPath path, WallRun run, float thickness)
    {
        var extension = thickness / 2;
        if (run.Horizontal)
        {
            var left = Math.Min(run.X1, run.X2);
            path.AddRectangle(new RectangleF(left - extension, run.Y1 - thickness / 2,
                Math.Abs(run.X2 - run.X1) + extension * 2, thickness));
        }
        else
        {
            var top = Math.Min(run.Y1, run.Y2);
            path.AddRectangle(new RectangleF(run.X1 - thickness / 2, top - extension,
                thickness, Math.Abs(run.Y2 - run.Y1) + extension * 2));
        }
    }

    private static void AddWallHighlight(GraphicsPath path, WallRun run)
    {
        if (run.Horizontal)
        {
            var left = Math.Min(run.X1, run.X2);
            path.AddRectangle(new RectangleF(left - 7, run.Y1 - 7, Math.Abs(run.X2 - run.X1) + 14, 3));
        }
        else
        {
            var top = Math.Min(run.Y1, run.Y2);
            path.AddRectangle(new RectangleF(run.X1 - 7, top - 7, 3, Math.Abs(run.Y2 - run.Y1) + 14));
        }
    }

    private readonly record struct WallRun(bool Horizontal, float X1, float Y1, float X2, float Y2);

    private void DrawReceiver(Graphics g)
    {
        var p = CellCenter(_exitCell);
        var locked = _hasCircuitObjective && !CircuitObjectiveComplete;
        var blink = ((int)(_time * (locked ? 7 : 4)) & 1) == 0;
        var halfWidth = Math.Min(35f, _cellSize * .43f);
        var halfHeight = Math.Min(34f, _cellSize * .41f);
        using var shadow = new SolidBrush(Color.FromArgb(176, C.Void));
        using var housing = new SolidBrush(Color.FromArgb(78, 85, 70));
        using var housingLight = new SolidBrush(Color.FromArgb(155, 151, 112));
        using var throat = new SolidBrush(Color.FromArgb(1, 4, 4));
        using var inner = new SolidBrush(Color.FromArgb(21, 29, 26));
        using var status = new SolidBrush(locked
            ? (blink ? C.Red : Color.FromArgb(86, 34, 31))
            : (blink ? C.Signal : Color.FromArgb(115, 78, 43)));
        using var stencil = new SolidBrush(locked ? C.Oxide : C.Bone);

        g.FillPolygon(shadow, CutCornerBoxPoints(new PointF(p.X + 5, p.Y + 7),
            halfWidth + 3, halfHeight + 3, 9));
        g.FillPolygon(housing, CutCornerBoxPoints(p, halfWidth, halfHeight, 8));
        g.FillPolygon(housingLight, CutCornerBoxPoints(new PointF(p.X, p.Y - 2),
            halfWidth - 6, halfHeight - 7, 5));
        g.FillRectangle(throat, p.X - halfWidth + 10, p.Y - halfHeight + 12,
            halfWidth * 2 - 20, halfHeight * 2 - 22);
        g.FillRectangle(inner, p.X - halfWidth + 16, p.Y - halfHeight + 18,
            halfWidth * 2 - 32, halfHeight * 2 - 34);

        // Heavy transfer rails and a physical threshold make this read as an
        // extraction hatch instead of another pickup marker.
        g.FillRectangle(housing, p.X - halfWidth + 7, p.Y - halfHeight + 9, 6, halfHeight * 2 - 16);
        g.FillRectangle(housing, p.X + halfWidth - 13, p.Y - halfHeight + 9, 6, halfHeight * 2 - 16);
        g.FillRectangle(status, p.X - halfWidth + 2, p.Y - halfHeight + 3, halfWidth * 2 - 4, 6);
        g.FillRectangle(status, p.X - halfWidth + 5, p.Y + halfHeight - 9, 7, 5);
        g.FillRectangle(status, p.X + halfWidth - 12, p.Y + halfHeight - 9, 7, 5);

        // Inward chevrons point into the transfer throat.
        for (var side = -1; side <= 1; side += 2)
        {
            var x = p.X + side * (halfWidth - 16);
            var direction = -side;
            using var chevron = new Pen(status.Color, 3)
            {
                StartCap = LineCap.Square,
                EndCap = LineCap.Square,
                LineJoin = LineJoin.Miter
            };
            g.DrawLines(chevron,
            [
                new PointF(x - direction * 4, p.Y - 7),
                new PointF(x + direction * 2, p.Y),
                new PointF(x - direction * 4, p.Y + 7)
            ]);
        }

        if (locked)
        {
            g.FillRectangle(status, p.X - halfWidth + 17, p.Y - 2, halfWidth * 2 - 34, 5);
            LabFont.Draw(g, "EXIT", p.X, p.Y - 12, 1, C.Bone, LabTextAlign.Center, 0);
            LabFont.Draw(g, "SEALED", p.X, p.Y + 9, 1, C.Oxide, LabTextAlign.Center, 0);
        }
        else
        {
            LabFont.Draw(g, "EXIT", p.X, p.Y - 5, 1, stencil.Color, LabTextAlign.Center, 0);
            LabFont.Draw(g, "OUT", p.X, p.Y + 9, 1, C.Signal, LabTextAlign.Center, 0);
        }

        var signWidth = 51f;
        var signAbove = p.Y - halfHeight - 19 >= _mazeRect.Top + 3;
        var signY = signAbove ? p.Y - halfHeight - 19 : p.Y + halfHeight + 4;
        g.FillRectangle(throat, p.X - signWidth / 2, signY, signWidth, 15);
        g.FillRectangle(status, p.X - signWidth / 2, signY, 4, 15);
        LabFont.Draw(g, "EXIT", p.X + 2, signY + 3, 1,
            locked ? C.Oxide : C.Bone, LabTextAlign.Center, 0);
        if (_transferPulse > 0)
        {
            var spread = halfWidth + 8 + (1 - _transferPulse) * 45;
            using var pulse = new Pen(Color.FromArgb((int)(_transferPulse * 155), C.Signal), 3);
            g.DrawRectangle(pulse, p.X - spread, p.Y - spread, spread * 2, spread * 2);
        }
    }

    private static PointF[] CutCornerBoxPoints(PointF center, float halfWidth, float halfHeight, float cut) =>
    [
        new(center.X - halfWidth + cut, center.Y - halfHeight),
        new(center.X + halfWidth - cut, center.Y - halfHeight),
        new(center.X + halfWidth, center.Y - halfHeight + cut),
        new(center.X + halfWidth, center.Y + halfHeight - cut),
        new(center.X + halfWidth - cut, center.Y + halfHeight),
        new(center.X - halfWidth + cut, center.Y + halfHeight),
        new(center.X - halfWidth, center.Y + halfHeight - cut),
        new(center.X - halfWidth, center.Y - halfHeight + cut)
    ];
}
