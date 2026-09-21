using GBARecomp.ARM;
using static GBARecomp.ARM.Registers;

namespace GBARecomp;

internal enum FlowKind
{
    Next,
    Branch,
    TailCall,
    Call,
    NoreturnCall,
    JumpTable,
    BranchExchange,
    IndirectCall,
    IndirectJump,
}

internal readonly record struct Flow(FlowKind Kind, uint Target = 0, uint[]? Targets = null)
{
    public IEnumerable<uint> LocalTargets => Kind switch
    {
        FlowKind.Branch or FlowKind.IndirectCall => [Target],
        FlowKind.BranchExchange => [Target & ~1u],
        FlowKind.JumpTable => Targets!,
        _ => [],
    };

    public bool FallsThrough(Instruction instruction)
    {
        return instruction.Condition != Condition.AL || Kind is FlowKind.Next or FlowKind.Call;
    }
}

internal sealed class FunctionAnalysis
{
    private const uint SoftReset = 0x00;
    private const uint HardReset = 0x26;
    private const ushort PreservedAcrossCalls = 0x0FF0;
    private const int JumpTablePathLimit = 32;

    private readonly Context _context;
    private readonly SortedDictionary<uint, Instruction> _instructions = [];
    private readonly SortedSet<uint> _labels = [];
    private readonly SortedSet<uint> _landingPads = [];
    private readonly Dictionary<uint, Flow> _flows = [];
    private readonly List<string> _errors = [];
    private readonly List<(uint Target, bool IsThumb)> _unknownTargets = [];
    private readonly Stack<(uint Address, bool IsThumb)> _pending = [];

    private FunctionAnalysis(Context context, Function function)
    {
        _context = context;
        Function = function;
    }

    public Function Function { get; }

    public IReadOnlyCollection<Instruction> Instructions => _instructions.Values;

    public IReadOnlySet<uint> Labels => _labels;

    public IReadOnlySet<uint> LandingPads => _landingPads;

    public IReadOnlyDictionary<uint, Flow> Flows => _flows;

    public Function? FallsThroughTo { get; private set; }

    public IReadOnlyList<string> Errors => _errors;

    public IReadOnlyList<(uint Target, bool IsThumb)> UnknownTargets => _unknownTargets;

    public static FunctionAnalysis Analyze(Context context, Function function)
    {
        var analysis = new FunctionAnalysis(context, function);
        analysis._pending.Push((function.Address, function.IsThumb));

        while (analysis._pending.TryPop(out var start))
        {
            analysis.FollowPath(start.Address, start.IsThumb);
        }

        analysis.CheckLiterals();
        analysis.CheckHooks();
        analysis.FindLandingPads();
        return analysis;
    }

    private void FindLandingPads()
    {
        foreach (uint address in _context.PCRelativeAddressesIn(Function))
        {
            bool startsBlock = _labels.Contains(address)
                || (_instructions.ContainsKey(address)
                    && TryGetInstructionBefore(address, out var previous)
                    && _flows.ContainsKey(previous.Address));
            if (startsBlock)
            {
                _landingPads.Add(address);
                _labels.Add(address);
            }
        }
    }

    private void CheckHooks()
    {
        foreach (var hook in _context.Hooks[Function.Name])
        {
            if (hook.BeforeAddress != 0 && !_instructions.ContainsKey(hook.BeforeAddress))
            {
                _errors.Add($"The hook before 0x{hook.BeforeAddress:X8} is not at an instruction that runs. Move it to one that does.");
            }
        }
    }

    private void CheckLiterals()
    {
        foreach (var load in _instructions.Values)
        {
            if (load is not { Opcode: Opcode.Ldr, Rn: PC, OperandKind: OperandKind.Immediate, PreIndexed: true })
            {
                continue;
            }

            uint literal = load.LiteralAddress;

            bool isDecoded = _instructions.ContainsKey(literal)
                || _instructions.ContainsKey(literal + 2)
                || (_instructions.TryGetValue(literal - 2, out var previous) && previous.Size == 4);

            if (isDecoded)
            {
                _errors.Add($"The literal at 0x{literal:X8} loaded by 0x{load.Address:X8} was also decoded as an instruction. If the call before it never returns, add that function to input.noreturn_funcs.");
            }
        }
    }

    private bool IsFarJump(Instruction instruction)
    {
        return instruction.Opcode == Opcode.Bl
            && instruction.Target > Function.Address
            && instruction.Target < Function.End
            && _context.FindFunction(instruction.Target) is null
            && !UsesReturnAddress(instruction.Target, instruction.IsThumb);
    }

    private bool UsesReturnAddress(uint start, bool isThumb)
    {
        var visited = new HashSet<uint>();
        var pending = new Stack<uint>([start]);
        while (pending.TryPop(out uint address))
        {
            while (address + (isThumb ? 2 : 4) <= Function.End && visited.Add(address))
            {
                var instruction = Decode(address, isThumb);
                if (instruction.Reads(LR))
                {
                    return true;
                }

                if (instruction.Opcode == Opcode.B && Contains(instruction.Target))
                {
                    pending.Push(instruction.Target);
                }

                bool endsPath = instruction.Opcode == Opcode.Undefined
                    || (instruction.Condition == Condition.AL
                        && (instruction.Writes(LR) || instruction.Writes(PC) || instruction.Opcode is Opcode.B or Opcode.Bx));
                if (endsPath)
                {
                    break;
                }

                address += instruction.Size;
            }
        }

        return false;
    }

    private Instruction Decode(uint address, bool isThumb)
    {
        if (!isThumb)
        {
            return ARMDecoder.Decode(address, _context.ReadUInt32(Function, address));
        }

        ushort next = address + 4 <= Function.End ? _context.ReadUInt16(Function, address + 2) : (ushort)0;
        return ThumbDecoder.Decode(address, _context.ReadUInt16(Function, address), next);
    }

    private void FollowPath(uint address, bool isThumb)
    {
        while (!_instructions.ContainsKey(address))
        {
            if (address == Function.End)
            {
                FallsThroughTo = _context.FindFunction(address);
                if (FallsThroughTo is null)
                {
                    _errors.Add("Execution runs past the end and no function starts there. If the call before it never returns, add that function to input.noreturn_funcs; otherwise fix the size with input.function_sizes.");
                }

                return;
            }

            if (address + (isThumb ? 2 : 4) > Function.End)
            {
                _errors.Add($"The instruction at 0x{address:X8} runs past the end. Fix the size with input.function_sizes.");
                return;
            }

            var instruction = Decode(address, isThumb);
            if (instruction.Opcode == Opcode.Undefined)
            {
                _errors.Add($"Undefined instruction 0x{instruction.Encoding:X} at 0x{address:X8}. If it isn't code, check input.noreturn_funcs and input.arm_funcs; if it is, replace the function through patches.ignored.");
                return;
            }

            if (Unsupported(instruction) is { } problem)
            {
                _errors.Add($"0x{address:X8} ({Disassembler.Format(instruction)}) {problem}");
                return;
            }

            _instructions.Add(address, instruction);

            var flow = Classify(instruction);
            if (flow.Kind != FlowKind.Next)
            {
                _flows.Add(address, flow);
            }

            bool targetIsThumb = flow.Kind == FlowKind.BranchExchange ? (flow.Target & 1) != 0 : isThumb;
            foreach (uint target in flow.LocalTargets)
            {
                AddLabel(target, targetIsThumb);
            }

            if (!flow.FallsThrough(instruction))
            {
                return;
            }

            address += instruction.Size;
        }
    }

    private Flow Classify(Instruction instruction)
    {
        switch (instruction.Opcode)
        {
            case Opcode.B when Contains(instruction.Target):
                return new Flow(FlowKind.Branch, instruction.Target);

            case Opcode.B:
                if (_context.FindFunction(instruction.Target) is null)
                {
                    _unknownTargets.Add((instruction.Target, instruction.IsThumb));
                    _errors.Add($"The branch at 0x{instruction.Address:X8} leaves for 0x{instruction.Target:X8}, which is not a function. Add one there with input.manual_funcs.");
                }

                return new Flow(FlowKind.TailCall, instruction.Target);

            case Opcode.Bl when IsFarJump(instruction):
                return new Flow(FlowKind.Branch, instruction.Target);

            case Opcode.Bl:
                var callee = _context.FindFunction(instruction.Target);
                if (callee is null)
                {
                    _unknownTargets.Add((instruction.Target, instruction.IsThumb));
                    _errors.Add($"The call at 0x{instruction.Address:X8} targets 0x{instruction.Target:X8}, which is not a function. Add one there with input.manual_funcs.");
                }

                return new Flow(callee is { IsNoreturn: true } ? FlowKind.NoreturnCall : FlowKind.Call, instruction.Target);

            case Opcode.Swi when instruction.BIOSFunction is SoftReset or HardReset:
                return new Flow(FlowKind.NoreturnCall);

            case Opcode.Bx:
                return FindBranchExchange(instruction) ?? FindIndirectCall(instruction) ?? new Flow(FlowKind.IndirectJump);

            case Opcode.Mov when instruction.Rd == PC:
                return FindJumpTable(instruction) ?? FindIndirectCall(instruction) ?? new Flow(FlowKind.IndirectJump);

            case var _ when instruction.Writes(PC):
                return new Flow(FlowKind.IndirectJump);

            default:
                return new Flow(FlowKind.Next);
        }
    }

    private static string? Unsupported(Instruction i)
    {
        bool usesPC = i.Opcode switch
        {
            <= Opcode.Mvn => i.OperandKind == OperandKind.RegisterShift && i.Rs == PC,
            Opcode.Mul => i.Rd == PC || i.Rs == PC || i.Rm == PC,
            >= Opcode.Mla and <= Opcode.Smlal => i.Rd == PC || i.Rn == PC || i.Rs == PC || i.Rm == PC,
            Opcode.Mrs => i.Rd == PC,
            Opcode.Msr => i.OperandKind == OperandKind.ImmediateShift && i.Rm == PC,
            Opcode.Swp or Opcode.Swpb => i.Rd == PC || i.Rn == PC || i.Rm == PC,
            >= Opcode.Ldr and <= Opcode.Strh => (i.OperandKind == OperandKind.ImmediateShift && i.Rm == PC) || (i.WriteBack && i.Rn == PC),
            Opcode.Ldm or Opcode.Stm => i.Rn == PC,
            _ => false,
        };

        if (usesPC)
        {
            return "uses R15 where the CPU only takes R0 to R14. Replace the function through patches.ignored.";
        }

        if (i.Opcode <= Opcode.Mvn && i.Rd == PC && i.SetsFlags && !i.IsCompare)
        {
            return "returns from an exception, which isn't supported. Replace the function through patches.ignored.";
        }

        if (i.Opcode is Opcode.Ldm or Opcode.Stm && i.PSROrUserBank)
        {
            return "has the S bit set, which isn't supported. Replace the function through patches.ignored.";
        }

        if (i.Opcode is Opcode.Ldm or Opcode.Stm && i.RegisterList == 0)
        {
            return "has an empty register list, which isn't supported. Replace the function through patches.ignored.";
        }

        bool computesJumpFromPC = i.Rd == PC && i.Rn == PC && i.OperandKind != OperandKind.Immediate
            && (i.Opcode is Opcode.Ldr || (i.Opcode <= Opcode.Mvn && !i.IsCompare));
        if (computesJumpFromPC)
        {
            return "jumps to an address worked out from R15. Only agbcc's Thumb jump tables are recognized, so replace the function through patches.ignored.";
        }

        return null;
    }

    private bool Contains(uint address) => address >= Function.Address && address < Function.End;

    private void AddLabel(uint address, bool isThumb)
    {
        if (!Contains(address))
        {
            return;
        }

        _labels.Add(address);
        _pending.Push((address, isThumb));
    }

    private bool TryGetPrevious(Instruction instruction, out Instruction previous)
    {
        return _instructions.TryGetValue(instruction.Address - (instruction.IsThumb ? 2u : 4u), out previous);
    }

    private Flow? FindBranchExchange(Instruction bx)
    {
        uint target;
        if (bx.Rm == PC)
        {
            target = bx.AlignedPC;
        }
        else if (TryGetPrevious(bx, out var adr)
            && adr is { Opcode: Opcode.Add, Rn: PC, OperandKind: OperandKind.Immediate }
            && adr.Rd == bx.Rm
            && adr.Condition == bx.Condition)
        {
            target = adr.AlignedPC + adr.Immediate;
        }
        else
        {
            return null;
        }

        if (!Contains(target & ~1u))
        {
            _errors.Add($"The BX at 0x{bx.Address:X8} leaves the function for 0x{target & ~1u:X8}. Fix the size with input.function_sizes.");
        }

        return new Flow(FlowKind.BranchExchange, target);
    }

    private Flow? FindIndirectCall(Instruction branch)
    {
        if (!TryGetPrevious(branch, out var link) || link.Rd != LR || link.Condition != branch.Condition)
        {
            return null;
        }

        uint returnAddress;
        if (link is { Opcode: Opcode.Mov, OperandKind: OperandKind.ImmediateShift, Rm: PC, ShiftAmount: 0 })
        {
            returnAddress = link.Address + (link.IsThumb ? 4u : 8u);
        }
        else if (link is { Opcode: Opcode.Add, Rn: PC, OperandKind: OperandKind.Immediate })
        {
            returnAddress = link.AlignedPC + link.Immediate;
        }
        else
        {
            return null;
        }

        if (!Contains(returnAddress))
        {
            _errors.Add($"The indirect call at 0x{branch.Address:X8} returns to 0x{returnAddress:X8}, outside the function. Fix the size with input.function_sizes.");
        }

        return new Flow(FlowKind.IndirectCall, returnAddress);
    }

    private Flow? FindJumpTable(Instruction movPC)
    {
        if (!movPC.IsThumb
            || FindWriter(movPC.Rm, movPC) is not { Opcode: Opcode.Ldr, OperandKind: OperandKind.Immediate, Immediate: 0 } loadEntry
            || FindWriter(loadEntry.Rn, loadEntry) is not { Opcode: Opcode.Add, OperandKind: OperandKind.ImmediateShift, ShiftType: ShiftType.LSL, ShiftAmount: 0 } addTable
            || FindTableAndIndex(addTable) is not { } table)
        {
            return null;
        }

        uint? entryCount = FindJumpTableEntryCount(table.ShiftIndex, movPC);
        if (entryCount is null)
        {
            _errors.Add($"The jump table dispatch at 0x{movPC.Address:X8} has no range check before it, so its size is unknown. Replace the function through patches.ignored.");
            return null;
        }

        if (ReadLiteral(table.LoadTable) is not { } tableAddress || tableAddress < Function.Address || tableAddress + entryCount * 4 > Function.End)
        {
            _errors.Add($"The jump table for 0x{movPC.Address:X8} is outside the function. Fix the size with input.function_sizes.");
            return null;
        }

        var targets = new uint[entryCount.Value];
        for (uint i = 0; i < targets.Length; i++)
        {
            uint target = _context.ReadUInt32(Function, tableAddress + i * 4) & ~1u;
            if (!Contains(target))
            {
                _errors.Add($"Jump table entry 0x{target:X8} at 0x{tableAddress + i * 4:X8} is outside the function. Fix the size with input.function_sizes.");
                return null;
            }

            targets[i] = target;
        }

        return new Flow(FlowKind.JumpTable, Targets: targets.Distinct().ToArray());
    }

    private (Instruction LoadTable, Instruction ShiftIndex)? FindTableAndIndex(Instruction addTable)
    {
        foreach (var (table, index) in new[] { (addTable.Rn, addTable.Rm), (addTable.Rm, addTable.Rn) })
        {
            if (FindWriter(table, addTable) is { Opcode: Opcode.Ldr, Rn: PC } loadTable && FindScaledIndex(index, addTable) is { } shiftIndex)
            {
                return (loadTable, shiftIndex);
            }
        }

        return null;
    }

    private Instruction? FindScaledIndex(byte register, Instruction before)
    {
        return FindWriter(register, before) switch
        {
            { Opcode: Opcode.Mov, OperandKind: OperandKind.ImmediateShift, ShiftType: ShiftType.LSL, ShiftAmount: 2 } shift => shift,
            { Opcode: Opcode.Mov, OperandKind: OperandKind.ImmediateShift, ShiftType: ShiftType.LSL, ShiftAmount: 0 } copy => FindScaledIndex(copy.Rm, copy),
            { Opcode: Opcode.Add, OperandKind: OperandKind.Immediate, Immediate: 0 } copy => FindScaledIndex(copy.Rn, copy),
            _ => null,
        };
    }

    private uint? FindJumpTableEntryCount(Instruction shiftIndex, Instruction movPC)
    {
        byte index = shiftIndex.Rm;
        var path = PathBefore(movPC).ToList();
        int shift = path.FindIndex(step => step.Instruction.Address == shiftIndex.Address);
        int rangeCheck = path.FindIndex(step => step.Instruction.Opcode == Opcode.B
            && step.Instruction.Condition == (step.IsTaken ? Condition.LS : Condition.HI));

        if (shift < 0
            || rangeCheck < 0
            || rangeCheck + 1 == path.Count
            || path[rangeCheck + 1].Instruction is not { Opcode: Opcode.Cmp } compare
            || compare.Rn != index)
        {
            return null;
        }

        int compared = rangeCheck + 1;
        bool indexChanges = path
            .Skip(Math.Min(shift, compared) + 1)
            .Take(Math.Abs(shift - compared) - 1)
            .Any(step => MayChange(step.Instruction, index));
        if (indexChanges || (shift > compared && shiftIndex.Rd == index))
        {
            return null;
        }

        uint? last = compare.OperandKind == OperandKind.Immediate
            ? compare.Immediate
            : FindConstant(compare.Rm, compare.Address);
        return last + 1;
    }

    private Instruction? FindWriter(byte register, Instruction before)
    {
        foreach (var (instruction, _) in PathBefore(before))
        {
            if (instruction.Writes(register))
            {
                return instruction;
            }

            if (MayChange(instruction, register))
            {
                return null;
            }
        }

        return null;
    }

    private static bool MayChange(Instruction instruction, byte register)
    {
        return instruction.Writes(register)
            || instruction.Opcode == Opcode.Swi
            || (instruction.Opcode == Opcode.Bl && (PreservedAcrossCalls & (1 << register)) == 0);
    }

    private IEnumerable<(Instruction Instruction, bool IsTaken)> PathBefore(Instruction start)
    {
        uint address = start.Address;
        for (int step = 0; step < JumpTablePathLimit && TryGetInstructionBefore(address, out var previous); step++)
        {
            bool isTaken = _flows.TryGetValue(previous.Address, out var flow) && !flow.FallsThrough(previous);
            if (isTaken
                && (flow.Kind != FlowKind.Branch
                    || !TryGetInstructionBefore(previous.Address, out previous)
                    || previous is not { Opcode: Opcode.B, Condition: not Condition.AL }
                    || previous.Target != address))
            {
                yield break;
            }

            yield return (previous, isTaken);
            address = previous.Address;
        }
    }

    private bool TryGetInstructionBefore(uint address, out Instruction previous)
    {
        return _instructions.TryGetValue(address - 2, out previous)
            || (_instructions.TryGetValue(address - 4, out previous) && previous.Size == 4);
    }

    private uint? FindConstant(byte register, uint address)
    {
        for (int step = 0; step < 4; step++)
        {
            if (!TryGetInstructionBefore(address, out var instruction))
            {
                return null;
            }

            address = instruction.Address;
            if (instruction.Opcode is Opcode.B or Opcode.Bl or Opcode.Bx or Opcode.Swi)
            {
                return null;
            }

            if (!instruction.Writes(register))
            {
                continue;
            }

            return instruction switch
            {
                { Opcode: Opcode.Ldr, Rn: PC } => ReadLiteral(instruction),
                { Opcode: Opcode.Mov, OperandKind: OperandKind.Immediate } => instruction.Immediate,
                { Opcode: Opcode.Mov, OperandKind: OperandKind.ImmediateShift, ShiftType: ShiftType.LSL }
                    when instruction.Rm == register => FindConstant(register, address) << instruction.ShiftAmount,
                _ => null,
            };
        }

        return null;
    }

    private uint? ReadLiteral(Instruction load)
    {
        uint address = load.LiteralAddress;
        return address >= Function.Address && address + 4 <= Function.End ? _context.ReadUInt32(Function, address) : null;
    }
}
