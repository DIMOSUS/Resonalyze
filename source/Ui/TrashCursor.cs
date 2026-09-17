using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Resonalyze.Ui;

/// <summary>Drawn in the app palette; built via <c>CreateIconIndirect</c>, the only way to put the hotspot in the middle of the bin.</summary>
internal static class TrashCursor
{
    private const int Size = 32;

    private static Cursor? cursor;

    /// <summary>Never disposed: process lifetime, and a handle-built <see cref="Cursor"/> does not own it.</summary>
    public static Cursor Instance => cursor ??= Create();

    private static Cursor Create()
    {
        using var bitmap = new Bitmap(Size, Size);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Draw(graphics);
        }

        return FromBitmap(bitmap, new Point(Size / 2, Size / 2));
    }

    // Dark contour under the strokes: the strip can be dragged over any desktop background.
    private static void Draw(Graphics graphics)
    {
        using var body = new GraphicsPath();
        body.AddLine(9f, 12f, 10.5f, 26f);
        body.AddArc(10.5f, 24f, 11f, 5f, 180f, -180f);
        body.AddLine(23f, 12f, 9f, 12f);

        using var lid = new GraphicsPath();
        lid.AddLine(6f, 9.5f, 26f, 9.5f);

        using var handle = new GraphicsPath();
        handle.AddLine(13f, 6f, 13f, 9.5f);
        handle.AddLine(13f, 6f, 19f, 6f);
        handle.AddLine(19f, 6f, 19f, 9.5f);

        using (var contour = RoundedPen(Color.FromArgb(170, UiPalette.CursorOutline), 3.4f))
        {
            graphics.DrawPath(contour, body);
            graphics.DrawPath(contour, lid);
            graphics.DrawPath(contour, handle);
        }

        using (var stroke = RoundedPen(UiPalette.Error, 1.9f))
        {
            graphics.DrawPath(stroke, body);
            graphics.DrawPath(stroke, lid);
            graphics.DrawPath(stroke, handle);
        }

        using var ribs = RoundedPen(Color.FromArgb(190, UiPalette.Error), 1.4f);
        graphics.DrawLine(ribs, 13.5f, 15f, 13.5f, 23f);
        graphics.DrawLine(ribs, 18.5f, 15f, 18.5f, 23f);
    }

    private static Pen RoundedPen(Color color, float width) => new(color, width)
    {
        LineJoin = LineJoin.Round,
        StartCap = LineCap.Round,
        EndCap = LineCap.Round
    };

    private static Cursor FromBitmap(Bitmap bitmap, Point hotspot)
    {
        IntPtr icon = bitmap.GetHicon();
        try
        {
            if (!GetIconInfo(icon, out IconInfo info))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                info.IsIcon = false;
                info.HotspotX = hotspot.X;
                info.HotspotY = hotspot.Y;
                IntPtr handle = CreateIconIndirect(ref info);
                if (handle == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                return new Cursor(handle);
            }
            finally
            {
                // GetIconInfo returns copies of both bitmaps; they are ours to free.
                DeleteObject(info.MaskBitmap);
                DeleteObject(info.ColorBitmap);
            }
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public bool IsIcon;
        public int HotspotX;
        public int HotspotY;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconIndirect(ref IconInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);
}
