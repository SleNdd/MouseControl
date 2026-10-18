using MouseControl.Masking;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MouseControl.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var verify = args.Contains("--verify-launch");
        try
        {
            if (!verify && Mutex.TryOpenExisting("SleNdd.MouseControl", out var existing))
            {
                using (existing) PostMessage((nint)0xffff, RegisterWindowMessage("SleNdd.MouseControl.ShowWindow"), 0, 0);
                return 0;
            }
            var mask = args.Contains("--no-mask") ? null : ProcessMasker.FindBestMask();
            using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("MouseControl.Payload")
                ?? throw new InvalidOperationException("The application payload is missing.");
            var executable = RuntimeCache.Prepare(payload, mask);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
            foreach (var argument in args) start.ArgumentList.Add(argument);
            start.Environment.Remove("MOUSECONTROL_PROCESS_NAME");
            start.Environment.Remove("MOUSECONTROL_ICON_PATH");
            if (mask is not null)
            {
                start.Environment["MOUSECONTROL_PROCESS_NAME"] = mask.ProcessName;
                start.Environment["MOUSECONTROL_ICON_PATH"] = Path.Combine(start.WorkingDirectory, "mask.ico");
            }
            using var child = Process.Start(start) ?? throw new InvalidOperationException("The application could not be started.");
            // A diagnostic run is isolated from saved settings and never emits mouse input.
            if (verify)
            {
                if (!child.WaitForExit(30_000)) throw new TimeoutException("The application verification timed out.");
                return child.ExitCode;
            }
            return 0;
        }
        catch (Exception ex)
        {
            var message = "MouseControl could not start: " + ex.Message;
            if (verify)
            {
                var output = Environment.GetEnvironmentVariable("MOUSECONTROL_VERIFY_OUTPUT");
                if (!string.IsNullOrWhiteSpace(output)) File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(new { Success = false, Error = message }));
            }
            else MessageBox(0, message, "MouseControl", 0x10);
            return 1;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBox(nint hwnd, string text, string title, uint type);
}
