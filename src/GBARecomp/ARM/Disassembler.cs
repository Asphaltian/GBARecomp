using System.Text;

namespace GBARecomp.ARM;

internal static class Disassembler
{
    private static readonly string[] RegisterNames =
    [
        "r0", "r1", "r2", "r3", "r4", "r5", "r6", "r7",
        "r8", "r9", "r10", "r11", "r12", "sp", "lr", "pc",
    ];

    public static string Format(Instruction i)
    {
        string condition = i.Condition == Condition.AL ? "" : i.Condition.ToString().ToLowerInvariant();
        string mnemonic = i.Opcode.ToString().ToLowerInvariant();
        string s = i.SetsFlags ? "s" : "";
        string rd = RegisterNames[i.Rd];
        string rn = RegisterNames[i.Rn];
        string rm = RegisterNames[i.Rm];
        string rs = RegisterNames[i.Rs];

        switch (i.Opcode)
        {
            case Opcode.Mov or Opcode.Mvn:
                return $"{mnemonic}{condition}{s} {rd}, {FormatOperand(i)}";

            case Opcode.Tst or Opcode.Teq or Opcode.Cmp or Opcode.Cmn:
                return $"{mnemonic}{condition} {rn}, {FormatOperand(i)}";

            case <= Opcode.Mvn:
                return $"{mnemonic}{condition}{s} {rd}, {rn}, {FormatOperand(i)}";

            case Opcode.Mul:
                return $"{mnemonic}{condition}{s} {rd}, {rm}, {rs}";

            case Opcode.Mla:
                return $"{mnemonic}{condition}{s} {rd}, {rm}, {rs}, {rn}";

            case Opcode.Umull or Opcode.Umlal or Opcode.Smull or Opcode.Smlal:
                return $"{mnemonic}{condition}{s} {rn}, {rd}, {rm}, {rs}";

            case Opcode.B or Opcode.Bl:
                return $"{mnemonic}{condition} 0x{i.Target:x8}";

            case Opcode.Bx:
                return $"{mnemonic}{condition} {rm}";

            case Opcode.Ldr or Opcode.Str:
                return $"{mnemonic}{condition} {rd}, {FormatAddress(i)}";

            case Opcode.Ldrb or Opcode.Ldrh or Opcode.Ldrsb or Opcode.Ldrsh or Opcode.Strb or Opcode.Strh:
                return $"{mnemonic[..3]}{condition}{mnemonic[3..]} {rd}, {FormatAddress(i)}";

            case Opcode.Ldm or Opcode.Stm:
                string mode = (i.AddOffset, i.PreIndexed) switch
                {
                    (true, false) => "ia",
                    (true, true) => "ib",
                    (false, false) => "da",
                    (false, true) => "db",
                };
                return $"{mnemonic}{condition}{mode} {rn}{(i.WriteBack ? "!" : "")}, "
                    + $"{FormatRegisterList(i.RegisterList)}{(i.PSROrUserBank ? "^" : "")}";

            case Opcode.Swp:
                return $"swp{condition} {rd}, {rm}, [{rn}]";

            case Opcode.Swpb:
                return $"swp{condition}b {rd}, {rm}, [{rn}]";

            case Opcode.Mrs:
                return $"mrs{condition} {rd}, {(i.UsesSPSR ? "spsr" : "cpsr")}";

            case Opcode.Msr:
                return $"msr{condition} {(i.UsesSPSR ? "spsr" : "cpsr")}_{FormatPSRFields(i.PSRFieldMask)}, {FormatOperand(i)}";

            case Opcode.Swi:
                return $"swi{condition} 0x{i.Immediate:x}";

            default:
                return i.IsThumb && i.Size == 2 ? $".hword 0x{i.Encoding:x4}" : $".word 0x{i.Encoding:x8}";
        }
    }

    private static string FormatOperand(Instruction i)
    {
        return i.OperandKind switch
        {
            OperandKind.Immediate => $"#0x{i.Immediate:x}",
            OperandKind.RegisterShift => $"{RegisterNames[i.Rm]}, {FormatShiftType(i.ShiftType)} {RegisterNames[i.Rs]}",
            _ => FormatShiftedRegister(i),
        };
    }

    private static string FormatShiftedRegister(Instruction i)
    {
        string rm = RegisterNames[i.Rm];
        if (i.ShiftType == ShiftType.RRX)
        {
            return $"{rm}, rrx";
        }

        return i.ShiftAmount == 0 ? rm : $"{rm}, {FormatShiftType(i.ShiftType)} #{i.ShiftAmount}";
    }

    private static string FormatAddress(Instruction i)
    {
        string sign = i.AddOffset ? "" : "-";
        string offset = i.OperandKind == OperandKind.Immediate
            ? $"#{sign}0x{i.Immediate:x}"
            : sign + FormatShiftedRegister(i);
        string rn = RegisterNames[i.Rn];

        if (!i.PreIndexed)
        {
            return $"[{rn}], {offset}";
        }

        if (i.OperandKind == OperandKind.Immediate && i.Immediate == 0 && !i.WriteBack)
        {
            return $"[{rn}]";
        }

        return $"[{rn}, {offset}]{(i.WriteBack ? "!" : "")}";
    }

    private static string FormatRegisterList(ushort registerList)
    {
        var builder = new StringBuilder("{");
        for (int first = 0; first < 16; first++)
        {
            if ((registerList & (1 << first)) == 0)
            {
                continue;
            }

            int last = first;
            while (last < 15 && (registerList & (1 << (last + 1))) != 0)
            {
                last++;
            }

            if (builder.Length > 1)
            {
                builder.Append(", ");
            }

            builder.Append(RegisterNames[first]);
            if (last > first)
            {
                builder.Append(last == first + 1 ? ", " : "-").Append(RegisterNames[last]);
            }

            first = last;
        }

        return builder.Append('}').ToString();
    }

    private static string FormatPSRFields(byte mask)
    {
        return string.Concat("cxsf".Where((_, bit) => (mask & (1 << bit)) != 0).Reverse());
    }

    private static string FormatShiftType(ShiftType shiftType) => shiftType.ToString().ToLowerInvariant();
}
