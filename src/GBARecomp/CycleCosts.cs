using GBARecomp.ARM;
using static GBARecomp.ARM.Registers;

namespace GBARecomp;

internal enum Fetch
{
    Sequential,
    NonSequential,
    PrefetchedOrSequential,
    PrefetchedOrNonSequential,
    SequentialOrNonSequential,
}

internal sealed class CodeCycles(CodeRegion region)
{
    private const uint GamePakBlockMask = 0x1FFFF;

    private readonly int[] _halfwords = new int[5];
    private int _accesses;

    public int Internal { get; set; }

    public bool IsEmpty => _halfwords.All(count => count == 0) && _accesses == 0 && Internal == 0;

    public void Add(Fetch first, int halfwords, bool isWordAccess, uint? address = null)
    {
        _accesses += isWordAccess ? 1 : halfwords;
        var rest = first is Fetch.PrefetchedOrSequential or Fetch.PrefetchedOrNonSequential ? Fetch.PrefetchedOrSequential : Fetch.Sequential;
        for (uint n = 0; n < halfwords; n++)
        {
            var kind = n == 0 ? first : rest;
            bool startsBlock = address is { } start && region == CodeRegion.ROM && ((start + (n * 2)) & GamePakBlockMask) == 0;
            _halfwords[(int)(startsBlock ? NonSequentialAtBlockStart(kind) : kind)]++;
        }
    }

    public void Replace(Fetch from, Fetch to, uint address)
    {
        bool startsBlock = region == CodeRegion.ROM && (address & GamePakBlockMask) == 0;
        _halfwords[(int)(startsBlock ? NonSequentialAtBlockStart(from) : from)]--;
        _halfwords[(int)(startsBlock ? NonSequentialAtBlockStart(to) : to)]++;
    }

    public override string ToString()
    {
        bool fetches = _halfwords.Any(count => count != 0);
        string? fetch = !fetches ? null : region switch
        {
            CodeRegion.ROM => $"CPUTiming.ROMFetch({string.Join(", ", _halfwords)})",
            CodeRegion.EWRAM => $"CPUTiming.EWRAMFetch({_halfwords.Sum()})",
            CodeRegion.CopiedToRAM => $"CPUTiming.CopiedToRAMFetch({_halfwords.Sum()}, {_accesses})",
            _ => null,
        };

        if (fetch is not null)
        {
            return Internal == 0 ? fetch : $"{Internal} + {fetch}";
        }

        int cycles = region switch
        {
            CodeRegion.PaletteRAM or CodeRegion.VRAM => _halfwords.Sum(),
            _ => _accesses,
        };

        return $"{Internal + cycles}";
    }

    private static Fetch NonSequentialAtBlockStart(Fetch kind) => kind switch
    {
        Fetch.Sequential or Fetch.SequentialOrNonSequential => Fetch.NonSequential,
        Fetch.PrefetchedOrSequential => Fetch.PrefetchedOrNonSequential,
        _ => kind,
    };
}

internal static class CycleCosts
{
    public static Fetch FirstFetch(Instruction instruction, bool isPrefetched)
    {
        bool isStore = instruction.Opcode is Opcode.Str or Opcode.Strb or Opcode.Strh or Opcode.Stm;
        bool isSlowedWithoutPrefetch = isStore || (HasInternalCycles(instruction) && !instruction.Writes(PC));
        return (isPrefetched, isStore, isSlowedWithoutPrefetch) switch
        {
            (true, _, true) => Fetch.PrefetchedOrNonSequential,
            (true, _, false) => Fetch.PrefetchedOrSequential,
            (false, true, _) => Fetch.NonSequential,
            (false, false, true) => Fetch.SequentialOrNonSequential,
            _ => Fetch.Sequential,
        };
    }

    public static void AddJumpCycles(CodeCycles cycles, bool isThumb)
    {
        cycles.Add(Fetch.NonSequential, isThumb ? 1 : 2, isWordAccess: !isThumb);
        cycles.Add(Fetch.Sequential, isThumb ? 1 : 2, isWordAccess: !isThumb);
    }

    public static int FixedInternal(Instruction instruction)
    {
        return instruction.Opcode switch
        {
            Opcode.Mul or Opcode.Mla or Opcode.Umull or Opcode.Smull or Opcode.Umlal or Opcode.Smlal => 0,
            Opcode.Ldr or Opcode.Ldrb or Opcode.Ldrh or Opcode.Ldrsb or Opcode.Ldrsh => 1,
            Opcode.Ldm or Opcode.Swp or Opcode.Swpb => 1,
            _ => instruction.OperandKind == OperandKind.RegisterShift ? 1 : 0,
        };
    }

    public static bool HasInternalCycles(Instruction instruction)
    {
        return FixedInternal(instruction) > 0 || instruction.Opcode is >= Opcode.Mul and <= Opcode.Smlal;
    }

    public static bool AccessesMemory(Instruction instruction)
    {
        return instruction.Opcode is >= Opcode.Ldr and <= Opcode.Swpb;
    }
}
