namespace Dust;

internal enum LabTextAlign { Left, Center, Right }

/// <summary>
/// A deliberately severe 5x7 equipment font. Every visible game label is drawn
/// from these cells, keeping the interface independent of installed system fonts.
/// </summary>
internal static class LabFont
{
    private static readonly IReadOnlyDictionary<char, byte[]> Glyphs = new Dictionary<char, byte[]>
    {
        [' '] = [0, 0, 0, 0, 0, 0, 0],
        ['A'] = [14, 17, 17, 31, 17, 17, 17],
        ['B'] = [30, 17, 17, 30, 17, 17, 30],
        ['C'] = [15, 16, 16, 16, 16, 16, 15],
        ['D'] = [30, 17, 17, 17, 17, 17, 30],
        ['E'] = [31, 16, 16, 30, 16, 16, 31],
        ['F'] = [31, 16, 16, 30, 16, 16, 16],
        ['G'] = [15, 16, 16, 23, 17, 17, 15],
        ['H'] = [17, 17, 17, 31, 17, 17, 17],
        ['I'] = [31, 4, 4, 4, 4, 4, 31],
        ['J'] = [7, 2, 2, 2, 18, 18, 12],
        ['K'] = [17, 18, 20, 24, 20, 18, 17],
        ['L'] = [16, 16, 16, 16, 16, 16, 31],
        ['M'] = [17, 27, 21, 21, 17, 17, 17],
        ['N'] = [17, 25, 21, 19, 17, 17, 17],
        ['O'] = [14, 17, 17, 17, 17, 17, 14],
        ['P'] = [30, 17, 17, 30, 16, 16, 16],
        ['Q'] = [14, 17, 17, 17, 21, 18, 13],
        ['R'] = [30, 17, 17, 30, 20, 18, 17],
        ['S'] = [15, 16, 16, 14, 1, 1, 30],
        ['T'] = [31, 4, 4, 4, 4, 4, 4],
        ['U'] = [17, 17, 17, 17, 17, 17, 14],
        ['V'] = [17, 17, 17, 17, 17, 10, 4],
        ['W'] = [17, 17, 17, 21, 21, 21, 10],
        ['X'] = [17, 17, 10, 4, 10, 17, 17],
        ['Y'] = [17, 17, 10, 4, 4, 4, 4],
        ['Z'] = [31, 1, 2, 4, 8, 16, 31],
        ['0'] = [14, 17, 19, 21, 25, 17, 14],
        ['1'] = [4, 12, 4, 4, 4, 4, 14],
        ['2'] = [14, 17, 1, 2, 4, 8, 31],
        ['3'] = [30, 1, 1, 14, 1, 1, 30],
        ['4'] = [2, 6, 10, 18, 31, 2, 2],
        ['5'] = [31, 16, 16, 30, 1, 1, 30],
        ['6'] = [14, 16, 16, 30, 17, 17, 14],
        ['7'] = [31, 1, 2, 4, 8, 8, 8],
        ['8'] = [14, 17, 17, 14, 17, 17, 14],
        ['9'] = [14, 17, 17, 15, 1, 1, 14],
        ['.'] = [0, 0, 0, 0, 0, 12, 12],
        [','] = [0, 0, 0, 0, 0, 12, 8],
        [':'] = [0, 12, 12, 0, 12, 12, 0],
        [';'] = [0, 12, 12, 0, 12, 8, 0],
        ['!'] = [4, 4, 4, 4, 4, 0, 4],
        ['?'] = [14, 17, 1, 2, 4, 0, 4],
        ['-'] = [0, 0, 0, 31, 0, 0, 0],
        ['_'] = [0, 0, 0, 0, 0, 0, 31],
        ['/'] = [1, 1, 2, 4, 8, 16, 16],
        ['\\'] = [16, 16, 8, 4, 2, 1, 1],
        ['+'] = [0, 4, 4, 31, 4, 4, 0],
        ['='] = [0, 0, 31, 0, 31, 0, 0],
        ['['] = [14, 8, 8, 8, 8, 8, 14],
        [']'] = [14, 2, 2, 2, 2, 2, 14],
        ['('] = [2, 4, 8, 8, 8, 4, 2],
        [')'] = [8, 4, 2, 2, 2, 4, 8],
        ['<'] = [2, 4, 8, 16, 8, 4, 2],
        ['>'] = [8, 4, 2, 1, 2, 4, 8],
        ['|'] = [4, 4, 4, 4, 4, 4, 4],
        ['#'] = [10, 31, 10, 10, 31, 10, 0],
        ['*'] = [0, 21, 14, 31, 14, 21, 0],
        ['%'] = [25, 25, 2, 4, 8, 19, 19],
        ['^'] = [4, 10, 17, 0, 0, 0, 0],
    };

    public static Size Measure(string text, int scale, int tracking = 1, int lineGap = 2)
    {
        scale = Math.Max(2, scale);
        var lines = text.Replace("\r", string.Empty).Split('\n');
        var cell = 5 * scale;
        var maxWidth = lines.Max(line => line.Length == 0 ? 0 : line.Length * cell + (line.Length - 1) * tracking * scale);
        var height = lines.Length * 7 * scale + Math.Max(0, lines.Length - 1) * lineGap * scale;
        return new Size(maxWidth, height);
    }

    public static void Draw(Graphics g, string text, float x, float y, int scale, Color color,
        LabTextAlign align = LabTextAlign.Left, int tracking = 1, int lineGap = 2)
    {
        if (scale < 1 || string.IsNullOrEmpty(text)) return;
        scale = Math.Max(2, scale);
        using var brush = new SolidBrush(color);
        var lines = text.ToUpperInvariant().Replace("\r", string.Empty).Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var width = Measure(line, scale, tracking, lineGap).Width;
            var lineX = align switch
            {
                LabTextAlign.Center => x - width / 2f,
                LabTextAlign.Right => x - width,
                _ => x
            };
            var lineY = y + lineIndex * (7 + lineGap) * scale;
            for (var index = 0; index < line.Length; index++)
            {
                var glyph = Glyphs.TryGetValue(line[index], out var found) ? found : Glyphs['?'];
                var glyphX = lineX + index * (5 + tracking) * scale;
                for (var row = 0; row < 7; row++)
                for (var column = 0; column < 5; column++)
                    if ((glyph[row] & (1 << (4 - column))) != 0)
                        g.FillRectangle(brush, glyphX + column * scale, lineY + row * scale, scale, scale);
            }
        }
    }

    public static void DrawShadowed(Graphics g, string text, float x, float y, int scale, Color color,
        Color shadow, LabTextAlign align = LabTextAlign.Left, int tracking = 1)
    {
        Draw(g, text, x + scale, y + scale, scale, shadow, align, tracking);
        Draw(g, text, x, y, scale, color, align, tracking);
    }
}
