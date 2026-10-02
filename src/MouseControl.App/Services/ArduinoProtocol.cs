using System.Buffers.Binary;

namespace MouseControl.App.Services;

internal enum ArduinoCommand : byte { Hello = 1, Ping = 2, Move = 3, Stop = 4, HelloAck = 0x81, Ack = 0x82, Nack = 0x83 }
internal readonly record struct ArduinoPacket(ArduinoCommand Command, uint Session, uint Sequence, sbyte X = 0, sbyte Y = 0);

internal static class ArduinoProtocol
{
    public const int FrameLength = 16;
    public const byte Version = 1;
    public const int MaximumChunk = 127;

    public static byte[] Encode(ArduinoPacket packet)
    {
        var bytes = new byte[FrameLength];
        bytes[0] = 0xA5; bytes[1] = 0x5A; bytes[2] = Version; bytes[3] = (byte)packet.Command;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), packet.Session);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), packet.Sequence);
        bytes[12] = unchecked((byte)packet.X); bytes[13] = unchecked((byte)packet.Y);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), Crc(bytes.AsSpan(0, 14)));
        return bytes;
    }

    public static bool TryDecode(ReadOnlySpan<byte> bytes, out ArduinoPacket packet)
    {
        packet = default;
        if (bytes.Length != FrameLength || bytes[0] != 0xA5 || bytes[1] != 0x5A || bytes[2] != Version ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..]) != Crc(bytes[..14])) return false;
        packet = new((ArduinoCommand)bytes[3], BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]), unchecked((sbyte)bytes[12]), unchecked((sbyte)bytes[13]));
        return true;
    }

    internal static ushort Crc(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0xFFFF;
        foreach (var value in bytes)
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++) crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }
}
