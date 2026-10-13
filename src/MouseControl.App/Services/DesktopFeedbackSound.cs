using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace MouseControl.App.Services;

internal enum DesktopFeedbackKind { Enabled, Disabled, Adjustment }

/// <summary>Serializes short in-memory PCM cues; no files, device discovery or UI waits.</summary>
internal sealed class DesktopFeedbackSound : IDisposable
{
    private readonly Channel<DesktopFeedbackKind> _queue = Channel.CreateBounded<DesktopFeedbackKind>(
        new BoundedChannelOptions(3) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private readonly Action<byte[]> _play;
    private int _disposed;

    public DesktopFeedbackSound() : this(PlayMemoryWave) { }

    // A deterministic sink permits queue/disposal checks without opening an audio device.
    internal DesktopFeedbackSound(Action<byte[]> play)
    {
        _play = play;
        _worker = Task.Run(RunAsync);
    }

    internal Task Completion => _worker;

    public void Play(DesktopFeedbackKind kind)
    {
        if (kind is < DesktopFeedbackKind.Enabled or > DesktopFeedbackKind.Adjustment) return;
        if (Volatile.Read(ref _disposed) == 0) _queue.Writer.TryWrite(kind);
    }

    private async Task RunAsync()
    {
        try
        {
            var cues = new[] { CreateWave(660, 880), CreateWave(660, 440), CreateWave(900, 900, 0.075) };
            await foreach (var kind in _queue.Reader.ReadAllAsync(_stop.Token))
            {
                if (_stop.IsCancellationRequested) break;
                _play(cues[(int)kind]);
            }
        }
        catch (OperationCanceledException) { }
        finally { _stop.Dispose(); }
    }

    private static void PlayMemoryWave(byte[] wave)
    {
        var pinned = GCHandle.Alloc(wave, GCHandleType.Pinned);
        try
        {
            // SND_MEMORY | SND_NODEFAULT | SND_SYNC. The pinned buffer remains alive
            // until playback returns. One worker prevents overlapping cues.
            PlaySound(pinned.AddrOfPinnedObject(), 0, 0x0004 | 0x0002);
        }
        finally { pinned.Free(); }
    }

    internal static byte[] CreateWave(double first, double second, double seconds = 0.14)
    {
        const int rate = 22050;
        var count = (int)(rate * seconds);
        using var stream = new MemoryStream(44 + count * 2);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(36 + count * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate);
        writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(count * 2);
        double phase = 0;
        for (var i = 0; i < count; i++)
        {
            var t = i / (double)count;
            var envelope = Math.Min(1, t / 0.08) * Math.Min(1, (1 - t) / 0.22);
            phase += 2 * Math.PI * (t < 0.5 ? first : second) / rate;
            writer.Write((short)(Math.Sin(phase) * envelope * 4200));
        }
        return stream.ToArray();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _queue.Writer.TryComplete();
        // Cancellation is signalled before the worker disposes its source. Do not wait on
        // the UI thread: an already-playing cue finishes in at most 140ms.
        try { _stop.Cancel(); } catch (ObjectDisposedException) { }
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    private static extern bool PlaySound(nint sound, nint module, uint flags);
}
