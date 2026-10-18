using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MouseControl.Masking;

internal static class ProcessMasker
{
    private static readonly string[] BrowserNames =
    ["brave", "chrome", "yandex", "browser", "firefox", "msedge", "opera", "vivaldi",
     "waterfox", "librewolf", "chromium", "iridium", "ungoogled-chromium", "thorium", "zen"];

    internal sealed record Candidate(string Name, string ExecutablePath, bool HasWindow);
    internal sealed record MaskResult(string ProcessName, byte[] IconBytes);

    // Browsers outrank other apps. Within each tier, prefer the largest process group.
    internal static IEnumerable<IGrouping<string, Candidate>> Rank(IEnumerable<Candidate> candidates) =>
        candidates.Where(c => IsUsableName(c.Name) && !IsSystemProcess(c.Name))
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => IsBrowser(g.Key, g.First().ExecutablePath))
            .ThenByDescending(g => g.Count())
            .ThenByDescending(g => g.Any(c => c.HasWindow))
            .ThenBy(g => Array.FindIndex(BrowserNames, n => n.Equals(g.Key, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

    internal static MaskResult? FindBestMask()
    {
        var candidates = new List<Candidate>();
        Process[] processes;
        using var ownProcess = Process.GetCurrentProcess();
        var session = ownProcess.SessionId;
        try { processes = Process.GetProcesses(); }
        catch (Exception) { return null; }
        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.SessionId != session) continue;
                    var name = process.ProcessName;
                    if (!IsUsableName(name) || IsSystemProcess(name)) continue;
                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    if (FileVersionInfo.GetVersionInfo(path).ProductName is "MaskShow" or "MouseControl") continue;
                    candidates.Add(new Candidate(name, path, process.MainWindowHandle != 0));
                }
                catch (Exception) { /* Processes can exit or deny access during enumeration. */ }
            }
        }
        return ChooseMask(candidates, ExecutableResources.ReadIcon);
    }

    internal static MaskResult? ChooseMask(IEnumerable<Candidate> candidates, Func<string, byte[]?> readIcon)
    {
        foreach (var group in Rank(candidates))
            foreach (var candidate in group.OrderByDescending(c => c.HasWindow).DistinctBy(c => c.ExecutablePath, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (readIcon(candidate.ExecutablePath) is { Length: > 0 } icon)
                        return new MaskResult(group.Key.ToLowerInvariant(), icon);
                }
                catch (Exception) { /* An unavailable candidate must not abort the whole scan. */ }
            }
        return null;
    }

    private static bool IsBrowser(string name, string path)
    {
        if (!BrowserNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return false;
        if (!name.Equals("browser", StringComparison.OrdinalIgnoreCase)) return true;
        // The generic filename is used by Yandex, but also by unrelated applications.
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return (info.ProductName + " " + info.CompanyName).Contains("Yandex", StringComparison.OrdinalIgnoreCase)
                || (info.ProductName + " " + info.CompanyName).Contains("Яндекс", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }

    internal static bool IsUsableName(string name) => !string.IsNullOrWhiteSpace(name)
        && name.Length <= 100 && name is not "." and not ".."
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !name.EndsWith('.') && !name.EndsWith(' ')
        && !new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }
            .Contains(name.Split('.')[0], StringComparer.OrdinalIgnoreCase);

    private static bool IsSystemProcess(string name) => name.ToLowerInvariant() is
        "svchost" or "csrss" or "conhost" or "smss" or "services" or "lsass" or
        "wininit" or "winlogon" or "dwm" or "explorer" or "system" or "idle" or
        "taskhostw" or "searchhost" or "startmenuexperiencehost" or "shellexperiencehost" or
        "runtimebroker" or "textinputhost" or "ctfmon" or "sihost" or "fontdrvhost" or
        "applicationframehost" or "systemsettings" or "lockapp" or "searchapp" or
        "securityhealthservice" or "securityhealthsystray" or "dllhost" or "spoolsv" or
        "dashost" or "audiodg" or "msdtc" or "lsaiso" or "registry" or "wudfhost" or
        "wmiprvse" or "searchindexer" or "searchprotocolhost" or "searchfilterhost" or
        "mousecontrol" or "maskshow" or "maskshow.app" or "maskshow.checks" or
        "dotnet" or "msbuild" or "vbcscompiler" or "node" or "node_repl" or "cmd" or "powershell" or "pwsh" or
        "wsl" or "windowsterminal" or "ssh" or "git" or "nvcontainer" or "nvdisplay.container" or
        "widgets" or "msedgewebview2" or "webview2" or "microsoftedgeupdate" or "googleupdate" or "updater" or
        "msmpeng" or "mpcmdrun" or "nissrv";

    internal static nint LoadIconFromFile(string? path, int size = 16) =>
        string.IsNullOrEmpty(path) || !File.Exists(path) ? 0 : LoadImage(0, path, 1, size, size, 0x10);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint instance, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(nint icon);
}
