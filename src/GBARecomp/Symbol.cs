using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace GBARecomp;

internal readonly record struct Symbol(uint Address, uint Size, string Name, bool? IsThumb = null)
{
    private const int SHT_SYMTAB = 2;
    private const int STT_FUNC = 2;
    private const ushort SHN_UNDEF = 0;
    private const ushort SHN_ABS = 0xFFF1;
    private const int SymbolSize = 0x10;

    public bool IsFunction => Size > 0 || IsThumb is not null;

    public static List<Symbol> ReadFile(string path)
    {
        var symbols = new List<Symbol>();
        int lineNumber = 0;

        foreach (string line in File.ReadLines(path))
        {
            lineNumber++;
            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = line.Split(' ', 4);
            if (parts.Length != 4
                || !uint.TryParse(parts[0], NumberStyles.HexNumber, null, out uint address)
                || parts[1] is not ("g" or "l")
                || !uint.TryParse(parts[2], NumberStyles.HexNumber, null, out uint size))
            {
                throw new InvalidDataException($"{path}({lineNumber}): expected \"address g|l size name\".");
            }

            symbols.Add(new Symbol(address, size, parts[3]));
        }

        return symbols;
    }

    public static List<Symbol> ReadELF(byte[] elf)
    {
        if (elf.Length < 0x34 || BinaryPrimitives.ReadUInt32BigEndian(elf) != 0x7F454C46 || elf[4] != 1 || elf[5] != 1)
        {
            throw new InvalidDataException("This is not a 32-bit little-endian ELF file.");
        }

        try
        {
            return ReadSymbolTables(elf);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidDataException("The ELF file is cut short or damaged.");
        }
    }

    private static List<Symbol> ReadSymbolTables(ReadOnlySpan<byte> span)
    {
        int sectionTable = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[0x20..]);
        int sectionSize = BinaryPrimitives.ReadUInt16LittleEndian(span[0x2E..]);
        int sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(span[0x30..]);

        var symbols = new List<Symbol>();
        for (int i = 0; i < sectionCount; i++)
        {
            var section = span[(sectionTable + (i * sectionSize))..];
            if (BinaryPrimitives.ReadUInt32LittleEndian(section[0x04..]) != SHT_SYMTAB)
            {
                continue;
            }

            int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(section[0x10..]);
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(section[0x14..]);
            int link = (int)BinaryPrimitives.ReadUInt32LittleEndian(section[0x18..]);
            int entrySize = (int)BinaryPrimitives.ReadUInt32LittleEndian(section[0x24..]);
            if (entrySize < SymbolSize)
            {
                throw new InvalidDataException("The ELF file's symbol table is damaged.");
            }

            var names = span[(int)BinaryPrimitives.ReadUInt32LittleEndian(span[(sectionTable + (link * sectionSize) + 0x10)..])..];

            for (int entry = offset; entry < offset + size; entry += entrySize)
            {
                var symbol = span[entry..];
                var name = names[(int)BinaryPrimitives.ReadUInt32LittleEndian(symbol)..];
                name = name[..name.IndexOf((byte)0)];
                uint value = BinaryPrimitives.ReadUInt32LittleEndian(symbol[0x04..]);
                bool isFunction = (symbol[0x0C] & 0xF) == STT_FUNC;
                ushort sectionIndex = BinaryPrimitives.ReadUInt16LittleEndian(symbol[0x0E..]);

                if (name.IsEmpty || name[0] == '$' || sectionIndex is SHN_UNDEF or SHN_ABS)
                {
                    continue;
                }

                symbols.Add(isFunction
                    ? new Symbol(value & ~1u, BinaryPrimitives.ReadUInt32LittleEndian(symbol[0x08..]), Encoding.UTF8.GetString(name), (value & 1) != 0)
                    : new Symbol(value, 0, Encoding.UTF8.GetString(name)));
            }
        }

        return symbols;
    }
}
