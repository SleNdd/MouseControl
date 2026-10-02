using System.Runtime.InteropServices;
using MouseControl.Core;

namespace MouseControl.App.Services;

internal interface IInputBackend : IDisposable
{
    InputBackendKind Kind { get; }
    string Port { get; }
    bool Connected { get; }
    void Move(int dx, int dy);
    void Stop();
}

internal sealed class WindowsInputBackend(Func<int, int, uint> submitter) : IInputBackend
{
    public InputBackendKind Kind => InputBackendKind.Windows;
    public string Port => "";
    public bool Connected { get; private set; } = true;

    public void Move(int dx, int dy)
    {
        ObjectDisposedException.ThrowIf(!Connected, this);
        if (submitter(dx, dy) != 1)
            throw new IOException($"Input submission failed (Windows error {Marshal.GetLastWin32Error()}). Check integrity-level restrictions.");
    }
    public void Stop() { }
    public void Dispose() => Connected = false;
}

internal sealed class SimulatedInputBackend(Action<int, int>? output) : IInputBackend
{
    public InputBackendKind Kind => InputBackendKind.Windows;
    public string Port => "";
    public bool Connected { get; private set; } = true;
    public void Move(int dx, int dy)
    {
        ObjectDisposedException.ThrowIf(!Connected, this);
        output?.Invoke(dx, dy);
    }
    public void Stop() { }
    public void Dispose() => Connected = false;
}

internal sealed class DisconnectedArduinoBackend(string port) : IInputBackend
{
    public InputBackendKind Kind => InputBackendKind.Arduino;
    public string Port => port;
    public bool Connected => false;
    public void Move(int dx, int dy) => throw new IOException("Arduino is disconnected. Check the selected port in Settings.");
    public void Stop() { }
    public void Dispose() { }
}
