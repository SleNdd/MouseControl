using MouseControl.Masking;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;

namespace MouseControl.App;

public partial class App : Application
{
    private MainWindow? _window;
    private static Mutex? _instance;
    public App() => InitializeComponent();

    internal sealed record WindowMask(string ProcessName, string IconPath);
    internal static WindowMask? Mask { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var verify = Environment.GetCommandLineArgs().Contains("--verify-launch");
        bool smoke = verify;
#if DEBUG
        smoke |= Environment.GetCommandLineArgs().Contains("--smoke-test") || Environment.GetCommandLineArgs().Contains("--ui-check");
#endif
        _instance = new Mutex(true, smoke ? "SleNdd.MouseControl.Check." + Guid.NewGuid().ToString("N") : "SleNdd.MouseControl", out bool first);
        if (!first)
        {
            Native.PostMessage((nint)0xffff, Native.RegisterWindowMessage("SleNdd.MouseControl.ShowWindow"), 0, 0);
            Exit(); return;
        }
        try
        {
            var name = Environment.GetEnvironmentVariable("MOUSECONTROL_PROCESS_NAME");
            var icon = Environment.GetEnvironmentVariable("MOUSECONTROL_ICON_PATH");
            if (name is not null && ProcessMasker.IsUsableName(name) && icon is not null && File.Exists(icon))
                Mask = new WindowMask(name, icon);
            _window = new MainWindow(smoke);
            _window.Title = Mask?.ProcessName ?? "MouseControl";
            if (Mask is { } mask) _window.AppWindow.SetIcon(mask.IconPath);
            _window.Activate();
            if (verify) VerifyLaunch();
            else _window.ApplyStartupVisibility();
        }
        catch (Exception ex)
        {
            if (verify) WriteVerification(new { Success = false, Error = ex.ToString() });
            else Native.MessageBox(0, ex.Message, "MouseControl could not start", 0x10);
            Exit();
        }
    }

    private void VerifyLaunch()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window!);
                var smallIcon = SendMessage(hwnd, 0x7F, 0, 0);
                var bigIcon = SendMessage(hwnd, 0x7F, 1, 0);
                var processName = Process.GetCurrentProcess().ProcessName;
                var desktopFeatures = _window!.VerifyDesktopFeatures();
                var success = _window!.Title == (Mask?.ProcessName ?? "MouseControl")
                    && (Mask is null || processName == Mask.ProcessName)
                    && (Mask is null || (smallIcon != 0 && bigIcon != 0))
                    && _window.MaskTrayAvailable && _window.MaskTrayIcon != 0;
                WriteVerification(new
                {
                    Success = success, ProcessName = processName, Executable = Environment.ProcessPath,
                    WindowTitle = _window.Title, HasSmallWindowIcon = smallIcon != 0,
                    HasBigWindowIcon = bigIcon != 0, TrayAvailable = _window.MaskTrayAvailable,
                    HasTrayIcon = _window.MaskTrayIcon != 0, Mask = Mask?.ProcessName,
                    DesktopFeatures = desktopFeatures
                });
            }
            catch (Exception ex) { WriteVerification(new { Success = false, Error = ex.ToString() }); }
            finally { _window?.Close(); }
        };
        timer.Start();
    }

    private static void WriteVerification(object result)
    {
        var output = Environment.GetEnvironmentVariable("MOUSECONTROL_VERIFY_OUTPUT")
            ?? Path.Combine(Path.GetTempPath(), "MouseControl-verification.json");
        File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint message, nint wparam, nint lparam);
}
