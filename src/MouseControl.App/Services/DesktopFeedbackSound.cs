using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MouseControl.App.Services;

internal enum DesktopFeedbackKind { Enabled, Disabled, Adjustment }

/// <summary>Latest-event audio with one device and a short fade when replacing a cue.</summary>
internal sealed class DesktopFeedbackSound : IDisposable
{
    private readonly Channel<DesktopFeedbackKind> _queue = Channel.CreateBounded<DesktopFeedbackKind>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<IDesktopFeedbackAudio> _createAudio;
    private readonly Task _worker;
    private int _disposed;

    public DesktopFeedbackSound() : this(() => new DesktopFeedbackAudio()) { }
    internal DesktopFeedbackSound(Func<IDesktopFeedbackAudio> createAudio)
    {
        ArgumentNullException.ThrowIfNull(createAudio);
        _createAudio = createAudio;
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
        IDesktopFeedbackAudio? audio = null;
        try
        {
            await foreach (var kind in _queue.Reader.ReadAllAsync(_stop.Token))
            {
                if (_stop.IsCancellationRequested) break;
                try { (audio ??= _createAudio()).Play(kind); }
                catch (Exception)
                {
                    // A missing/disconnected device must not stop notifications or input.
                    // A later event may reopen the default device; never replay old cues.
                    ReleaseAudio();
                }
            }
        }
        catch (OperationCanceledException) { }
        finally { ReleaseAudio(); _stop.Dispose(); }

        void ReleaseAudio()
        {
            var previous = audio; audio = null;
            try { previous?.Dispose(); } catch (Exception) { }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _queue.Writer.TryComplete();
        // The worker owns the device, including shutdown. Never block the UI on a driver.
        try { _stop.Cancel(); } catch (ObjectDisposedException) { }
    }
}

internal interface IDesktopFeedbackAudio : IDisposable
{
    void Play(DesktopFeedbackKind kind);
}

/// <summary>Created and disposed by the worker, independently of PlaySound's process slot.</summary>
internal sealed class DesktopFeedbackAudio : IDesktopFeedbackAudio
{
    private readonly WasapiOut _output = new(AudioClientShareMode.Shared, true, 30);
    private readonly DesktopCueProvider _provider = new();
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _failure;
    private int _disposed;

    public DesktopFeedbackAudio()
    {
        _output.PlaybackStopped += (_, args) =>
        {
            Volatile.Write(ref _failure, args.Exception ?? new InvalidOperationException("Audio playback stopped."));
            _stopped.TrySetResult();
        };
        try { _output.Init(_provider); _output.Play(); }
        catch { _output.Dispose(); throw; }
    }

    public void Play(DesktopFeedbackKind kind)
    {
        if (Volatile.Read(ref _failure) is { } failure) throw new InvalidOperationException("Audio device is unavailable.", failure);
        _provider.Play(kind);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // This runs on our worker. Stop joins WASAPI's render thread; the completion signal
        // also covers playback that ended because of a device error before Stop was called.
        try { _output.Stop(); }
        finally
        {
            _stopped.Task.GetAwaiter().GetResult();
            _output.Dispose();
        }
    }
}

/// <summary>One PCM voice. Replacement fades out the old voice before starting the newest cue.</summary>
internal sealed class DesktopCueProvider : IWaveProvider
{
    internal const int SampleRate = 48000;
    internal const int FadeSamples = SampleRate * 6 / 1000;
    private static readonly short[][] Cues = [CreateCue(660, 880, .10), CreateCue(660, 440, .10), CreateCue(900, 900, .065)];
    private readonly object _gate = new();
    private short[] _current = [];
    private short[]? _pending;
    private int _position, _fadeLeft;
    public WaveFormat WaveFormat { get; } = new(SampleRate, 16, 1);

    internal void Play(DesktopFeedbackKind kind)
    {
        if (kind is < DesktopFeedbackKind.Enabled or > DesktopFeedbackKind.Adjustment) return;
        lock (_gate)
        {
            _pending = Cues[(int)kind];
            // More requests replace the pending cue without restarting an ongoing fade.
            if (_fadeLeft == 0 && _position < _current.Length) _fadeLeft = FadeSamples;
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count || count % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        lock (_gate)
        {
            for (var i = offset; i < offset + count; i += 2)
            {
                if (_pending is not null && _fadeLeft == 0)
                { _current = _pending; _pending = null; _position = 0; }
                var sample = _position < _current.Length ? _current[_position++] : (short)0;
                if (_fadeLeft > 0)
                {
                    var gain = (_fadeLeft - 1) / (double)(FadeSamples - 1);
                    sample = (short)(sample * gain); _fadeLeft--;
                }
                buffer[i] = (byte)sample; buffer[i + 1] = (byte)(sample >> 8);
            }
        }
        // Silence keeps the one device ready; old cues never form a delayed playlist.
        return count;
    }

    internal static short[] CreateCue(double first, double second, double seconds)
    {
        var samples = new short[(int)(SampleRate * seconds)];
        double phase = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = i / (double)(samples.Length - 1);
            var attack = Math.Min(1, t / .12);
            var release = Math.Min(1, (1 - t) / .25);
            var envelope = Math.Pow(Math.Sin(attack * Math.PI / 2), 2) * Math.Pow(Math.Sin(release * Math.PI / 2), 2);
            var glide = t * t * (3 - 2 * t);
            phase += 2 * Math.PI * (first + (second - first) * glide) / SampleRate;
            samples[i] = (short)(Math.Sin(phase) * envelope * 4200);
        }
        return samples;
    }
}
