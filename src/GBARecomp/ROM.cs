using System.Buffers.Binary;

namespace GBARecomp;

internal sealed class ROM(byte[] data)
{
    public const uint BaseAddress = 0x08000000;

    public bool Contains(uint address, uint size)
    {
        return address >= BaseAddress && (ulong)address - BaseAddress + size <= (ulong)data.Length;
    }

    public ushort ReadUInt16(uint address)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan((int)(address - BaseAddress)));
    }

    public uint ReadUInt32(uint address)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)(address - BaseAddress)));
    }

    public ushort ReadOpcode16(uint address)
    {
        return Contains(address, 2) ? ReadUInt16(address) : (ushort)(address >> 1);
    }

    public void Write(uint address, uint value, int size)
    {
        var bytes = data.AsSpan((int)(address - BaseAddress), size);
        if (size == 2)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        }
    }
}
