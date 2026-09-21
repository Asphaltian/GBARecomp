using static GBARecomp.ARM.Registers;

namespace GBARecomp.ARM;

internal static class ThumbDecoder
{
    public static Instruction Decode(uint address, ushort encoding, ushort next)
    {
        var instruction = new Instruction
        {
            Address = address,
            Encoding = encoding,
            Size = 2,
            IsThumb = true,
            Condition = Condition.AL,
        };

        return (encoding >> 11) switch
        {
            0b00000 or 0b00001 or 0b00010 => DecodeShift(instruction, encoding),
            0b00011 => DecodeAddSubtract(instruction, encoding),
            0b00100 or 0b00101 or 0b00110 or 0b00111 => DecodeImmediate(instruction, encoding),
            0b01000 when (encoding & 0x0400) == 0 => DecodeALU(instruction, encoding),
            0b01000 => DecodeHighRegister(instruction, encoding),
            0b01001 => instruction with
            {
                Opcode = Opcode.Ldr,
                Rd = Register(encoding, 8),
                Rn = PC,
                OperandKind = OperandKind.Immediate,
                Immediate = (uint)(encoding & 0xFF) << 2,
                PreIndexed = true,
                AddOffset = true,
            },
            0b01010 or 0b01011 => DecodeRegisterOffsetTransfer(instruction, encoding),
            0b01100 or 0b01101 or 0b01110 or 0b01111 => DecodeImmediateOffsetTransfer(instruction, encoding),
            0b10000 or 0b10001 => instruction with
            {
                Opcode = Bit(encoding, 11) ? Opcode.Ldrh : Opcode.Strh,
                Rd = Register(encoding, 0),
                Rn = Register(encoding, 3),
                OperandKind = OperandKind.Immediate,
                Immediate = (uint)((encoding >> 6) & 0x1F) << 1,
                PreIndexed = true,
                AddOffset = true,
            },
            0b10010 or 0b10011 => instruction with
            {
                Opcode = Bit(encoding, 11) ? Opcode.Ldr : Opcode.Str,
                Rd = Register(encoding, 8),
                Rn = SP,
                OperandKind = OperandKind.Immediate,
                Immediate = (uint)(encoding & 0xFF) << 2,
                PreIndexed = true,
                AddOffset = true,
            },
            0b10100 or 0b10101 => instruction with
            {
                Opcode = Opcode.Add,
                Rd = Register(encoding, 8),
                Rn = Bit(encoding, 11) ? SP : PC,
                OperandKind = OperandKind.Immediate,
                Immediate = (uint)(encoding & 0xFF) << 2,
            },
            0b10110 or 0b10111 => DecodeMiscellaneous(instruction, encoding),
            0b11000 or 0b11001 => instruction with
            {
                Opcode = Bit(encoding, 11) ? Opcode.Ldm : Opcode.Stm,
                Rn = Register(encoding, 8),
                RegisterList = (ushort)(encoding & 0xFF),
                AddOffset = true,
                WriteBack = true,
            },
            0b11010 or 0b11011 => DecodeConditionalBranch(instruction, encoding),
            0b11100 => instruction with
            {
                Opcode = Opcode.B,
                Target = address + 4 + (uint)((encoding << 21) >> 20),
            },
            0b11110 => DecodeBranchWithLink(instruction, encoding, next),
            _ => instruction with { Opcode = Opcode.Undefined },
        };
    }

    private static Instruction DecodeShift(Instruction instruction, ushort encoding)
    {
        var shiftType = (ShiftType)((encoding >> 11) & 3);
        int amount = (encoding >> 6) & 0x1F;
        if (amount == 0 && shiftType != ShiftType.LSL)
        {
            amount = 32;
        }

        return instruction with
        {
            Opcode = Opcode.Mov,
            SetsFlags = true,
            Rd = Register(encoding, 0),
            OperandKind = OperandKind.ImmediateShift,
            Rm = Register(encoding, 3),
            ShiftType = shiftType,
            ShiftAmount = (byte)amount,
        };
    }

    private static Instruction DecodeAddSubtract(Instruction instruction, ushort encoding)
    {
        instruction = instruction with
        {
            Opcode = Bit(encoding, 9) ? Opcode.Sub : Opcode.Add,
            SetsFlags = true,
            Rd = Register(encoding, 0),
            Rn = Register(encoding, 3),
        };

        if (Bit(encoding, 10))
        {
            return instruction with
            {
                OperandKind = OperandKind.Immediate,
                Immediate = (uint)(encoding >> 6) & 7,
            };
        }

        return instruction with
        {
            OperandKind = OperandKind.ImmediateShift,
            Rm = Register(encoding, 6),
        };
    }

    private static Instruction DecodeImmediate(Instruction instruction, ushort encoding)
    {
        var opcode = ((encoding >> 11) & 3) switch
        {
            0 => Opcode.Mov,
            1 => Opcode.Cmp,
            2 => Opcode.Add,
            _ => Opcode.Sub,
        };

        byte rd = Register(encoding, 8);
        return instruction with
        {
            Opcode = opcode,
            SetsFlags = true,
            Rd = rd,
            Rn = rd,
            OperandKind = OperandKind.Immediate,
            Immediate = (uint)encoding & 0xFF,
        };
    }

    private static Instruction DecodeALU(Instruction instruction, ushort encoding)
    {
        byte rd = Register(encoding, 0);
        byte rs = Register(encoding, 3);
        int operation = (encoding >> 6) & 0xF;

        instruction = instruction with { SetsFlags = true, Rd = rd };

        switch (operation)
        {
            case 0x2: // LSL
            case 0x3: // LSR
            case 0x4: // ASR
            case 0x7: // ROR
                return instruction with
                {
                    Opcode = Opcode.Mov,
                    OperandKind = OperandKind.RegisterShift,
                    Rm = rd,
                    Rs = rs,
                    ShiftType = operation switch
                    {
                        0x2 => ShiftType.LSL,
                        0x3 => ShiftType.LSR,
                        0x4 => ShiftType.ASR,
                        _ => ShiftType.ROR,
                    },
                };

            case 0x9: // NEG
                return instruction with
                {
                    Opcode = Opcode.Rsb,
                    Rn = rs,
                    OperandKind = OperandKind.Immediate,
                };

            case 0xD: // MUL
                return instruction with { Opcode = Opcode.Mul, Rm = rs, Rs = rd };
        }

        var opcode = operation switch
        {
            0x0 => Opcode.And,
            0x1 => Opcode.Eor,
            0x5 => Opcode.Adc,
            0x6 => Opcode.Sbc,
            0x8 => Opcode.Tst,
            0xA => Opcode.Cmp,
            0xB => Opcode.Cmn,
            0xC => Opcode.Orr,
            0xE => Opcode.Bic,
            _ => Opcode.Mvn,
        };

        return instruction with
        {
            Opcode = opcode,
            Rn = rd,
            OperandKind = OperandKind.ImmediateShift,
            Rm = rs,
        };
    }

    private static Instruction DecodeHighRegister(Instruction instruction, ushort encoding)
    {
        byte rd = (byte)(((encoding >> 4) & 8) | (encoding & 7));
        byte rs = (byte)((encoding >> 3) & 0xF);
        int operation = (encoding >> 8) & 3;

        if (operation != 3 && rd < 8 && rs < 8)
        {
            return instruction with { Opcode = Opcode.Undefined };
        }

        switch (operation)
        {
            case 0:
                instruction = instruction with { Opcode = Opcode.Add, Rd = rd, Rn = rd };
                break;
            case 1:
                instruction = instruction with { Opcode = Opcode.Cmp, SetsFlags = true, Rn = rd };
                break;
            case 2:
                instruction = instruction with { Opcode = Opcode.Mov, Rd = rd };
                break;
            default:
                return Bit(encoding, 7)
                    ? instruction with { Opcode = Opcode.Undefined }
                    : instruction with { Opcode = Opcode.Bx, Rm = rs };
        }

        return instruction with { OperandKind = OperandKind.ImmediateShift, Rm = rs };
    }

    private static Instruction DecodeRegisterOffsetTransfer(Instruction instruction, ushort encoding)
    {
        var opcode = ((encoding >> 9) & 7) switch
        {
            0 => Opcode.Str,
            1 => Opcode.Strh,
            2 => Opcode.Strb,
            3 => Opcode.Ldrsb,
            4 => Opcode.Ldr,
            5 => Opcode.Ldrh,
            6 => Opcode.Ldrb,
            _ => Opcode.Ldrsh,
        };

        return instruction with
        {
            Opcode = opcode,
            Rd = Register(encoding, 0),
            Rn = Register(encoding, 3),
            OperandKind = OperandKind.ImmediateShift,
            Rm = Register(encoding, 6),
            PreIndexed = true,
            AddOffset = true,
        };
    }

    private static Instruction DecodeImmediateOffsetTransfer(Instruction instruction, ushort encoding)
    {
        bool isByte = Bit(encoding, 12);
        bool load = Bit(encoding, 11);
        uint offset = (uint)(encoding >> 6) & 0x1F;

        return instruction with
        {
            Opcode = (load, isByte) switch
            {
                (false, false) => Opcode.Str,
                (false, true) => Opcode.Strb,
                (true, false) => Opcode.Ldr,
                (true, true) => Opcode.Ldrb,
            },
            Rd = Register(encoding, 0),
            Rn = Register(encoding, 3),
            OperandKind = OperandKind.Immediate,
            Immediate = isByte ? offset : offset << 2,
            PreIndexed = true,
            AddOffset = true,
        };
    }

    private static Instruction DecodeMiscellaneous(Instruction instruction, ushort encoding)
    {
        if ((encoding & 0xFF00) == 0xB000)
        {
            return instruction with
            {
                Opcode = Bit(encoding, 7) ? Opcode.Sub : Opcode.Add,
                Rd = SP,
                Rn = SP,
                OperandKind = OperandKind.Immediate,
                Immediate = (uint)(encoding & 0x7F) << 2,
            };
        }

        if ((encoding & 0xF600) == 0xB400)
        {
            bool pop = Bit(encoding, 11);
            int registerList = encoding & 0xFF;
            if (Bit(encoding, 8))
            {
                registerList |= 1 << (pop ? PC : LR);
            }

            return instruction with
            {
                Opcode = pop ? Opcode.Ldm : Opcode.Stm,
                Rn = SP,
                RegisterList = (ushort)registerList,
                PreIndexed = !pop,
                AddOffset = pop,
                WriteBack = true,
            };
        }

        return instruction with { Opcode = Opcode.Undefined };
    }

    private static Instruction DecodeConditionalBranch(Instruction instruction, ushort encoding)
    {
        int condition = (encoding >> 8) & 0xF;
        switch (condition)
        {
            case 0xE:
                return instruction with { Opcode = Opcode.Undefined };
            case 0xF:
                return instruction with { Opcode = Opcode.Swi, Immediate = (uint)encoding & 0xFF };
        }

        return instruction with
        {
            Opcode = Opcode.B,
            Condition = (Condition)condition,
            Target = instruction.Address + 4 + (uint)((sbyte)encoding << 1),
        };
    }

    private static Instruction DecodeBranchWithLink(Instruction instruction, ushort encoding, ushort next)
    {
        if ((next & 0xF800) != 0xF800)
        {
            return instruction with { Opcode = Opcode.Undefined };
        }

        int high = (encoding << 21) >> 9;
        int low = (next & 0x7FF) << 1;
        return instruction with
        {
            Opcode = Opcode.Bl,
            Encoding = encoding | ((uint)next << 16),
            Size = 4,
            Target = instruction.Address + 4 + (uint)(high + low),
        };
    }

    private static bool Bit(ushort encoding, int bit) => ((encoding >> bit) & 1) != 0;

    private static byte Register(ushort encoding, int lowBit) => (byte)((encoding >> lowBit) & 7);
}
