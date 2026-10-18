using System.IO.Compression;
using System.Security.Cryptography;

namespace MouseControl.Masking;

internal static class RuntimeCache
{
    internal static string Prepare(Stream payload, ProcessMasker.MaskResult? mask, string? cacheRoot = null)
    {
        if (mask is not null && !ProcessMasker.IsUsableName(mask.ProcessName))
            throw new InvalidDataException("The selected process name is invalid.");
        var payloadHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        payload.Position = 0;
        var name = mask?.ProcessName ?? "MouseControl.App";
        var iconHash = mask is null ? "original" : Convert.ToHexString(SHA256.HashData(mask.IconBytes)).ToLowerInvariant();
        var root = Path.GetFullPath(cacheRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseControl", "Runtime"));
        var buildFolder = Path.Combine(root, payloadHash);
        var directory = Path.Combine(buildFolder, name + "-" + iconHash);
        var executable = Path.Combine(directory, name + ".exe");
        using var preparation = new Mutex(false, "SleNdd.MouseControl.Prepare");
        var acquired = false;
        try
        {
            try { acquired = preparation.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new TimeoutException("Another MouseControl launch is still preparing its files.");
            bool Ready(string folder) => File.Exists(Path.Combine(folder, ".ready"))
                && File.ReadAllText(Path.Combine(folder, ".ready")) == payloadHash
                && File.Exists(Path.Combine(folder, name + ".exe"))
                && File.Exists(Path.Combine(folder, "MouseControl.App.dll"))
                && (mask is null || File.Exists(Path.Combine(folder, "mask.ico")));
            if (Ready(directory)) return executable;
            // A damaged canonical directory is preserved; reuse a previously recovered generation.
            if (Directory.Exists(buildFolder))
                foreach (var recovered in Directory.EnumerateDirectories(buildFolder, Path.GetFileName(directory) + "-*"))
                {
                    var suffix = Path.GetFileName(recovered).AsSpan(Path.GetFileName(directory).Length + 1);
                    if (Guid.TryParseExact(suffix, "N", out _) && Ready(recovered))
                        return Path.Combine(recovered, name + ".exe");
                }

            // Never overwrite an existing cache or a running EXE. Publish a fully prepared directory.
            Directory.CreateDirectory(buildFolder);
            var staging = Path.Combine(buildFolder, "prepare-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                using (var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true))
                {
                    var prefix = staging + Path.DirectorySeparatorChar;
                    foreach (var entry in archive.Entries)
                    {
                        var destination = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                        if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("The application archive contains an unsafe path.");
                        if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destination); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        entry.ExtractToFile(destination);
                    }
                }
                var apphost = Path.Combine(staging, "MouseControl.App.exe");
                if (!File.Exists(apphost) || !File.Exists(Path.Combine(staging, "MouseControl.App.dll")))
                    throw new InvalidDataException("The application archive is incomplete.");
                if (mask is not null)
                {
                    var renamed = Path.Combine(staging, name + ".exe");
                    File.Copy(apphost, renamed);
                    ExecutableResources.Apply(renamed, mask);
                    File.WriteAllBytes(Path.Combine(staging, "mask.ico"), mask.IconBytes);
                }
                File.WriteAllText(Path.Combine(staging, ".ready"), payloadHash);
                if (Directory.Exists(directory)) directory += "-" + Guid.NewGuid().ToString("N");
                MovePreparedDirectory(staging, directory);
                return Path.Combine(directory, name + ".exe");
            }
            finally
            {
                // Only this call's disposable staging directory can be removed.
                var resolved = Path.GetFullPath(staging);
                if (resolved.StartsWith(buildFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
            }
        }
        finally { if (acquired) preparation.ReleaseMutex(); }
    }

    // Windows can briefly hold newly extracted files open while examining them.
    // Retry only publication of the completed directory, never extraction or a running EXE.
    internal static void MovePreparedDirectory(string source, string destination,
        Action<string, string>? move = null, Action<int>? delay = null)
    {
        move ??= Directory.Move;
        delay ??= Thread.Sleep;
        for (var attempt = 0; ; attempt++)
        {
            try { move(source, destination); return; }
            catch (Exception ex) when (attempt < 6 &&
                (ex is UnauthorizedAccessException || ex is IOException && (ex.HResult & 0xffff) is 5 or 32 or 33))
            { delay(25 << attempt); }
        }
    }
}
