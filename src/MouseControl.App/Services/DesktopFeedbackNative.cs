using System.Runtime.InteropServices;
using static MouseControl.App.Services.DesktopFeedbackService;

namespace MouseControl.App.Services;

/// <summary>Isolated Win32 toast windows: layered, click-through, topmost and never activated.</summary>
internal sealed class DesktopFeedbackNative
{
    internal static readonly DesktopFeedbackNative Instance = new();
    private const string ClassName = "MouseControl.DesktopFeedback.v1";
    private const uint Transparent = 0x00FF00FF;
    private readonly WindowProc _procedure;
    private readonly Dictionary<nint, Toast> _windows = [];
    private readonly nint _module;
    private readonly bool _registered;

    private DesktopFeedbackNative()
    {
        _procedure = Procedure;
        _module = GetModuleHandle(null);
        var definition = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(), Instance = _module,
            Procedure = Marshal.GetFunctionPointerForDelegate(_procedure), ClassName = ClassName
        };
        _registered = RegisterClassEx(ref definition) != 0;
    }

    internal nint Create(Toast toast, int x, int y, int width, int height)
    {
        if (!_registered) return 0;
        // WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
        // WS_POPUP. SW_SHOWNOACTIVATE and SWP_NOACTIVATE keep focus in the target app.
        var hwnd = CreateWindowEx(0x08000000 | 0x80000 | 0x20 | 0x80, ClassName, $"{toast.Name} — {toast.Status}", 0x80000000,
            x, y, width, height, 0, 0, _module, 0);
        if (hwnd == 0) return 0;
        _windows.Add(hwnd, toast);
        if (!SetLayeredWindowAttributes(hwnd, Transparent, 232, 1 | 2))
        {
            Destroy(hwnd);
            return 0;
        }
        Clip(hwnd, width, width, height);
        InvalidateRect(hwnd, 0, false);
        SetWindowPos(hwnd, new nint(-1), x, y, width, height, 0x0010);
        ShowWindow(hwnd, 4);
        return hwnd;
    }

    internal void Move(nint hwnd, int x, int y)
    {
        if (_windows.TryGetValue(hwnd, out var toast))
            Clip(hwnd, Math.Max(0, toast.Left - x), toast.Width, toast.Height);
        SetWindowPos(hwnd, new nint(-1), x, y, 0, 0, 0x0001 | 0x0010);
    }

    private static void Clip(nint hwnd, int left, int width, int height)
    {
        // Clip the off-screen portion even when there is another monitor to the left.
        // A successful SetWindowRgn transfers ownership of this GDI region to Windows.
        var region = CreateRectRgn(left, 0, width, height);
        if (SetWindowRgn(hwnd, region, true) == 0) DeleteObject(region);
    }
    internal void Destroy(nint hwnd)
    {
        DestroyWindow(hwnd);
        _windows.Remove(hwnd);
    }

    internal void GetMonitor(nint owner, out Rect monitorRect, out Rect workRect, out double scale)
    {
        var monitor = GetCursorPos(out var cursor) ? MonitorFromPoint(cursor, 2) : MonitorFromWindow(owner, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            info.Monitor = new Rect { Right = GetSystemMetrics(0), Bottom = GetSystemMetrics(1) };
            info.Work = info.Monitor;
        }
        monitorRect = info.Monitor;
        workRect = info.Work;
        var dpi = GetDpiForWindow(owner);
        if (_registered && monitor != MonitorFromWindow(owner, 2))
        {
            // GetDpiForMonitor is not supported on per-monitor-aware threads. A hidden
            // one-pixel window obtains the target display's actual window DPI instead.
            var probe = CreateWindowEx(0x08000000 | 0x80, ClassName, "", 0x80000000,
                info.Work.Left + (info.Work.Right - info.Work.Left) / 2,
                info.Work.Top + (info.Work.Bottom - info.Work.Top) / 2, 1, 1, 0, 0, _module, 0);
            if (probe != 0)
            {
                dpi = GetDpiForWindow(probe);
                DestroyWindow(probe);
            }
        }
        scale = dpi > 0 ? dpi / 96.0 : 1;
    }

    private nint Procedure(nint hwnd, uint message, nuint wparam, nint lparam)
    {
        if (message == 0x84) return new nint(-1); // HTTRANSPARENT as an additional safeguard.
        if (message == 0x21) return new nint(3); // MA_NOACTIVATE.
        if (message == 0x14) return new nint(1); // Full background painted below.
        if (message == 0x0F && _windows.TryGetValue(hwnd, out var toast))
        {
            var dc = BeginPaint(hwnd, out var paint);
            try { if (dc != 0) Paint(dc, toast); }
            finally { EndPaint(hwnd, ref paint); }
            return 0;
        }
        return DefWindowProc(hwnd, message, wparam, lparam);
    }

    private static void Paint(nint dc, Toast toast)
    {
        int Px(double value) => (int)Math.Round(value * toast.Scale);
        var bounds = new Rect { Right = toast.Width, Bottom = toast.Height };
        var clear = CreateSolidBrush(Transparent);
        FillRect(dc, ref bounds, clear);
        DeleteObject(clear);
        // A narrow shadow contour provides depth without a blur dependency.
        // The whole layered window is gently translucent; this is not an acrylic backdrop.
        var shadow = CreateSolidBrush(toast.Dark ? 0x00151210u : 0x00DAD5CFu);
        var oldBrush = SelectObject(dc, shadow);
        var oldPen = SelectObject(dc, GetStockObject(8)); // NULL_PEN
        RoundRect(dc, -Px(12), Px(3), toast.Width, toast.Height, Px(16), Px(16));
        SelectObject(dc, oldBrush);
        SelectObject(dc, oldPen);
        DeleteObject(shadow);
        var baseColor = toast.Dark ? 0x00261D18u : 0x00FDFAF8u;
        var surface = BlendColor(baseColor, toast.Color, toast.Dark ? 0.10 : 0.05);
        var background = CreateSolidBrush(surface);
        var border = CreatePen(0, Math.Max(1, Px(1)), BlendColor(surface, toast.Color, toast.Dark ? 0.75 : 0.65));
        oldBrush = SelectObject(dc, background);
        oldPen = SelectObject(dc, border);
        // The card ends 3 DIP before the window bounds to leave a narrow shadow gutter.
        // Left corners lie outside the window so the card meets the screen edge squarely.
        RoundRect(dc, -Px(12), 0, toast.Width - Px(3), toast.Height - Px(3), Px(14), Px(14));
        SelectObject(dc, oldBrush);
        SelectObject(dc, oldPen);
        DeleteObject(background);
        DeleteObject(border);
        var accent = CreateSolidBrush(toast.Color);
        var stripe = new Rect { Left = 0, Top = Px(9), Right = Px(2), Bottom = toast.Height - Px(12) };
        FillRect(dc, ref stripe, accent);
        oldBrush = SelectObject(dc, accent);
        oldPen = SelectObject(dc, GetStockObject(8));
        Ellipse(dc, Px(15), Px(13), Px(21), Px(19));
        SelectObject(dc, oldBrush);
        SelectObject(dc, oldPen);
        DeleteObject(accent);
        SetBkMode(dc, 1);
        DrawLine(dc, toast.Name, new Rect { Left = Px(31), Top = Px(7), Right = toast.Width - Px(14), Bottom = Px(25) },
            Px(12), 600, toast.Dark ? 0x00F5F5F5u : 0x00222222u);
        DrawLine(dc, toast.Status, new Rect { Left = Px(31), Top = Px(27), Right = toast.Width - Px(14), Bottom = Px(44) },
            Px(11), 400, toast.Dark ? 0x00DDD1C8u : 0x0060544Du);
    }

    private static uint BlendColor(uint background, uint foreground, double amount)
    {
        uint result = 0;
        for (var shift = 0; shift <= 16; shift += 8)
            result |= (uint)Math.Round(((background >> shift) & 255) * (1 - amount)
                + ((foreground >> shift) & 255) * amount) << shift;
        return result;
    }

    private static void DrawLine(nint dc, string text, Rect rect, int height, int weight, uint color)
    {
        var font = CreateFont(-height, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var previous = SelectObject(dc, font);
        SetTextColor(dc, color);
        DrawText(dc, text, text.Length, ref rect, 0x20 | 0x4 | 0x8000 | 0x800); // single line, centered vertically, ellipsis, no prefix
        SelectObject(dc, previous);
        DeleteObject(font);
    }

    private delegate nint WindowProc(nint hwnd, uint message, nuint wparam, nint lparam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        internal uint Size, Style;
        internal nint Procedure;
        internal int ClassExtra, WindowExtra;
        internal nint Instance, Icon, Cursor, Background;
        internal string? MenuName, ClassName;
        internal nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { internal int Size; internal Rect Monitor, Work; internal uint Flags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        internal nint Dc;
        internal int Erase;
        internal Rect Paint;
        internal int Restore, IncUpdate;
        internal byte R0, R1, R2, R3, R4, R5, R6, R7, R8, R9, R10, R11, R12, R13, R14, R15,
            R16, R17, R18, R19, R20, R21, R22, R23, R24, R25, R26, R27, R28, R29, R30, R31;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WindowClass definition);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint extended, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint hwnd, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern nint BeginPaint(nint hwnd, out PaintStruct paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(nint hwnd, ref PaintStruct paint);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("gdi32.dll")] private static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern nint CreatePen(int style, int width, uint color);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int index);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool RoundRect(nint dc, int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool Ellipse(nint dc, int left, int top, int right, int bottom);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref Rect rect, nint brush);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(nint dc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrecision, uint clipPrecision, uint quality, uint pitch, string face);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawText(nint dc, string text, int count, ref Rect rect, uint format);
}
