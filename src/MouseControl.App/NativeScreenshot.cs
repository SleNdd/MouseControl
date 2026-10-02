#if DEBUG
using System.Runtime.InteropServices;

namespace MouseControl.App;

internal static class NativeScreenshot
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, Bits;
        public uint Compression, ImageSize; public int XResolution, YResolution; public uint Used, Important;
    }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sx, int sy, uint operation);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[] bits, ref BitmapInfo info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);

    internal static void RaiseForCapture(nint hwnd) => SetWindowPos(hwnd, (nint)(-1), 0, 0, 0, 0, 0x0043);
    internal static void RestoreAfterCapture(nint hwnd) => SetWindowPos(hwnd, (nint)(-2), 0, 0, 0, 0, 0x0003);

    internal static (byte[] Pixels, int Width, int Height) Capture(nint hwnd)
    {
        if (!GetWindowRect(hwnd, out var rect)) throw new InvalidOperationException("Window bounds unavailable.");
        var width = rect.Right - rect.Left; var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0 || width > 5000 || height > 5000) throw new InvalidOperationException("Invalid window bounds.");
        var screen = GetDC(0); var memory = CreateCompatibleDC(screen); var bitmap = CreateCompatibleBitmap(screen, width, height);
        nint previous = 0;
        try
        {
            if (screen == 0 || memory == 0 || bitmap == 0) throw new InvalidOperationException("Capture resources unavailable.");
            previous = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, width, height, screen, rect.Left, rect.Top, 0x00CC0020)) throw new InvalidOperationException("Window capture failed.");
            SelectObject(memory, previous); previous = 0;
            var info = new BitmapInfo { Size = (uint)Marshal.SizeOf<BitmapInfo>(), Width = width, Height = -height, Planes = 1, Bits = 32 };
            var pixels = new byte[checked(width * height * 4)];
            if (GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0) != height) throw new InvalidOperationException("Pixel read failed.");
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return (pixels, width, height);
        }
        finally
        {
            if (previous != 0) SelectObject(memory, previous);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            if (screen != 0) ReleaseDC(0, screen);
        }
    }
}
#endif
