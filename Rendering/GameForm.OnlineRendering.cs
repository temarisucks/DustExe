namespace Dust;

internal sealed partial class GameForm
{
    private void DrawOnlineRemotePlayers(Graphics g)
    {
        if (!IsOnlineGameplayActive) return;
        foreach (var player in _onlinePlayers.Values
                     .Where(player => !player.Extracted)
                     .OrderBy(player => player.JoinOrder))
        {
            var center = CellCenter(player.VisualCell);
            var alpha = !player.Connected
                ? 42
                : player.Defeated
                    ? 68
                : player.InShop
                ? 55
                : player.CamouflageTimer > 0
                    ? 72
                    : 225;
            DrawRemoteCargoRack(g, player, center);
            DrawDrone(g, player.Drone, player.CoreColor, player.FrameColor,
                center, _cellSize * .27f, alpha,
                drawShadow: true, drawBrackets: false,
                bank: player.Bank, pitch: player.Pitch,
                showDamage: true, damageOverride: player.Damage,
                maximumHealthOverride: player.MaximumHealth);

            var labelY = center.Y - _cellSize * .47f;
            using var plate = new SolidBrush(Color.FromArgb(184, C.Ink));
            var label = !player.Connected
                ? $"{player.Username} / LINK LOST"
                : player.Defeated
                    ? $"{player.Username} / UNIT LOST"
                    : player.Username;
            var width = Math.Clamp(label.Length * 12 + 18, 58, 250);
            g.FillRectangle(plate, center.X - width / 2f, labelY - 5, width, 21);
            LabFont.Draw(g, label.ToUpperInvariant(), center.X,
                labelY, 1,
                player.InShop || player.Defeated || !player.Connected
                    ? C.Steel
                    : C.Bone,
                LabTextAlign.Center);
        }
    }

    private void DrawRemoteCargoRack(
        Graphics g,
        OnlineRemotePlayer player,
        PointF droneCenter)
    {
        var carried = _cargoItems.Where(item =>
            item.CarrierPlayerId == player.PlayerId && !item.Delivered).ToList();
        if (carried.Count == 0) return;
        var center = droneCenter;
        center.Y += DroneFloatOffset(player.Drone, player.Bank, player.Pitch) +
                    _cellSize * .23f;
        var rackWidth = Math.Max(22, carried.Count * 18 + 10);
        using var shadow = new SolidBrush(Color.FromArgb(180, C.Void));
        using var rack = new SolidBrush(C.Steel);
        g.FillRectangle(shadow, center.X - rackWidth / 2f + 3,
            center.Y, rackWidth, 11);
        g.FillRectangle(rack, center.X - rackWidth / 2f,
            center.Y - 4, rackWidth, 9);
        for (var index = 0; index < carried.Count; index++)
        {
            var itemCenter = new PointF(
                center.X + (index - (carried.Count - 1) / 2f) * 18,
                center.Y + 5);
            DrawCargoCrate(g, itemCenter, carried[index], .42f, drawLabel: false);
        }
    }
}
