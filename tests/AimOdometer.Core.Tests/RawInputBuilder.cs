using System.Buffers.Binary;

namespace AimOdometer.Core.Tests;

/// <summary>Builds RAWINPUT records with the 64-bit Windows layout for tests.</summary>
internal sealed class RawInputBuilder
{
    public const int RecordSize = 48; // RAWINPUTHEADER (24) + RAWMOUSE (24)

    private readonly List<byte[]> _records = [];

    public int Count => _records.Count;

    public RawInputBuilder Move(nint device, int dx, int dy, ushort flags = 0, uint extra = 0) =>
        Add(device, flags, buttons: 0, buttonData: 0, dx, dy, extra);

    public RawInputBuilder Buttons(nint device, ushort buttons, short buttonData = 0) =>
        Add(device, 0, buttons, buttonData, 0, 0, 0);

    public RawInputBuilder Keyboard()
    {
        var record = new byte[RecordSize];
        BinaryPrimitives.WriteUInt32LittleEndian(record, 1); // RIM_TYPEKEYBOARD
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), RecordSize);
        _records.Add(record);
        return this;
    }

    public byte[] Build() => _records.SelectMany(r => r).ToArray();

    private RawInputBuilder Add(nint device, ushort flags, ushort buttons, short buttonData, int dx, int dy, uint extra)
    {
        var r = new byte[RecordSize];
        var s = r.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(s, 0); // RIM_TYPEMOUSE
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..], RecordSize);
        BinaryPrimitives.WriteInt64LittleEndian(s[8..], device);
        var m = s[24..];
        BinaryPrimitives.WriteUInt16LittleEndian(m, flags);
        BinaryPrimitives.WriteUInt16LittleEndian(m[4..], buttons);
        BinaryPrimitives.WriteInt16LittleEndian(m[6..], buttonData);
        BinaryPrimitives.WriteInt32LittleEndian(m[12..], dx);
        BinaryPrimitives.WriteInt32LittleEndian(m[16..], dy);
        BinaryPrimitives.WriteUInt32LittleEndian(m[20..], extra);
        _records.Add(r);
        return this;
    }
}
