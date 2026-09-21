namespace GBARecomp.ARM;

internal enum Condition : byte
{
    EQ, NE, CS, CC, MI, PL, VS, VC, HI, LS, GE, LT, GT, LE, AL, NV,
}

internal enum Opcode : byte
{
    And, Eor, Sub, Rsb, Add, Adc, Sbc, Rsc, Tst, Teq, Cmp, Cmn, Orr, Mov, Bic, Mvn,

    Mul, Mla, Umull, Umlal, Smull, Smlal,
    B, Bl, Bx,
    Ldr, Ldrb, Ldrh, Ldrsb, Ldrsh, Str, Strb, Strh,
    Ldm, Stm,
    Swp, Swpb,
    Mrs, Msr,
    Swi,
    Undefined,
}

internal static class Registers
{
    public const byte SP = 13;
    public const byte LR = 14;
    public const byte PC = 15;
}

internal enum ShiftType : byte
{
    LSL, LSR, ASR, ROR, RRX,
}

internal enum OperandKind : byte
{
    None,
    Immediate,
    ImmediateShift,
    RegisterShift,
}

internal readonly record struct Instruction
{
    public uint Address { get; init; }

    public uint Encoding { get; init; }

    public byte Size { get; init; }

    public bool IsThumb { get; init; }

    public Opcode Opcode { get; init; }

    public Condition Condition { get; init; }

    public bool SetsFlags { get; init; }

    public byte Rd { get; init; }

    public byte Rn { get; init; }

    public byte Rm { get; init; }

    public byte Rs { get; init; }

    public OperandKind OperandKind { get; init; }

    public uint Immediate { get; init; }

    public ShiftType ShiftType { get; init; }

    public byte ShiftAmount { get; init; }

    public bool PreIndexed { get; init; }

    public bool AddOffset { get; init; }

    public bool WriteBack { get; init; }

    public ushort RegisterList { get; init; }

    public bool PSROrUserBank { get; init; }

    public bool UsesSPSR { get; init; }

    public byte PSRFieldMask { get; init; }

    public uint Target { get; init; }

    public uint AlignedPC => IsThumb ? (Address + 4) & ~2u : Address + 8;

    public uint LiteralAddress => AddOffset ? AlignedPC + Immediate : AlignedPC - Immediate;

    public bool IsCompare => Opcode is Opcode.Tst or Opcode.Teq or Opcode.Cmp or Opcode.Cmn;

    public uint? PCRelativeAddress => (Opcode, Rn, OperandKind) switch
    {
        (Opcode.Add, Registers.PC, OperandKind.Immediate) => AlignedPC + Immediate,
        (Opcode.Sub, Registers.PC, OperandKind.Immediate) => AlignedPC - Immediate,
        _ => null,
    };

    public uint BIOSFunction => IsThumb ? Immediate : Immediate >> 16;

    public bool Reads(byte register)
    {
        bool readsOperand = OperandKind switch
        {
            OperandKind.ImmediateShift => Rm == register,
            OperandKind.RegisterShift => Rm == register || Rs == register,
            _ => false,
        };

        return Opcode switch
        {
            Opcode.Mov or Opcode.Mvn or Opcode.Msr => readsOperand,
            <= Opcode.Mvn => Rn == register || readsOperand,
            Opcode.Mul or Opcode.Umull or Opcode.Smull => Rm == register || Rs == register,
            Opcode.Mla => Rm == register || Rs == register || Rn == register,
            Opcode.Umlal or Opcode.Smlal => Rm == register || Rs == register || Rd == register || Rn == register,
            >= Opcode.Ldr and <= Opcode.Ldrsh => Rn == register || readsOperand,
            Opcode.Str or Opcode.Strb or Opcode.Strh => Rn == register || Rd == register || readsOperand,
            Opcode.Ldm => Rn == register,
            Opcode.Stm => Rn == register || (RegisterList & (1 << register)) != 0,
            Opcode.Swp or Opcode.Swpb => Rn == register || Rm == register,
            Opcode.Bx => Rm == register,
            _ => false,
        };
    }

    public bool Writes(byte register)
    {
        return Opcode switch
        {
            <= Opcode.Mvn => !IsCompare && Rd == register,
            Opcode.Mul or Opcode.Mla or Opcode.Mrs or Opcode.Swp or Opcode.Swpb => Rd == register,
            >= Opcode.Umull and <= Opcode.Smlal => Rd == register || Rn == register,
            Opcode.Bl => register == Registers.LR,
            >= Opcode.Ldr and <= Opcode.Ldrsh => Rd == register || (WriteBack && Rn == register),
            Opcode.Str or Opcode.Strb or Opcode.Strh or Opcode.Stm => WriteBack && Rn == register,
            Opcode.Ldm => (RegisterList & (1 << register)) != 0 || (WriteBack && Rn == register),
            _ => false,
        };
    }
}
