using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MouseControl.Masking;

var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine("PASS " + description); checks++;
}
ProcessMasker.Candidate C(string name, bool window = false) => new(name, name + ".exe", window);
byte[]? FakeIcon(string path) => [1];
string? Pick(params ProcessMasker.Candidate[] candidates) => ProcessMasker.ChooseMask(candidates, FakeIcon)?.ProcessName;
Check(Pick(C("editor"), C("editor"), C("brave")) == "brave", "A browser outranks duplicated non-browser processes");
Check(Pick(C("brave"), C("chrome"), C("CHROME")) == "chrome", "The most numerous browser wins; grouping ignores case");
Check(Pick(C("editor"), C("editor"), C("player", true)) == "editor", "Duplicated apps outrank a single app with a window");
Check(Pick(C("firefox", true), C("brave")) == "firefox", "A window breaks ties between equally numerous browsers");
Check(Pick(C("chrome_helper"), C("chrome_helper"), C("firefox")) == "firefox", "Browser matching does not accept substring helpers");
Check(Pick(C("svchost"), C("svchost"), C("MouseControl"), C("editor")) == "editor", "Infrastructure and the original application are excluded");
Check(ProcessMasker.ChooseMask([C("brave"), C("editor")], p => p == "brave.exe" ? null : [1])?.ProcessName == "editor", "Unavailable browser icon falls back to the next app");
Check(ProcessMasker.ChooseMask([C("brave"), C("editor")], p => p == "brave.exe" ? throw new IOException() : [1])?.ProcessName == "editor", "An exited/inaccessible candidate does not abort selection");
Check(ProcessMasker.ChooseMask([], FakeIcon) is null, "An empty process list falls back without failure");
Check(new[] { "../outside", "CON", "nul.ext", "bad:name", "..", "name.", "" }.All(n => !ProcessMasker.IsUsableName(n)), "Unsafe executable names are rejected");
var moveAttempts = 0;
RuntimeCache.MovePreparedDirectory("source", "destination", (_, _) =>
{
    if (++moveAttempts < 3) throw new UnauthorizedAccessException();
}, _ => { });
Check(moveAttempts == 3, "Transient Windows publication denial retries the prepared directory");
moveAttempts = 0;
try
{
    RuntimeCache.MovePreparedDirectory("source", "destination", (_, _) => { moveAttempts++; throw new UnauthorizedAccessException(); }, _ => { });
    throw new Exception("Permanent denial was ignored");
}
catch (UnauthorizedAccessException) { }
Check(moveAttempts == 7, "Permanent publication denial fails after a bounded number of attempts");

var actual = ProcessMasker.FindBestMask() ?? throw new Exception("No icon-bearing app is running for the integration check.");
Console.WriteLine($"Live selection: {actual.ProcessName}, icon {actual.IconBytes.Length} bytes");
var source = Path.GetFullPath(args[0]);
var sourceHash = SHA256.HashData(File.ReadAllBytes(source));
var testRoot = Path.Combine(Path.GetTempPath(), "MouseControl-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
try
{
    var mask = new ProcessMasker.MaskResult("brave", actual.IconBytes);
    var copy = Path.Combine(testRoot, "brave.exe");
    File.Copy(source, copy);
    ExecutableResources.Apply(copy, mask);
    var version = FileVersionInfo.GetVersionInfo(copy);
    Check(version.FileDescription == "brave" && version.ProductName == "MouseControl" && version.OriginalFilename == "brave.exe", "Native EXE description/name are updated and keep our product identity");
    Check(version.FileVersion == "0.5.0.0" && version.FileMajorPart == 0 && version.FileMinorPart == 5 && version.FileBuildPart == 0, "VERSIONINFO strings and fixed version numbers agree");
    Check(ExecutableResources.ReadIcon(copy)!.SequenceEqual(mask.IconBytes), "All icon sizes survive the native EXE resource round trip");
    Check(Resources.Manifest(source).SequenceEqual(Resources.Manifest(copy)), "The native application manifest survives resource updates");
    var beforeInvalid = SHA256.HashData(File.ReadAllBytes(copy));
    try { ExecutableResources.Apply(copy, new("brave", [0, 0, 1, 0, 1, 0])); throw new Exception("Invalid icon was accepted"); }
    catch (InvalidDataException) { }
    Check(beforeInvalid.SequenceEqual(SHA256.HashData(File.ReadAllBytes(copy))), "Invalid icon data leaves the EXE unchanged");

    byte[] Archive(bool traversal = false)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            using (var host = zip.CreateEntry("MouseControl.App.exe").Open()) host.Write(File.ReadAllBytes(source));
            using (var dll = zip.CreateEntry("MouseControl.App.dll").Open()) dll.Write([1, 2, 3]);
            if (traversal) { using var escaped = zip.CreateEntry("../escaped.txt").Open(); escaped.WriteByte(1); }
        }
        return stream.ToArray();
    }
    var bytes = Archive();
    string Prepare(ProcessMasker.MaskResult? selection = null) => RuntimeCache.Prepare(new MemoryStream(bytes), selection, Path.Combine(testRoot, "cache"));
    var prepared = Prepare(mask);
    Check(Path.GetFileName(prepared) == "brave.exe" && ExecutableResources.ReadIcon(prepared)!.SequenceEqual(mask.IconBytes), "Cache prepares a named executable with the selected icon");
    var writeTime = File.GetLastWriteTimeUtc(prepared);
    Check(Prepare(mask) == prepared && File.GetLastWriteTimeUtc(prepared) == writeTime, "Repeated launch reuses the cache without rewriting its EXE");
    var concurrent = Task.WhenAll(Task.Run(() => Prepare(mask)), Task.Run(() => Prepare(mask))).GetAwaiter().GetResult();
    Check(concurrent.All(p => p == prepared), "Concurrent preparation resolves to the same complete cache");
    var unmasked = Prepare();
    Check(Path.GetFileName(unmasked) == "MouseControl.App.exe" && sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(unmasked))), "No suitable process retains the original unmodified apphost");
    var dir = Path.GetDirectoryName(prepared)!;
    File.WriteAllText(Path.Combine(dir, "keep.txt"), "preserve");
    File.Delete(Path.Combine(dir, "MouseControl.App.dll"));
    var recovered = Prepare(mask);
    Check(recovered != prepared && File.Exists(Path.Combine(dir, "keep.txt")), "Incomplete cache is recovered without overwriting or deleting existing files");
    Check(Prepare(mask) == recovered, "Subsequent launches reuse the successfully recovered cache");
    try { RuntimeCache.Prepare(new MemoryStream(Archive(true)), mask, Path.Combine(testRoot, "unsafe-cache")); throw new Exception("Unsafe archive accepted"); }
    catch (InvalidDataException) { }
    Check(!Directory.GetFiles(testRoot, "escaped.txt", SearchOption.AllDirectories).Any(), "Archive traversal is rejected before it writes outside staging");
    Check(sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))), "The source executable is unchanged");
}
finally
{
    var resolved = Path.GetFullPath(testRoot);
    if (resolved.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(resolved).StartsWith("MouseControl-checks-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
}
Console.WriteLine($"{checks} checks passed.");

internal static class Resources
{
    internal static byte[] Manifest(string path)
    {
        var module = LoadLibraryEx(path, 0, 0x22);
        if (module == 0) throw new Exception("Resource module missing");
        try
        {
            var resource = FindResource(module, (nint)1, (nint)24);
            var size = SizeofResource(module, resource);
            var pointer = LockResource(LoadResource(module, resource));
            if (size == 0 || pointer == 0) throw new Exception("Manifest resource missing");
            var data = new byte[size]; Marshal.Copy(pointer, data, 0, data.Length); return data;
        }
        finally { FreeLibrary(module); }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadLibraryEx(string path, nint file, uint flags);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(nint module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint FindResource(nint module, nint name, nint type);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(nint module, nint resource);
    [DllImport("kernel32.dll")] private static extern nint LoadResource(nint module, nint resource);
    [DllImport("kernel32.dll")] private static extern nint LockResource(nint resource);
}
