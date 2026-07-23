using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Dust;

/// <summary>A small code-drawn equipment cursor with an explicit pixel hotspot.</summary>
internal sealed class LabCursor : IDisposable
{
    private IntPtr _handle;
    public Cursor Cursor { get; }

    private LabCursor(IntPtr handle)
    {
        _handle = handle;
        Cursor = new Cursor(handle);
    }

    public static LabCursor Create(bool active)
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.None;
            using var shadow = new SolidBrush(Color.FromArgb(230, 4, 8, 8));
            using var body = new SolidBrush(active
                ? Color.FromArgb(220, 151, 72)
                : Color.FromArgb(211, 208, 166));
            using var joint = new SolidBrush(active
                ? Color.FromArgb(177, 48, 43)
                : Color.FromArgb(82, 94, 79));

            var outline = new Point[]
            {
                new(2, 1), new(5, 1), new(5, 5), new(9, 5), new(9, 9),
                new(13, 9), new(13, 13), new(17, 13), new(17, 17),
                new(13, 17), new(13, 15), new(10, 15), new(14, 24),
                new(10, 26), new(6, 17), new(4, 20), new(2, 20)
            };
            g.FillPolygon(shadow, outline);
            g.FillRectangle(body, 3, 2, 2, 16);
            g.FillRectangle(body, 5, 6, 3, 10);
            g.FillRectangle(body, 8, 10, 3, 5);
            g.FillRectangle(body, 11, 14, 4, 2);
            g.FillRectangle(joint, 4, 3, 2, 2);
            if (active)
            {
                g.FillRectangle(body, 20, 4, 8, 2);
                g.FillRectangle(body, 23, 1, 2, 8);
                g.FillRectangle(joint, 23, 4, 2, 2);
            }
        }

        var iconHandle = bitmap.GetHicon();
        try
        {
            if (!GetIconInfo(iconHandle, out var info)) throw new InvalidOperationException("Cursor conversion failed.");
            try
            {
                info.IsIcon = false;
                info.HotspotX = 3;
                info.HotspotY = 2;
                var cursorHandle = CreateIconIndirect(ref info);
                if (cursorHandle == IntPtr.Zero) throw new InvalidOperationException("Cursor creation failed.");
                return new LabCursor(cursorHandle);
            }
            finally
            {
                if (info.MaskBitmap != IntPtr.Zero) DeleteObject(info.MaskBitmap);
                if (info.ColorBitmap != IntPtr.Zero) DeleteObject(info.ColorBitmap);
            }
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    public void Dispose()
    {
        Cursor.Dispose();
        if (_handle == IntPtr.Zero) return;
        DestroyCursor(_handle);
        _handle = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public int HotspotX;
        public int HotspotY;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref IconInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyCursor(IntPtr cursor);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr graphicObject);
}
