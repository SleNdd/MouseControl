using System.Text.Json;
using System.Text.Json.Serialization;

namespace MouseControl.Core;

public sealed record ProfileLoadResult(ProfileConfiguration? Configuration, string? Error)
{
    public bool Success => Configuration is not null && Error is null;
}

public sealed record ProfileSaveResult(bool Success, string? Error);

/// <summary>
/// Atomic, versioned JSON storage. A corrupt/unsupported existing document is never overwritten.
/// Cooperating writers serialize through an exclusive sibling lock file; lock contention fails
/// immediately and can be retried. Valid documents use last-writer-wins semantics.
/// </summary>
public sealed class ProfileStore
{
    private const long MaximumDocumentBytes = 1_048_576;
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public ProfileStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public ProfileLoadResult Load()
    {
        // Windows File.Replace can briefly deny opening the target during its atomic rename.
        // Retry only this transient condition; malformed JSON is never retried or repaired.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new(ReadExisting() ?? ProfileConfiguration.CreateDefault(), null);
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33 && attempt < 4)
            {
                Thread.Sleep(2 << attempt);
            }
            catch (Exception ex) when (IsExpectedFailure(ex))
            {
                return new(null, ex.Message);
            }
        }
    }

    public ProfileSaveResult Save(ProfileConfiguration configuration)
    {
        string? temporaryPath = null;
        try
        {
            ArgumentNullException.ThrowIfNull(configuration);
            // Snapshot the only collection so callers cannot change it during serialization.
            var snapshot = configuration with { Profiles = configuration.Profiles?.ToArray()! };
            snapshot.Validate();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
            if (bytes.LongLength > MaximumDocumentBytes)
                return new(false, "Configuration exceeds the one-megabyte document limit.");

            var directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);
            using var writerLock = new FileStream(_filePath + ".lock", FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            var existed = ReadExisting() is not null; // Validate before any replacement.
            temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
            using (var temporary = new FileStream(temporaryPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                temporary.Write(bytes);
                temporary.Flush(flushToDisk: true);
            }
            // Both paths are on the same filesystem. No delete-and-write fallback is allowed.
            if (existed)
                File.Replace(temporaryPath, _filePath, destinationBackupFileName: null);
            else
                File.Move(temporaryPath, _filePath); // Fails if a noncooperating writer creates it.
            temporaryPath = null;
            return new(true, null);
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            return new(false, ex.Message);
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private ProfileConfiguration? ReadExisting()
    {
        FileStream stream;
        try
        {
            // Windows replacement acquires write/delete access. Readers must share both;
            // cooperating writers only replace whole files and never edit a live document.
            stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        using (stream)
        {
            if (stream.Length > MaximumDocumentBytes)
                throw new ArgumentException("Configuration exceeds the one-megabyte document limit.");
            var configuration = JsonSerializer.Deserialize<ProfileConfiguration>(stream, JsonOptions)
                ?? throw new JsonException("Configuration document cannot be null.");
            configuration.Validate();
            return configuration;
        }
    }

    private static bool IsExpectedFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException;

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
