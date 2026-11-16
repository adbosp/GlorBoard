using System;
using System.Runtime.InteropServices;

namespace GlorBoard;

internal sealed class DesktopPatch : IDisposable
{
    private const uint SourceWithLayers = 0x40CC0020;
    private const uint ExcludeWindow = 0x11;
    private nint dc;
    private nint bitmap;
    private nint previousBitmap;

    internal int Width { get; }
    internal int Height { get; }
    internal nint Pixels { get; }

    internal DesktopPatch(int width, int height)
    {
        Width = width;
        Height = height;
        dc = CreateCompatibleDC(0);
        if (dc == 0) return;
        BitmapHeader header = new()
        {
            Size = (uint)Marshal.SizeOf<BitmapHeader>(),
            Width = width,
            Height = -height,
            Planes = 1,
            Bits = 32,
        };
        bitmap = CreateDIBSection(dc, ref header, 0, out nint pixels, 0, 0);
        if (bitmap == 0) return;
        previousBitmap = SelectObject(dc, bitmap);
        if (previousBitmap != 0 && previousBitmap != -1) Pixels = pixels;
    }

    internal bool ReadBehind(nint window, int x, int y)
    {
        if (Pixels == 0) return false;
        // The getter may fail for a regular non-layered HWND, even though the setter works.
        if (GetWindowDisplayAffinity(window, out uint original) && original != 0) return false;
        if (!SetWindowDisplayAffinity(window, ExcludeWindow)) return false;

        nint screen = 0;
        bool copied = false;
        bool restored;
        try
        {
            screen = GetDC(0);
            if (screen != 0)
                copied = BitBlt(dc, 0, 0, Width, Height, screen, x, y, SourceWithLayers);
        }
        finally
        {
            if (screen != 0) _ = ReleaseDC(0, screen);
            // Only exclude this window for the synchronous read. Recording works normally between samples.
            restored = SetWindowDisplayAffinity(window, 0);
        }
        return restored && copied;
    }

    public void Dispose()
    {
        if (dc != 0 && previousBitmap != 0 && previousBitmap != -1) _ = SelectObject(dc, previousBitmap);
        if (bitmap != 0) _ = DeleteObject(bitmap);
        if (dc != 0) _ = DeleteDC(dc);
        dc = bitmap = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        internal uint Size;
        internal int Width, Height;
        internal ushort Planes, Bits;
        internal uint Compression, ImageSize;
        internal int XPelsPerMeter, YPelsPerMeter;
        internal uint ColorsUsed, ImportantColors;
    }

    [DllImport("user32")] private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
    [DllImport("user32")] private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    [DllImport("user32")] private static extern nint GetDC(nint window);
    [DllImport("user32")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32")] private static extern nint CreateDIBSection(nint dc, ref BitmapHeader header, uint usage, out nint pixels, nint section, uint offset);
    [DllImport("gdi32")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32")] private static extern bool BitBlt(nint output, int x, int y, int width, int height, nint source, int left, int top, uint operation);
}
