using System.IO.Ports;

namespace MouseControl.App.Services;

internal interface IArduinoSerialConnection : IDisposable
{
    bool IsOpen { get; }
    bool DtrEnable { set; }
    int ReadTimeout { set; }
    int WriteTimeout { set; }
    void Open();
    void DiscardInBuffer();
    void DiscardOutBuffer();
    void Write(byte[] bytes, int offset, int count);
    int Read(byte[] bytes, int offset, int count);
}

internal sealed class ArduinoSerialConnection : IArduinoSerialConnection
{
    private readonly SerialPort _serial;

    public ArduinoSerialConnection(string port, int baudRate)
    {
        _serial = new SerialPort(port, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None, DtrEnable = false, RtsEnable = false,
            ReadTimeout = 200, WriteTimeout = 100, ReadBufferSize = 4096, WriteBufferSize = 2048
        };
    }

    public bool IsOpen => _serial.IsOpen;
    public bool DtrEnable { set => _serial.DtrEnable = value; }
    public int ReadTimeout { set => _serial.ReadTimeout = value; }
    public int WriteTimeout { set => _serial.WriteTimeout = value; }
    public void Open() => _serial.Open();
    public void DiscardInBuffer() => _serial.DiscardInBuffer();
    public void DiscardOutBuffer() => _serial.DiscardOutBuffer();
    public void Write(byte[] bytes, int offset, int count) => _serial.Write(bytes, offset, count);
    public int Read(byte[] bytes, int offset, int count) => _serial.Read(bytes, offset, count);
    public void Dispose() => _serial.Dispose();
}
