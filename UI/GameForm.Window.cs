namespace Dust;

internal sealed partial class GameForm
{
    private void DrawWindowChrome(Graphics g)
    {
        _closeButton = new RectangleF(DesignWidth - 41, 7, 25, 22);
        _minButton = new RectangleF(DesignWidth - 73, 7, 25, 22);
        using var fill = new SolidBrush(Color.FromArgb(16, 24, 23));
        using var edge = new Pen(C.Steel, 2);
        g.FillRectangle(fill, _minButton); g.FillRectangle(fill, _closeButton);
        g.DrawRectangle(edge, _minButton.X, _minButton.Y, _minButton.Width, _minButton.Height);
        g.DrawRectangle(edge, _closeButton.X, _closeButton.Y, _closeButton.Width, _closeButton.Height);
        using var mark = new SolidBrush(C.Sick);
        g.FillRectangle(mark, _minButton.X + 7, _minButton.Bottom - 7, 11, 2);
        g.FillRectangle(mark, _closeButton.X + 7, _closeButton.Y + 6, 11, 3);
        g.FillRectangle(mark, _closeButton.X + 11, _closeButton.Y + 3, 3, 11);
    }

    private static void DrawCutPanel(Graphics g, RectangleF rect, Color fillColor, Color edgeColor, float cut, float edgeWidth)
    {
        var points = CutPanelPoints(rect, cut);
        using var fill = new SolidBrush(fillColor);
        using var edge = new Pen(edgeColor, edgeWidth);
        g.FillPolygon(fill, points);
        g.DrawPolygon(edge, points);
    }

    private static PointF[] CutPanelPoints(RectangleF rect, float cut) =>
    [
        new(rect.X + cut, rect.Y), new(rect.Right - cut * 2, rect.Y),
        new(rect.Right, rect.Y + cut * 2), new(rect.Right, rect.Bottom - cut),
        new(rect.Right - cut, rect.Bottom), new(rect.X + cut * 2, rect.Bottom),
        new(rect.X, rect.Bottom - cut * 2), new(rect.X, rect.Y + cut)
    ];

    private void ResetHover()
    {
        ResetTutorialHover();
        _hoverMenu = -1;
        _hoverRunSetting = -1;
        _hoverRunStart = false;
        _hoverDrone = -1;
        _hoverPaintPart = -1;
        _hoverColor = -1;
        _hoverSetting = -1;
        _hoverProgressionTab = -1;
        _hoverProgressionRow = -1;
        _hoverProgressionToggle = false;
        _hoverShopCommand = -1;
        _hoverShopRow = -1;
        _hoverMissionDossier = false;
        _hoverMissionDossierClose = false;
        _hoverInventory = false;
        _hoverInventoryClose = false;
        _hoverInventoryUse = false;
        _hoverInventoryRow = -1;
        _hoverPause = -1;
        _hoverOverlay = -1;
        _onlineHover = -1;
        _hoverBack = false;
    }
}
