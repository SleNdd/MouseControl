using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;

namespace MouseControl.App.Services;

/// <summary>Desktop-only transient feedback. Construct, show and dispose on the UI thread.</summary>
internal sealed class DesktopFeedbackService : IDisposable
{
    private const int MaximumToasts = 4;
    private const double SlideSeconds = 0.22, VisibleSeconds = 2.6;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timer;
    private readonly nint _owner;
    private readonly DesktopFeedbackSound _sound = new();
    private readonly List<Toast> _toasts = [];
    private bool _disposed;

    public DesktopFeedbackService(DispatcherQueue dispatcher, nint ownerHwnd)
    {
        _dispatcher = dispatcher;
        _owner = ownerHwnd;
        VerifyThread();
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += Animate;
    }

    internal int ActiveToastCount => _toasts.Count;
    internal string? LatestToastStatus => _toasts.FirstOrDefault()?.Status;

    public void Show(string profileName, string colorHex, string status, bool playSound = false, bool showToast = true,
        double? verticalUnitsPerSecond = null)
    {
        VerifyThread();
        if (_disposed) return;
        if (playSound) PlaySound(status.Equals("On", StringComparison.OrdinalIgnoreCase)
            ? DesktopFeedbackKind.Enabled : status.Equals("Off", StringComparison.OrdinalIgnoreCase)
                ? DesktopFeedbackKind.Disabled : DesktopFeedbackKind.Adjustment);
        if (!showToast) return;

        var native = DesktopFeedbackNative.Instance;
        native.GetMonitor(_owner, out var monitor, out var work, out var scale);
        // Restart the stack on the current monitor rather than leave windows scattered
        // across displays after a cursor move or a display configuration change.
        if (_toasts.Count > 0 && (_toasts[0].Left != monitor.Left || _toasts[0].Top != work.Top
            || Math.Abs(_toasts[0].Scale - scale) > 0.01)) ClearToasts();
        var width = Math.Min((int)Math.Round(220 * scale), monitor.Right - monitor.Left);
        var height = (int)Math.Round(54 * scale);
        var gap = (int)Math.Round(6 * scale);
        var baseY = Math.Min(work.Top + (int)Math.Round(72 * scale), work.Bottom - height);
        var availableRows = Math.Max(1, (work.Bottom - baseY + gap) / (height + gap));
        var capacity = Math.Min(MaximumToasts, availableRows);
        while (_toasts.Count >= capacity) RemoveAt(_toasts.Count - 1);
        var toast = new Toast(profileName, FormatStatus(status, verticalUnitsPerSecond), ParseColor(colorHex), IsDark(), scale,
            monitor.Left, work.Top, baseY, width, height);
        toast.Handle = native.Create(toast, monitor.Left - width, baseY, width, height);
        if (toast.Handle == 0) return; // Notifications must never prevent the actual action.
        _toasts.Insert(0, toast);
        _timer.Start();
        Animate(_timer, null!);
    }

    internal static string FormatStatus(string status, double? verticalUnitsPerSecond) =>
        verticalUnitsPerSecond is { } vertical && double.IsFinite(vertical)
            && (status.Equals("On", StringComparison.OrdinalIgnoreCase) || status.Equals("Off", StringComparison.OrdinalIgnoreCase))
            ? $"{status} · {vertical.ToString("0.##", CultureInfo.InvariantCulture)} units/s" : status;

    public void PlaySound(DesktopFeedbackKind kind)
    {
        if (!_disposed) _sound.Play(kind);
    }

    private void Animate(DispatcherQueueTimer sender, object args)
    {
        var now = Stopwatch.GetTimestamp();
        for (var i = _toasts.Count - 1; i >= 0; i--)
        {
            var toast = _toasts[i];
            var elapsed = (now - toast.Created) / (double)Stopwatch.Frequency;
            if (elapsed >= VisibleSeconds + SlideSeconds) { RemoveAt(i); continue; }
            var progress = elapsed < SlideSeconds ? Ease(elapsed / SlideSeconds)
                : elapsed < VisibleSeconds ? 1 : 1 - Ease((elapsed - VisibleSeconds) / SlideSeconds);
            var x = toast.Left - (int)Math.Round(toast.Width * (1 - progress));
            var targetY = toast.BaseY + i * (toast.Height + (int)Math.Round(6 * toast.Scale));
            toast.Y += (targetY - toast.Y) * 0.25;
            DesktopFeedbackNative.Instance.Move(toast.Handle, x, (int)Math.Round(toast.Y));
        }
        if (_toasts.Count == 0) _timer.Stop();
    }

    private static double Ease(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);

    internal static uint ParseColor(string? hex)
    {
        var value = hex?.Trim().TrimStart('#');
        if (value?.Length == 8) value = value[2..];
        if (value?.Length != 6 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            rgb = 0x60A5FA;
        return ((rgb & 255) << 16) | (rgb & 0xFF00) | (rgb >> 16);
    }

    private static bool IsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (System.Security.SecurityException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private void RemoveAt(int index)
    {
        DesktopFeedbackNative.Instance.Destroy(_toasts[index].Handle);
        _toasts.RemoveAt(index);
    }

    private void ClearToasts()
    {
        while (_toasts.Count > 0) RemoveAt(_toasts.Count - 1);
    }

    private void VerifyThread()
    {
        if (!_dispatcher.HasThreadAccess) throw new InvalidOperationException("Desktop feedback must be accessed on its UI thread.");
    }

    public void Dispose()
    {
        VerifyThread();
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= Animate;
        ClearToasts();
        _sound.Dispose();
    }

    internal sealed class Toast(string name, string status, uint color, bool dark, double scale,
        int left, int top, int baseY, int width, int height)
    {
        internal readonly string Name = name, Status = status;
        internal readonly uint Color = color;
        internal readonly bool Dark = dark;
        internal readonly double Scale = scale;
        internal readonly int Left = left, Top = top, BaseY = baseY, Width = width, Height = height;
        internal readonly long Created = Stopwatch.GetTimestamp();
        internal double Y = baseY;
        internal nint Handle;
    }
}
