using MouseControl.Core;

namespace MouseControl.App.Services;

internal interface IArduinoTransport : IDisposable
{
    bool IsOpen { get; }
    void DiscardInput();
    void Write(byte[] bytes, int timeoutMilliseconds);
    int Read(byte[] bytes, int offset, int count, int timeoutMilliseconds);
}

internal sealed class ArduinoSerialTransport : IArduinoTransport
{
    private const int BaudRate = 115200;
    private const int ConnectionResetMilliseconds = 300;
    private readonly IArduinoSerialConnection _serial;
    public bool IsOpen => _serial.IsOpen;

    public ArduinoSerialTransport(string port) : this(port,
        (name, baudRate) => new ArduinoSerialConnection(name, baudRate), Thread.Sleep)
    {
    }

    internal ArduinoSerialTransport(string port, Func<string, int, IArduinoSerialConnection> createConnection,
        Action<int> delay)
    {
        if (!ProfileConfiguration.IsArduinoPortName(port)) throw new ArgumentException("Select a valid COM port.", nameof(port));
        ArgumentNullException.ThrowIfNull(createConnection);
        ArgumentNullException.ThrowIfNull(delay);
        _serial = createConnection(port, BaudRate);
        try
        {
            // Firmware clears its session only while DTR is low. Its USB send can block
            // for 250ms, so allow 300ms for the loop to observe low DTR after opening,
            // then remove stale bytes before the fresh HELLO.
            // Keep 115200 baud: Leonardo's 1200-baud touch would invoke its bootloader.
            _serial.DtrEnable = false;
            _serial.Open();
            delay(ConnectionResetMilliseconds);
            _serial.DiscardInBuffer();
            _serial.DiscardOutBuffer();
            _serial.DtrEnable = true;
        }
        catch { _serial.Dispose(); throw; }
    }

    public void DiscardInput() => _serial.DiscardInBuffer();
    public void Write(byte[] bytes, int timeoutMilliseconds)
    {
        _serial.WriteTimeout = timeoutMilliseconds;
        _serial.Write(bytes, 0, bytes.Length);
    }
    public int Read(byte[] bytes, int offset, int count, int timeoutMilliseconds)
    {
        _serial.ReadTimeout = timeoutMilliseconds;
        return _serial.Read(bytes, offset, count);
    }
    public void Dispose() => _serial.Dispose();
}
