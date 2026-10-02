using System.Diagnostics;
using System.Security.Cryptography;
using MouseControl.Core;

namespace MouseControl.App.Services;

/// <summary>Single-owner, one-command/ACK serial backend. Failed commands are never retried.</summary>
internal sealed class ArduinoInputBackend : IInputBackend
{
    private const int CommandTimeoutMilliseconds = 150;
    private const int HandshakeTimeoutMilliseconds = 500;
    private readonly IArduinoTransport _transport;
    private readonly uint _session;
    private uint _sequence;
    private bool _disposed;
    public InputBackendKind Kind => InputBackendKind.Arduino;
    public string Port { get; }
    public bool Connected => !_disposed && _transport.IsOpen;

    public static IInputBackend Connect(string port)
    {
        var session = NewSession();
        return new ArduinoInputBackend(new ArduinoSerialTransport(port), port, session);
    }

    internal ArduinoInputBackend(IArduinoTransport transport, string port, uint session)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        Port = port;
        _session = session;
        try
        {
            if (session == 0) throw new ArgumentException("A nonzero connection nonce is required.", nameof(session));
            _transport.DiscardInput();
            var reply = Exchange(new(ArduinoCommand.Hello, session, 0), HandshakeTimeoutMilliseconds);
            if (reply.Command != ArduinoCommand.HelloAck || reply.X != 'M' || reply.Y != 'C')
                throw new IOException("Selected port did not identify as MouseControl Leonardo firmware.");
        }
        catch { Dispose(); throw; }
    }

    public void Move(int dx, int dy)
    {
        // Runtime limits rate<=2000 and dt<=100ms; even accumulated carry remains <=201.
        if (Math.Abs((long)dx) > 254 || Math.Abs((long)dy) > 254)
            throw new ArgumentOutOfRangeException(nameof(dx), "At most two HID chunks may be submitted per tick.");
        var remainingX = dx;
        var remainingY = dy;
        while (remainingX != 0 || remainingY != 0)
        {
            var x = Math.Clamp(remainingX, -ArduinoProtocol.MaximumChunk, ArduinoProtocol.MaximumChunk);
            var y = Math.Clamp(remainingY, -ArduinoProtocol.MaximumChunk, ArduinoProtocol.MaximumChunk);
            Command(ArduinoCommand.Move, (sbyte)x, (sbyte)y);
            remainingX -= x; remainingY -= y;
        }
    }

    public void Stop()
    {
        if (Connected) Command(ArduinoCommand.Stop);
    }

    internal void Ping() => Command(ArduinoCommand.Ping);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _transport.Dispose();
    }

    private void Command(ArduinoCommand command, sbyte x = 0, sbyte y = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sequence == uint.MaxValue) { Dispose(); throw new IOException("Arduino sequence exhausted. Reconnect explicitly."); }
        var reply = Exchange(new(command, _session, ++_sequence, x, y), CommandTimeoutMilliseconds);
        if (reply.Command != ArduinoCommand.Ack || reply.X != 0 || reply.Y != 0)
        {
            Dispose();
            throw new IOException($"Arduino rejected command {command} (code {reply.X}). Reconnect explicitly.");
        }
    }

    private ArduinoPacket Exchange(ArduinoPacket request, int timeoutMilliseconds)
    {
        try
        {
            var start = Stopwatch.GetTimestamp();
            _transport.Write(ArduinoProtocol.Encode(request), 100);
            var bytes = new byte[ArduinoProtocol.FrameLength];
            var received = 0;
            while (received < bytes.Length)
            {
                var remaining = timeoutMilliseconds - (int)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (remaining <= 0) throw new TimeoutException("Arduino ACK deadline expired. Command outcome is unknown; reconnect explicitly.");
                var count = _transport.Read(bytes, received, bytes.Length - received, remaining);
                if (count <= 0 || count > bytes.Length - received) throw new IOException("Arduino serial response ended unexpectedly.");
                received += count;
            }
            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds > timeoutMilliseconds)
                throw new TimeoutException("Arduino acknowledgement arrived after its deadline. Reconnect explicitly.");
            if (!ArduinoProtocol.TryDecode(bytes, out var reply)) throw new IOException("Arduino response checksum or protocol version is invalid.");
            if (reply.Session != request.Session || reply.Sequence != request.Sequence)
                throw new IOException("Arduino returned a stale or mismatched acknowledgement.");
            return reply;
        }
        catch
        {
            // An already-written MOVE might have executed. Never retransmit it or reconnect automatically.
            Dispose();
            throw;
        }
    }

    private static uint NewSession()
    {
        uint value;
        do { value = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)); } while (value == 0);
        return value;
    }
}
