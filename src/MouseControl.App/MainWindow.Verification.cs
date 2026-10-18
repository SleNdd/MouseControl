using System.Runtime.InteropServices;
using MouseControl.App.Services;

namespace MouseControl.App;

public sealed partial class MainWindow
{
    internal bool MaskTrayAvailable => _bridge.TrayAvailable;
    internal nint MaskTrayIcon => _bridge.TrayIconHandle;

    // Runs only with --verify-launch, which uses temporary profiles and a simulated backend.
    internal object VerifyDesktopFeatures()
    {
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        Require(_smoke, "Desktop verification requires a simulated backend.");
        Require(!_configuration.AppSettings.PersistentStatusOverlay, "The overlay should default to disabled.");
        Require(DesktopFeedbackService.FormatStatus("On", 450) == "On · 450 units/s", "On notification lacks its vertical value.");
        Require(DesktopFeedbackService.FormatStatus("Off", -350) == "Off · -350 units/s", "Off notification lost the signed vertical value.");
        Require(DesktopFeedbackService.FormatStatus("Vertical 50 units/s", 50) == "Vertical 50 units/s", "Adjustment notification was changed.");
        _feedback ??= new DesktopFeedbackService(DispatcherQueue, WinRT.Interop.WindowNative.GetWindowHandle(this));
        _feedback.Show("Verification", "#60A5FA", "On", false, true, 450);
        Require(_feedback.ActiveToastCount > 0 && _feedback.LatestToastStatus == "On · 450 units/s", "The native toast did not receive the vertical value.");

        _configuration = _configuration with { AppSettings = _configuration.AppSettings with { PersistentStatusOverlay = true } };
        SetVector(Current.Motion.X, 350 / MotionUi.MaximumSpeed);
        Require(_statusOverlay is { Visible: true, Text: "off 350" }, "The initial disabled indicator is incorrect.");
        var handle = _statusOverlay!.WindowHandle;
        const long expectedStyles = 0x080800A8; // TOPMOST | TOOLWINDOW | NOACTIVATE | LAYERED | TRANSPARENT.
        Require((VerificationNative.GetWindowLongPtr(handle, -20).ToInt64() & expectedStyles) == expectedStyles, "The indicator can interfere with focus or mouse input.");
        Require(VerificationNative.SendMessage(handle, 0x84, 0, 0) == -1 && VerificationNative.SendMessage(handle, 0x21, 0, 0) == 3, "The indicator intercepts native input.");
        Require(VerificationNative.GetLayeredWindowAttributes(handle, out var key, out var alpha, out var flags)
            && key == 0xFF00FF && alpha == 51 && flags == 3, "The indicator transparency is incorrect.");
        Require(VerificationNative.GetWindowRect(handle, out var bounds) && bounds.Left is >= 1 and <= 10 && bounds.Top is >= 1 and <= 10, "The indicator is not in the primary screen corner.");
        _runtime.SetEnabled(true); RefreshStatus();
        Require(_statusOverlay.Text == "on 350", "The indicator did not react to enabling.");
        SetVector(Current.Motion.X, 500 / MotionUi.MaximumSpeed);
        Require(_statusOverlay.Text == "on 500", "The indicator did not react to a changed vertical value.");
        AddProfile(Current);
        SetVector(Current.Motion.X, -50 / MotionUi.MaximumSpeed);
        _runtime.SetEnabled(false); RefreshStatus();
        Require(_statusOverlay.Text == "off -50", "The indicator did not track the active profile and disabling.");
        _bridge.Hide(); OnHidden();
        Require(VerificationNative.IsWindowVisible(handle), "The indicator disappeared when the app was hidden in the tray.");
        _configuration = _configuration with { AppSettings = _configuration.AppSettings with { PersistentStatusOverlay = false } };
        RefreshStatus();
        Require(!_statusOverlay.Visible && !VerificationNative.IsWindowVisible(handle), "The indicator remains after its setting is disabled.");
        return new { Success = true, Toast = "On · 450 units/s", EnabledText = "on 500", DisabledText = "off -50", Opacity = alpha / 255.0, ClickThrough = true, IndependentOfTray = true };
    }

    private static class VerificationNative
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
        [DllImport("user32.dll")] internal static extern nint SendMessage(nint hwnd, uint message, nint wparam, nint lparam);
        [DllImport("user32.dll")] internal static extern bool GetLayeredWindowAttributes(nint hwnd, out uint key, out byte alpha, out uint flags);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    }
}
