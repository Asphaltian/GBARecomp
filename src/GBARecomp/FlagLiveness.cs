using GBARecomp.ARM;

namespace GBARecomp;

[Flags]
internal enum StatusFlags : byte
{
    None = 0,
    N = 1,
    Z = 2,
    C = 4,
    V = 8,
    All = N | Z | C | V,
}

internal static class FlagLiveness
{
    public static Dictionary<uint, StatusFlags> Compute(FunctionAnalysis analysis)
    {
        var instructions = analysis.Instructions.ToArray();
        var liveIn = instructions.ToDictionary(i => i.Address, _ => StatusFlags.None);
        var liveOut = instructions.ToDictionary(i => i.Address, _ => StatusFlags.None);

        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int n = instructions.Length - 1; n >= 0; n--)
            {
                var instruction = instructions[n];
                var flow = analysis.Flows.GetValueOrDefault(instruction.Address);

                var live = StatusFlags.None;
                foreach (uint successor in Successors(instruction, flow))
                {
                    live |= liveIn.GetValueOrDefault(successor, StatusFlags.All);
                }

                if (flow.Kind is FlowKind.Call or FlowKind.NoreturnCall or FlowKind.TailCall
                    or FlowKind.IndirectCall or FlowKind.IndirectJump)
                {
                    live = StatusFlags.All;
                }

                var kills = instruction.Condition == Condition.AL ? Writes(instruction) : StatusFlags.None;
                var newLiveIn = Reads(instruction) | (live & ~kills);

                if (live != liveOut[instruction.Address] || newLiveIn != liveIn[instruction.Address])
                {
                    liveOut[instruction.Address] = live;
                    liveIn[instruction.Address] = newLiveIn;
                    changed = true;
                }
            }
        }

        return liveOut;
    }

    public static StatusFlags Writes(Instruction instruction)
    {
        if (instruction.Opcode == Opcode.Msr && !instruction.UsesSPSR && (instruction.PSRFieldMask & 8) != 0)
        {
            return StatusFlags.All;
        }

        if (!instruction.SetsFlags)
        {
            return StatusFlags.None;
        }

        switch (instruction.Opcode)
        {
            case Opcode.Sub or Opcode.Rsb or Opcode.Add or Opcode.Adc or Opcode.Sbc or Opcode.Rsc or Opcode.Cmp or Opcode.Cmn:
                return StatusFlags.All;

            case <= Opcode.Mvn:
                return StatusFlags.N | StatusFlags.Z | (HasShifterCarry(instruction) ? StatusFlags.C : StatusFlags.None);

            default:
                return StatusFlags.N | StatusFlags.Z;
        }
    }

    public static bool HasShifterCarry(Instruction instruction)
    {
        return instruction.OperandKind is OperandKind.Immediate or OperandKind.ImmediateShift && instruction.ShiftAmount != 0;
    }

    private static StatusFlags Reads(Instruction instruction)
    {
        var flags = instruction.Condition switch
        {
            Condition.EQ or Condition.NE => StatusFlags.Z,
            Condition.CS or Condition.CC => StatusFlags.C,
            Condition.MI or Condition.PL => StatusFlags.N,
            Condition.VS or Condition.VC => StatusFlags.V,
            Condition.HI or Condition.LS => StatusFlags.C | StatusFlags.Z,
            Condition.GE or Condition.LT => StatusFlags.N | StatusFlags.V,
            Condition.GT or Condition.LE => StatusFlags.N | StatusFlags.Z | StatusFlags.V,
            _ => StatusFlags.None,
        };

        if (instruction.Opcode is Opcode.Adc or Opcode.Sbc or Opcode.Rsc
            || (instruction.OperandKind == OperandKind.ImmediateShift && instruction.ShiftType == ShiftType.RRX))
        {
            flags |= StatusFlags.C;
        }

        if (instruction.SetsFlags && instruction.OperandKind == OperandKind.RegisterShift && instruction.Opcode <= Opcode.Mvn)
        {
            flags |= StatusFlags.C;
        }

        if (instruction.Opcode == Opcode.Mrs && !instruction.UsesSPSR)
        {
            flags = StatusFlags.All;
        }

        return flags;
    }

    private static IEnumerable<uint> Successors(Instruction instruction, Flow flow)
    {
        if (flow.FallsThrough(instruction))
        {
            yield return instruction.Address + instruction.Size;
        }

        foreach (uint target in flow.LocalTargets)
        {
            yield return target;
        }
    }
}
