using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MouseControl.App.Services;

internal interface IOutputScheduler : IDisposable
{
    void WaitForNextTick(int requestedRateHz, CancellationToken cancellationToken);
}

/// <summary>Single-owner, cancellable one-shot scheduling with no missed-tick queue.</summary>
internal sealed class OutputScheduler : IOutputScheduler
{
    private readonly TimerWaitHandle? _timer;
    private readonly OutputCadence _cadence = new();

    internal bool UsesHighResolution { get; }

    internal OutputScheduler(bool preferHighResolution = true)
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = CreateWaitableTimerExW(IntPtr.Zero, null,
            preferHighResolution ? 0x00000002u : 0u, 0x00100002); // SYNCHRONIZE | TIMER_MODIFY_STATE
        UsesHighResolution = preferHighResolution && !handle.IsInvalid;
        if (handle.IsInvalid && preferHighResolution)
        {
            handle.Dispose();
            handle = CreateWaitableTimerExW(IntPtr.Zero, null, 0, 0x00100002);
        }
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "Cannot create the output waitable timer.");
        }
        _timer = new TimerWaitHandle(handle);
    }

    public void WaitForNextTick(int requestedRateHz, CancellationToken cancellationToken)
    {
        var delay = _cadence.NextDelayTicks(Stopwatch.GetTimestamp(), requestedRateHz);
        WaitForDelay(TimeSpan.FromSeconds((double)delay / Stopwatch.Frequency), cancellationToken);
        _cadence.RecordWake(Stopwatch.GetTimestamp());
    }

    internal void WaitForDelay(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_timer is null)
        {
            // Portable development harness fallback; no busy spin or global timer-resolution change.
            var milliseconds = checked((int)Math.Max(1, Math.Ceiling(delay.TotalMilliseconds)));
            if (cancellationToken.WaitHandle.WaitOne(milliseconds)) cancellationToken.ThrowIfCancellationRequested();
        }
        else
        {
            // Relative 100 ns units, one-shot, no APC and no wake from suspended power states.
            var dueTime = -Math.Max(1, delay.Ticks);
            if (!SetWaitableTimer(_timer.SafeWaitHandle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot arm the output waitable timer.");
            WaitHandle.WaitAny([cancellationToken.WaitHandle, _timer]);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void Dispose() => _timer?.Dispose();

    private sealed class TimerWaitHandle : WaitHandle
    {
        internal TimerWaitHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period,
        IntPtr completionRoutine, IntPtr completionArgument, [MarshalAs(UnmanagedType.Bool)] bool resume);
}

/// <summary>Elapsed work consumes the interval; overruns start a fresh interval instead of catching up.</summary>
internal sealed class OutputCadence
{
    private int _rate;
    private long _period;
    private long _next;

    internal long NextDelayTicks(long now, int rate)
    {
        if (_rate != rate)
        {
            _rate = rate;
            _period = Math.Max(1, Stopwatch.Frequency / rate);
            _next = now + _period;
        }
        if (_next <= now) _next = now + _period;
        return _next - now;
    }

    internal void RecordWake(long now) => _next = now + _period;
}
