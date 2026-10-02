using System.IO.Ports;
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
    private readonly SerialPort _serial;
    public bool IsOpen => _serial.IsOpen;

    public ArduinoSerialTransport(string port)
    {
        if (!ProfileConfiguration.IsArduinoPortName(port)) throw new ArgumentException("Select a valid COM port.", nameof(port));
        _serial = new SerialPort(port, 115200, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None, DtrEnable = true, RtsEnable = false,
            ReadTimeout = 200, WriteTimeout = 100, ReadBufferSize = 4096, WriteBufferSize = 2048
        };
        try { _serial.Open(); _serial.DiscardInBuffer(); _serial.DiscardOutBuffer(); }
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
