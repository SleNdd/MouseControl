using System.Diagnostics;
using System.Runtime.InteropServices;
using MouseControl.Core;

namespace MouseControl.App.Services;

/// <summary>Serialized relative input runtime. Simulation never submits native input.</summary>
public sealed class RuntimeService : IDisposable
{
    private readonly object _sync = new();
    private readonly bool _simulation;
    private readonly Func<ActivationBinding, bool> _heldProvider;
    private readonly Func<bool> _ownForegroundProvider;
    private readonly Func<IInputBackend> _windowsFactory;
    private readonly Func<string, IInputBackend> _arduinoFactory;
    private IInputBackend _backend;
    private bool _pendingBackendChange;
    private readonly Func<int> _seedProvider;
    private readonly ActivationGate _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private MotionSettings _settings = new();
    private MotionGenerator _motion = new(new(), 0);
    private ActivationBinding _binding;
    private bool _suspended;
    private bool _active;
    private bool _disposed;
    private double _carryX, _carryY;
    private string _status = "Disabled";
    [ThreadStatic] private static RuntimeService? s_workerOwner;

    public RuntimeService(bool simulation = false, Action<int, int>? simulatedOutput = null,
        Func<ActivationBinding, bool>? heldProvider = null, Func<bool>? ownForegroundProvider = null,
        Func<int>? seedProvider = null)
        : this(simulation, simulatedOutput, heldProvider, ownForegroundProvider, null, seedProvider)
    {
    }

    // Internal seams keep behavioral checks isolated from live input and USB devices.
    internal RuntimeService(bool simulation, Action<int, int>? simulatedOutput,
        Func<ActivationBinding, bool>? heldProvider, Func<bool>? ownForegroundProvider,
        Func<int, int, uint>? inputSubmitter, Func<int>? seedProvider = null,
        Func<string, IInputBackend>? arduinoFactory = null)
    {
        _simulation = simulation;
        _heldProvider = heldProvider ?? IsHeld;
        _ownForegroundProvider = ownForegroundProvider ?? Native.OwnForeground;
        _windowsFactory = simulation
            ? () => new SimulatedInputBackend(simulatedOutput)
            : () => new WindowsInputBackend(inputSubmitter ?? SubmitNative);
        _arduinoFactory = arduinoFactory ?? ArduinoInputBackend.Connect;
        _backend = _windowsFactory();
        _seedProvider = seedProvider ?? Random.Shared.Next;
        _worker = Task.Run(RunAsync);
    }

    public event Action<string>? StatusChanged;
    public event Action? BackendChanged;
    public bool Enabled { get { lock (_sync) return _gate.Enabled; } }
    public string Status { get { lock (_sync) return _status; } }
    public InputBackendKind BackendKind { get { lock (_sync) return _backend.Kind; } }
    public string BackendPort { get { lock (_sync) return _backend.Port; } }
    public bool BackendConnected { get { lock (_sync) return !_disposed && _backend.Connected; } }

    /// <summary>Explicitly connects the selected backend, leaving motion disabled on every attempt.</summary>
    public bool SetBackend(InputBackendKind kind, string? port, out string error) => ChangeBackend(kind, port, connect: true, out error);

    /// <summary>Reconnects only the still-selected offline Arduino endpoint; stale queued attempts cannot switch backends.</summary>
    public bool TryReconnectArduino(string? port, out string error) => ChangeBackend(InputBackendKind.Arduino, port, connect: true, out error, onlyIfSelected: true);

    /// <summary>Restores Arduino selection without opening a port; Windows remains immediately usable.</summary>
    public bool SelectBackendDisconnected(InputBackendKind kind, string? port, out string error) => ChangeBackend(kind, port, connect: false, out error);

    private bool ChangeBackend(InputBackendKind kind, string? port, bool connect, out string error, bool onlyIfSelected = false)
    {
        error = "";
        string? changed;
        var success = false;
        var selectedPort = (port ?? "").Trim().ToUpperInvariant();
        lock (_sync)
        {
            if (_disposed) { error = "Runtime is stopped."; return false; }
            if (onlyIfSelected)
            {
                if (_backend.Kind != InputBackendKind.Arduino || _backend.Port != selectedPort)
                { error = "Arduino selection changed; reconnect attempt discarded."; return false; }
                if (_backend.Connected) return true;
            }
            _gate.SetEnabled(false);
            StopAndClearMotion();
            try
            {
                if (!Enum.IsDefined(kind)) throw new ArgumentException("Choose a supported input backend.");
                if (kind == InputBackendKind.Arduino &&
                    ((connect && selectedPort.Length == 0) || (selectedPort.Length != 0 && !ProfileConfiguration.IsArduinoPortName(selectedPort))))
                    throw new ArgumentException("Select a valid Arduino COM port.");
                if (kind == InputBackendKind.Arduino && connect && _simulation)
                    throw new InvalidOperationException("Simulation cannot open an Arduino connection.");
                // Same connected endpoint does not need a second exclusive port open.
                if (connect && kind == _backend.Kind && _backend.Connected &&
                    (kind == InputBackendKind.Windows || selectedPort == _backend.Port)) success = true;
                else
                {
                    var candidate = kind == InputBackendKind.Windows ? _windowsFactory()
                        : connect ? _arduinoFactory(selectedPort) : new DisconnectedArduinoBackend(selectedPort);
                    var previous = _backend;
                    _backend = candidate;
                    SafeDispose(previous);
                    success = true;
                }
                changed = SetStatus("Disabled");
            }
            catch (Exception ex)
            {
                error = ex.Message;
                changed = SetStatus($"Backend connection failed: {error}; motion disabled.");
            }
            _pendingBackendChange = true;
        }
        Notify(changed);
        return success;
    }

    public void SetProfile(Profile profile)
    {
        string? changed;
        lock (_sync)
        {
            if (_disposed) return;
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(profile.Motion);
            profile.Motion.Validate();
            if (!Enum.IsDefined(profile.Activation)) throw new ArgumentException("Invalid activation binding.", nameof(profile));
            _binding = profile.Activation;
            _settings = profile.Motion;
            _motion.UpdateSettings(profile.Motion);
            _gate.Invalidate();
            var stopError = StopAndClearMotion();
            changed = SetStatus(stopError ?? (_gate.Enabled ? "Idle" : "Disabled"));
        }
        Notify(changed);
    }

    public void SetEnabled(bool enabled)
    {
        string? changed;
        lock (_sync)
        {
            if (_disposed) return;
            if (enabled && !_backend.Connected)
            {
                _gate.SetEnabled(false);
                changed = SetStatus("Arduino offline — check the selected port in Settings.");
            }
            else
            {
                _gate.SetEnabled(enabled);
                var stopError = !enabled ? StopAndClearMotion() : null;
                changed = SetStatus(stopError ?? (enabled ? "Idle" : "Disabled"));
            }
        }
        Notify(changed);
    }

    public void Invalidate()
    {
        string? changed;
        lock (_sync)
        {
            if (_disposed) return;
            _gate.Invalidate();
            var stopError = StopAndClearMotion();
            changed = SetStatus(stopError ?? (_gate.Enabled ? "Idle" : "Disabled"));
        }
        Notify(changed);
    }

    public void SetSuspended(bool suspended)
    {
        string? changed;
        lock (_sync)
        {
            if (_disposed) return;
            string? stopError = null;
            if (_suspended != suspended)
            {
                _suspended = suspended;
                _gate.Invalidate();
                stopError = StopAndClearMotion();
            }
            changed = SetStatus(stopError ?? (!_gate.Enabled ? "Disabled" : suspended ? "Paused" : "Idle"));
        }
        Notify(changed);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _disposed = true;
                _gate.SetEnabled(false);
                StopAndClearMotion();
                SafeDispose(_backend);
                _pendingBackendChange = true;
                _status = "Stopped";
                _stop.Cancel();
            }
        }
        Notify("Stopped");
        // A worker callback cannot join itself; its disposed flag prevents further submission.
        if (!ReferenceEquals(s_workerOwner, this)) _worker.GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(8));
        var last = Stopwatch.GetTimestamp();
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
            {
                var now = Stopwatch.GetTimestamp();
                var dt = Stopwatch.GetElapsedTime(last, now).TotalSeconds;
                last = now;
                s_workerOwner = this;
                try { Tick(dt); }
                finally { s_workerOwner = null; }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            s_workerOwner = this;
            try { Fail($"Runtime stopped: {ex.Message}"); }
            finally { s_workerOwner = null; }
        }
        finally
        {
            lock (_sync)
            {
                _disposed = true;
                _gate.SetEnabled(false);
                StopAndClearMotion();
                SafeDispose(_backend);
                _stop.Dispose();
            }
        }
    }

    private void Tick(double dt)
    {
        string? changed = null;
        lock (_sync)
        {
            if (_disposed) return;
            try
            {
                if (!_gate.Enabled) { ClearMotion(); return; }
                var suspended = _suspended || _ownForegroundProvider();
                var active = _gate.Step(_heldProvider(_binding), suspended);
                if (!double.IsFinite(dt) || dt > MotionGenerator.MaximumStepSeconds || dt <= 0)
                {
                    _gate.Invalidate();
                    active = false;
                }
                if (!active)
                {
                    var stopError = StopAndClearMotion();
                    changed = SetStatus(stopError ?? (suspended ? "Paused" : "Idle"));
                }
                else
                {
                    if (!_active) _motion = new MotionGenerator(_settings, _seedProvider());
                    _active = true;
                    var delta = _motion.Step(dt);
                    _carryX += delta.Dx;
                    _carryY += delta.Dy;
                    var dx = checked((int)Math.Truncate(_carryX));
                    var dy = checked((int)Math.Truncate(_carryY));
                    _carryX -= dx;
                    _carryY -= dy;
                    if (dx != 0 || dy != 0)
                        _backend.Move(dx, dy);
                    if (_gate.Enabled) changed = SetStatus("Active");
                }
            }
            catch (Exception ex) { changed = FailLocked($"Runtime error: {ex.Message}; motion disabled."); }
        }
        Notify(changed);
    }

    private void Fail(string status)
    {
        string? changed;
        lock (_sync)
        {
            changed = FailLocked(status);
        }
        Notify(changed);
    }

    private string? FailLocked(string status)
    {
        _gate.SetEnabled(false);
        StopAndClearMotion();
        if (_backend.Kind == InputBackendKind.Arduino)
        {
            SafeDispose(_backend);
            _pendingBackendChange = true;
        }
        return SetStatus(status);
    }

    private void ClearMotion()
    {
        if (_active || _carryX != 0 || _carryY != 0) _motion.ResetSession();
        _active = false;
        _carryX = _carryY = 0;
    }

    private string? StopAndClearMotion()
    {
        var hadActivity = _active;
        ClearMotion();
        if (!hadActivity) return null;
        try { _backend.Stop(); return null; }
        catch (Exception ex)
        {
            _gate.SetEnabled(false);
            SafeDispose(_backend);
            _pendingBackendChange = true;
            return $"Backend stop failed: {ex.Message}; motion disabled.";
        }
    }

    private static void SafeDispose(IInputBackend backend)
    {
        try { backend.Dispose(); }
        catch { /* Closing a failed connection must not restart or prevent disabling motion. */ }
    }

    private string? SetStatus(string status)
    {
        if (_status == status) return null;
        _status = status;
        return status;
    }

    private void Notify(string? status)
    {
        Action<string>? observers;
        Action? backendObservers;
        lock (_sync)
        {
            observers = status is not null && status == _status ? StatusChanged : null;
            backendObservers = _pendingBackendChange ? BackendChanged : null;
            _pendingBackendChange = false;
        }
        // External observers run without our state lock, so a UI callback may safely dispose.
        // UI dispatchers should read Status on delivery, since a newer transition may follow.
        if (status is not null) InvokeObservers(observers, status);
        if (backendObservers is not null)
            foreach (Action callback in backendObservers.GetInvocationList()) { try { callback(); } catch { } }
    }

    private static void InvokeObservers(Action<string>? observers, string value)
    {
        if (observers is null) return;
        foreach (Action<string> callback in observers.GetInvocationList())
        {
            try { callback(value); }
            catch { /* A UI observer cannot terminate the input worker. */ }
        }
    }

    private static uint SubmitNative(int dx, int dy)
    {
        var input = new Native.Input
        {
            Type = 0,
            Data = new Native.InputUnion { Mouse = new Native.MouseInput { Dx = dx, Dy = dy, Flags = 0x0001 } }
        };
        return Native.SendInput(1, new[] { input }, Marshal.SizeOf<Native.Input>());
    }

    private static bool IsHeld(ActivationBinding binding)
    {
        var swapped = Native.GetSystemMetrics(23) != 0; // SM_SWAPBUTTON
        var lmb = swapped ? 0x02 : 0x01;
        var rmb = swapped ? 0x01 : 0x02;
        return binding switch
        {
            ActivationBinding.Lmb => Native.Down(lmb),
            ActivationBinding.Rmb => Native.Down(rmb),
            ActivationBinding.LmbAndRmb => Native.Down(lmb) && Native.Down(rmb),
            ActivationBinding.Mmb => Native.Down(0x04),
            ActivationBinding.Alt => Native.Down(0x12),
            ActivationBinding.Shift => Native.Down(0x10),
            _ => false
        };
    }
}
