namespace Dust;

internal sealed partial class GameForm
{
    private void DrawCircuitSwitches(Graphics g)
    {
        if (!_hasCircuitObjective || _maze is null) return;
        foreach (var circuitSwitch in _circuitSwitches)
        {
            if (IsCellConcealed(circuitSwitch.Cell)) continue;
            DrawCircuitSwitch(g, circuitSwitch);
        }
    }

    private void DrawCircuitSwitch(Graphics g, CircuitSwitch circuitSwitch)
    {
        var pose = GetRoomFixturePose(circuitSwitch.Cell, circuitSwitch.WallSide);
        var center = PointF.Empty;
        var pulse = (MathF.Sin(_time * 4.2f + circuitSwitch.Phase) + 1) * .5f;
        var activeColor = Color.FromArgb(75, 125, 88);
        var stateColor = circuitSwitch.Activated
            ? activeColor
            : Color.FromArgb(176 + (int)(pulse * 35), 86, 48);

        using var shadow = new SolidBrush(Color.FromArgb(205, C.Void));
        using var shell = new SolidBrush(Color.FromArgb(126, 126, 96));
        using var face = new SolidBrush(Color.FromArgb(26, 34, 31));
        using var state = new SolidBrush(stateColor);
        using var steel = new SolidBrush(Color.FromArgb(178, 173, 132));
        using var cable = new Pen(Color.FromArgb(102, 63, 43), 4);
        using var glow = new SolidBrush(Color.FromArgb(circuitSwitch.Activated ? 28 : 20, stateColor));

        var graphicsState = g.Save();
        ClipToCargoRoom(g, circuitSwitch.RoomId);
        g.TranslateTransform(pose.Center.X, pose.Center.Y);
        g.RotateTransform(pose.Rotation);

        g.FillRectangle(glow, center.X - 28, center.Y - 30, 56, 60);
        g.DrawLine(cable, center.X, center.Y + 23, center.X, center.Y + 40);
        g.FillRectangle(shadow, center.X - 21, center.Y - 25, 46, 54);
        g.FillRectangle(shell, center.X - 23, center.Y - 28, 46, 54);
        g.FillRectangle(face, center.X - 18, center.Y - 22, 36, 43);
        g.FillRectangle(state, center.X - 13, center.Y - 17, 26, 8);

        var pivot = new PointF(center.X, center.Y + 6);
        g.FillRectangle(steel, pivot.X - 5, pivot.Y - 5, 10, 10);
        using var lever = new Pen(circuitSwitch.Activated ? activeColor : C.Oxide, 7);
        var tip = circuitSwitch.Activated
            ? new PointF(pivot.X + 12, pivot.Y - 13)
            : new PointF(pivot.X - 12, pivot.Y + 13);
        g.DrawLine(lever, pivot, tip);
        g.FillRectangle(state, tip.X - 5, tip.Y - 5, 10, 10);
        LabFont.Draw(g, circuitSwitch.Number.ToString("00"), center.X, center.Y + 31, 1,
            circuitSwitch.Activated ? activeColor : C.Signal, LabTextAlign.Center, 0);
        g.Restore(graphicsState);
    }
}
