namespace Dust;

internal sealed partial class GameForm
{
    private void DrawSurvivorObjective(Graphics g)
    {
        if (_survivorObjective is not { } objective) return;

        if (_revealedRoomIds.Contains(objective.RequesterRoomId))
        {
            var requesterCenter = CellCenter(objective.RequesterCell);
            if (objective.Stage == SurvivorObjectiveStage.Rescued)
            {
                DrawHumanFigure(g, new PointF(requesterCenter.X - 13, requesterCenter.Y + 1),
                    crouched: false, requester: true, objective.VisualPhase);
                DrawHumanFigure(g, new PointF(requesterCenter.X + 15, requesterCenter.Y + 4),
                    crouched: true, requester: false, objective.VisualPhase + 1.7f);
                DrawSurvivorSignalPlate(g, requesterCenter, "2 LIFE SIGNS / STABLE", C.Signal);
            }
            else
            {
                DrawHumanFigure(g, requesterCenter, crouched: false, requester: true,
                    objective.VisualPhase);
                DrawSurvivorSignalPlate(g, requesterCenter, "DISTRESS SOURCE", C.Oxide);
            }
        }

        if (objective.Stage is SurvivorObjectiveStage.Uncontacted or SurvivorObjectiveStage.Searching)
        {
            var workerCenter = CellCenter(objective.WorkerCell);
            DrawHumanFigure(g, workerCenter, crouched: true, requester: false,
                objective.VisualPhase + 2.9f);
            var pulse = 17 + (MathF.Sin(_time * 3.2f + objective.VisualPhase) + 1) * 5;
            using var distress = new Pen(Color.FromArgb(115, C.Oxide), 2);
            g.DrawRectangle(distress, workerCenter.X - pulse, workerCenter.Y - pulse,
                pulse * 2, pulse * 2);
        }
        else if (objective.Stage == SurvivorObjectiveStage.Escorting)
        {
            var droneCell = _visualCell;
            var bank = _droneBank;
            var pitch = _dronePitch;
            if (IsOnlineGameplayActive &&
                objective.EscortPlayerId is { } escortId &&
                escortId != _onlinePlayerId)
            {
                if (!_onlinePlayers.TryGetValue(escortId, out var escort) ||
                    !escort.Connected || escort.Defeated)
                    return;
                droneCell = escort.VisualCell;
                bank = escort.Bank;
                pitch = escort.Pitch;
            }

            var droneCenter = CellCenter(droneCell);
            var escortCenter = new PointF(droneCenter.X + 31 - bank * 6,
                droneCenter.Y + 23 - pitch * 4);
            using var tether = new Pen(Color.FromArgb(125, C.Signal), 2);
            g.DrawLine(tether, escortCenter.X - 7, escortCenter.Y - 5,
                droneCenter.X + 8, droneCenter.Y + 8);
            DrawHumanFigure(g, escortCenter, crouched: true, requester: false,
                objective.VisualPhase + _time * .4f);
        }
    }

    private void DrawHumanFigure(Graphics g, PointF center, bool crouched, bool requester, float phase)
    {
        var tremor = MathF.Sin(_time * (requester ? 1.7f : 2.6f) + phase) * 1.2f;
        center.X += tremor;
        var bodyColor = requester ? Color.FromArgb(118, 128, 101) : Color.FromArgb(142, 91, 75);
        using var shadow = new SolidBrush(Color.FromArgb(165, C.Void));
        using var body = new SolidBrush(bodyColor);
        using var skin = new SolidBrush(Color.FromArgb(177, 161, 125));
        using var dark = new SolidBrush(C.Ink);
        using var signal = new SolidBrush(requester ? C.Signal : C.Oxide);

        var headY = center.Y - (crouched ? 10 : 18);
        g.FillRectangle(shadow, center.X - 9 + 4, center.Y - 14 + 5, 19, 28);
        g.FillRectangle(body, center.X - 8, center.Y - (crouched ? 5 : 10), 16,
            crouched ? 15 : 22);
        g.FillRectangle(skin, center.X - 6, headY - 6, 12, 11);
        g.FillRectangle(dark, center.X - 5, headY + 2, 10, 3);
        g.FillRectangle(signal, center.X - 4, headY, 2, 2);
        g.FillRectangle(signal, center.X + 2, headY, 2, 2);

        if (crouched)
        {
            g.FillRectangle(body, center.X - 13, center.Y + 5, 10, 7);
            g.FillRectangle(body, center.X + 3, center.Y + 5, 10, 7);
            g.FillRectangle(dark, center.X - 15, center.Y + 10, 9, 4);
            g.FillRectangle(dark, center.X + 6, center.Y + 10, 9, 4);
        }
        else
        {
            g.FillRectangle(body, center.X - 12, center.Y - 7, 5, 17);
            g.FillRectangle(body, center.X + 7, center.Y - 7, 5, 17);
            g.FillRectangle(dark, center.X - 7, center.Y + 10, 5, 11);
            g.FillRectangle(dark, center.X + 2, center.Y + 10, 5, 11);
        }
    }

    private void DrawSurvivorSignalPlate(Graphics g, PointF center, string text, Color color)
    {
        var width = LabFont.Measure(text, 1).Width + 22;
        var rect = new RectangleF(center.X - width / 2, center.Y + 31, width, 23);
        using var fill = new SolidBrush(Color.FromArgb(225, C.Ink));
        using var edge = new Pen(color, 2);
        g.FillRectangle(fill, rect);
        g.DrawRectangle(edge, rect.X, rect.Y, rect.Width, rect.Height);
        LabFont.Draw(g, text, center.X, rect.Y + 5, 1, color, LabTextAlign.Center, 0);
    }
}
