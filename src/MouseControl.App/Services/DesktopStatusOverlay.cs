using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace MouseControl.App.Services;

/// <summary>A persistent, click-through status label on the primary display.</summary>
internal sealed class DesktopStatusOverlay : IDisposable
{
    private const uint TransparentColor = 0x00FF00FF;
    private readonly DispatcherQueue _dispatcher;
    private readonly WindowProc _procedure;
    private readonly string _className = $"MouseControl.DesktopStatus.{Guid.NewGuid():N}";
    private readonly nint _module;
    private bool _registered, _disposed;
    private nint _window, _font;
    private int _width = 1, _height = 1;

    public DesktopStatusOverlay(DispatcherQueue dispatcher, nint ownerHwnd)
    {
        _dispatcher = dispatcher;
        VerifyThread();
        _procedure = Procedure;
        _module = GetModuleHandle(null);
        var definition = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(), Instance = _module,
            Procedure = Marshal.GetFunctionPointerForDelegate(_procedure), ClassName = _className
        };
        _registered = RegisterClassEx(ref definition) != 0;
        // Deliberately unowned: minimizing the settings window must not hide the label.
        _ = ownerHwnd;
    }

    internal nint WindowHandle => _window;
    internal string Text { get; private set; } = "";
    internal bool Visible { get; private set; }

    internal static string FormatText(bool enabled, double value) =>
        $"{(enabled ? "on" : "off")} {value.ToString("0.##", CultureInfo.InvariantCulture)}";

    public void Update(bool visible, bool enabled, double verticalUnitsPerSecond)
    {
        VerifyThread();
        if (_disposed) return;
        Text = FormatText(enabled, verticalUnitsPerSecond);
        if (!visible)
        {
            if (_window != 0) ShowWindow(_window, 0);
            Visible = false;
            return;
        }

        var monitor = MonitorFromPoint(default, 1); // (0, 0) belongs to the primary display.
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            Hide();
            return;
        }
        if (_window == 0 && !Create(info.Monitor.Left, info.Monitor.Top)) return;

        // Obtain the target display DPI from the actual window, including after a display change.
        if (!SetWindowPos(_window, new nint(-1), info.Monitor.Left, info.Monitor.Top,
                0, 0, 0x0001 | 0x0010))
        {
            Hide();
            return;
        }
        var dpi = GetDpiForWindow(_window);
        var scale = (dpi == 0 ? 96 : dpi) / 96.0;
        var font = CreateFont(-Math.Max(1, (int)Math.Round(9 * scale)), 0, 0, 0, 400,
            0, 0, 0, 1, 0, 0, 3, 0, "Segoe UI"); // Non-antialiased edges preserve the color key.
        if (font == 0)
        {
            Hide();
            return;
        }
        if (_font != 0) DeleteObject(_font);
        _font = font;
        var dc = GetDC(_window);
        if (dc == 0)
        {
            Hide();
            return;
        }
        try
        {
            var previous = SelectObject(dc, _font);
            try
            {
                var measured = new Rect();
                DrawText(dc, Text, Text.Length, ref measured, 0x20 | 0x400 | 0x800);
                _width = Math.Max(1, measured.Right - measured.Left + 2);
                _height = Math.Max(1, measured.Bottom - measured.Top + 2);
            }
            finally { SelectObject(dc, previous); }
        }
        finally { ReleaseDC(_window, dc); }

        var margin = (int)Math.Round(2 * scale);
        if (!SetWindowPos(_window, new nint(-1), info.Monitor.Left + margin,
                info.Monitor.Top + margin, _width, _height, 0x0010))
        {
            Hide();
            return;
        }
        InvalidateRect(_window, 0, false);
        ShowWindow(_window, 4); // SW_SHOWNOACTIVATE
        Visible = true;
    }

    private bool Create(int x, int y)
    {
        if (!_registered) return false;
        // WS_EX_TOPMOST | TOOLWINDOW | NOACTIVATE | LAYERED | TRANSPARENT, WS_POPUP.
        _window = CreateWindowEx(0x8 | 0x80 | 0x08000000 | 0x80000 | 0x20,
            _className, "MouseControl status", 0x80000000, x, y, 1, 1, 0, 0, _module, 0);
        if (_window == 0) return false;
        if (SetLayeredWindowAttributes(_window, TransparentColor, 51, 1 | 2)) return true;
        DestroyWindow(_window);
        _window = 0;
        return false;
    }

    private void Hide()
    {
        ShowWindow(_window, 0);
        Visible = false;
    }

    private nint Procedure(nint hwnd, uint message, nuint wparam, nint lparam)
    {
        // No managed exception may escape a native window procedure.
        try
        {
            if (message == 0x84) return new nint(-1); // HTTRANSPARENT
            if (message == 0x21) return new nint(3); // MA_NOACTIVATE
            if (message == 0x14) return new nint(1);
            if (message == 0x0F)
            {
                var dc = BeginPaint(hwnd, out var paint);
                try { if (dc != 0) Paint(dc); }
                finally { EndPaint(hwnd, ref paint); }
                return 0;
            }
            return DefWindowProc(hwnd, message, wparam, lparam);
        }
        catch (Exception error)
        {
            Debug.WriteLine($"Desktop status paint failed: {error}");
            return 0;
        }
    }

    private void Paint(nint dc)
    {
        var bounds = new Rect { Right = _width, Bottom = _height };
        var brush = CreateSolidBrush(TransparentColor);
        if (brush == 0) return;
        try { FillRect(dc, ref bounds, brush); }
        finally { DeleteObject(brush); }
        if (_font == 0) return;
        var previous = SelectObject(dc, _font);
        try
        {
            SetBkMode(dc, 1);
            SetTextColor(dc, 0x00202020);
            // A one-pixel dark outline; only glyph pixels remain after color-key compositing.
            for (var y = 0; y <= 2; y++)
                for (var x = 0; x <= 2; x++)
                    if (x != 1 || y != 1) DrawAt(dc, x, y);
            SetTextColor(dc, 0x00F5F5F5);
            DrawAt(dc, 1, 1);
        }
        finally { SelectObject(dc, previous); }
    }

    private void DrawAt(nint dc, int x, int y)
    {
        var bounds = new Rect { Left = x, Top = y, Right = _width + x, Bottom = _height + y };
        DrawText(dc, Text, Text.Length, ref bounds, 0x20 | 0x100 | 0x800);
    }

    private void VerifyThread()
    {
        if (!_dispatcher.HasThreadAccess)
            throw new InvalidOperationException("Desktop status must be accessed on its UI thread.");
    }

    public void Dispose()
    {
        VerifyThread();
        if (_disposed) return;
        if (_window != 0 && !DestroyWindow(_window))
            throw new InvalidOperationException("Could not destroy the desktop status window.");
        _window = 0;
        Visible = false;
        if (_font != 0) DeleteObject(_font);
        _font = 0;
        if (_registered) UnregisterClass(_className, _module);
        _registered = false;
        _disposed = true;
        GC.KeepAlive(_procedure);
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
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint extended, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint hwnd, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern nint BeginPaint(nint hwnd, out PaintStruct paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(nint hwnd, ref PaintStruct paint);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref Rect rect, nint brush);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(nint dc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrecision, uint clipPrecision, uint quality, uint pitch, string face);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawText(nint dc, string text, int count, ref Rect rect, uint format);
}
