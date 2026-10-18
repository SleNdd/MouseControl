using System.Runtime.InteropServices;
using MouseControl.App.Services;

namespace MouseControl.App;

public sealed partial class MainWindow
{
    internal bool MaskTrayAvailable => _bridge.TrayAvailable;
    internal nint MaskTrayIcon => _bridge.TrayIconHandle;

    internal object VerifySpreadControls()
    {
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        Require(_smoke, "Spread verification requires a simulated backend.");
        var original = Current;
        var originalArduinoVisibility = ArduinoStatus.Visibility;
        try
        {
            ReplaceProfile(Current with { Motion = Current.Motion with
            {
                X = .2, Y = .2, Humanization = .05, Model = MouseControl.Core.MotionModel.Bezier,
                RandomizeOnActivation = false, VerticalSpread = 15, HorizontalSpread = 15, HumanizationSpread = 10
            } });
            Require(VerticalSpreadPanel.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed, "Spread fields appear while disabled.");
            RandomizeCheck.IsChecked = true;
            Require(Current.Motion.RandomizeOnActivation && VerticalSpreadPanel.Visibility == Microsoft.UI.Xaml.Visibility.Visible
                && HorizontalSpreadPanel.Visibility == Microsoft.UI.Xaml.Visibility.Visible && HumanSpreadPanel.Visibility == Microsoft.UI.Xaml.Visibility.Visible,
                "The checkbox did not reveal all spread fields.");
            Require(VerticalNumber.Value == 400 && HorizontalNumber.Value == 400 && HumanNumber.Value == 5,
                "Enabling spread changed the saved base values.");
            Require(VerticalSpreadNumber.Value == 15 && HorizontalSpreadNumber.Value == 15 && HumanSpreadNumber.Value == 10,
                "The default spread values are incorrect.");
            VerticalSpreadNumber.Value = 25; HorizontalSpreadNumber.Value = 30; HumanSpreadNumber.Value = 12;
            Require(Current.Motion.VerticalSpread == 25 && Current.Motion.HorizontalSpread == 30 && Current.Motion.HumanizationSpread == 12,
                "Editing spread fields did not update the profile.");
            RandomizeCheck.IsChecked = false;
            Require(!Current.Motion.RandomizeOnActivation && VerticalSpreadPanel.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed
                && VerticalNumber.Value == 400 && HumanNumber.Value == 5, "Disabling spread did not restore the exact-value controls.");
            RandomizeCheck.IsChecked = true;
            Require(VerticalSpreadNumber.Value == 25 && HorizontalSpreadNumber.Value == 30 && HumanSpreadNumber.Value == 12,
                "Toggling spread discarded custom widths.");
            ArduinoStatus.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            Root.UpdateLayout();
            double Left(Microsoft.UI.Xaml.FrameworkElement element) => element.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point()).X;
            Require(VerticalSpreadNumber.ActualWidth >= 70 && HumanSpreadNumber.ActualWidth >= 70
                && Left(VerticalNumber) + VerticalNumber.ActualWidth <= Left(VerticalSpreadNumber)
                && Left(VerticalSpreadNumber) + VerticalSpreadNumber.ActualWidth < Left(PadButton), "Spread fields overlap or are clipped.");
            Require(RandomizeCheck.ActualWidth > 0 && Left(CorrectionEffectCheck) + CorrectionEffectCheck.ActualWidth <= Left(RandomizeCheck)
                && Left(ArduinoStatus) + ArduinoStatus.ActualWidth <= Left(CreditLink), "The effects row overlaps its adjacent controls.");
            return new { Success = true, VerticalDefault = "400 ±15", HumanizationDefault = "5 +10", CustomSpreadsPreserved = true, LayoutFits = true };
        }
        finally { ReplaceProfile(original); ArduinoStatus.Visibility = originalArduinoVisibility; }
    }

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
