using GBARecomp.ARM;

namespace GBARecomp.Tests;

public class ThumbDecoderTests
{
    private static Instruction Expected(uint address, ushort encoding) => new()
    {
        Address = address,
        Encoding = encoding,
        Size = 2,
        IsThumb = true,
        Condition = Condition.AL,
    };

    private static void AssertDecodes(Instruction expected)
    {
        Assert.Equal(expected, ThumbDecoder.Decode(expected.Address, (ushort)expected.Encoding, 0));
    }

    [Fact]
    public void ShiftImmediate()
    {
        AssertDecodes(Expected(0, 0x0088) with
        {
            Opcode = Opcode.Mov,
            SetsFlags = true,
            OperandKind = OperandKind.ImmediateShift,
            Rm = 1,
            ShiftAmount = 2,
        });

        AssertDecodes(Expected(0, 0x0808) with
        {
            Opcode = Opcode.Mov,
            SetsFlags = true,
            OperandKind = OperandKind.ImmediateShift,
            Rm = 1,
            ShiftType = ShiftType.LSR,
            ShiftAmount = 32,
        });
    }

    [Fact]
    public void AddSubtract()
    {
        AssertDecodes(Expected(0x080AE2A4, 0x1889) with
        {
            Opcode = Opcode.Add,
            SetsFlags = true,
            Rd = 1,
            Rn = 1,
            OperandKind = OperandKind.ImmediateShift,
            Rm = 2,
        });

        AssertDecodes(Expected(0x080AE2C8, 0x1E67) with
        {
            Opcode = Opcode.Sub,
            SetsFlags = true,
            Rd = 7,
            Rn = 4,
            OperandKind = OperandKind.Immediate,
            Immediate = 1,
        });
    }

    [Fact]
    public void ImmediateOperations()
    {
        AssertDecodes(Expected(0x080AE296, 0x2900) with
        {
            Opcode = Opcode.Cmp,
            SetsFlags = true,
            Rd = 1,
            Rn = 1,
            OperandKind = OperandKind.Immediate,
        });

        AssertDecodes(Expected(0x080AE2A2, 0x32E4) with
        {
            Opcode = Opcode.Add,
            SetsFlags = true,
            Rd = 2,
            Rn = 2,
            OperandKind = OperandKind.Immediate,
            Immediate = 0xE4,
        });
    }

    [Fact]
    public void ALUOperations()
    {
        AssertDecodes(Expected(0x080AE27C, 0x429A) with
        {
            Opcode = Opcode.Cmp,
            SetsFlags = true,
            Rd = 2,
            Rn = 2,
            OperandKind = OperandKind.ImmediateShift,
            Rm = 3,
        });

        AssertDecodes(Expected(0x080AE2D2, 0x434A) with
        {
            Opcode = Opcode.Mul,
            SetsFlags = true,
            Rd = 2,
            Rm = 1,
            Rs = 2,
        });

        AssertDecodes(Expected(0, 0x4088) with
        {
            Opcode = Opcode.Mov,
            SetsFlags = true,
            OperandKind = OperandKind.RegisterShift,
            Rs = 1,
        });

        AssertDecodes(Expected(0, 0x4248) with
        {
            Opcode = Opcode.Rsb,
            SetsFlags = true,
            Rn = 1,
            OperandKind = OperandKind.Immediate,
        });
    }

    [Fact]
    public void HighRegisterOperations()
    {
        AssertDecodes(Expected(0x080AE288, 0x4641) with
        {
            Opcode = Opcode.Mov,
            Rd = 1,
            OperandKind = OperandKind.ImmediateShift,
            Rm = 8,
        });

        AssertDecodes(Expected(0x080AE2C0, 0x4698) with
        {
            Opcode = Opcode.Mov,
            Rd = 8,
            OperandKind = OperandKind.ImmediateShift,
            Rm = 3,
        });

        AssertDecodes(Expected(0, 0x4488) with
        {
            Opcode = Opcode.Add,
            Rd = 8,
            Rn = 8,
            OperandKind = OperandKind.ImmediateShift,
            Rm = 1,
        });

        AssertDecodes(Expected(0x080AE280, 0x4770) with { Opcode = Opcode.Bx, Rm = 14 });
    }

    [Fact]
    public void LdrPCRelative()
    {
        AssertDecodes(Expected(0x080AE274, 0x481A) with
        {
            Opcode = Opcode.Ldr,
            Rn = 15,
            OperandKind = OperandKind.Immediate,
            Immediate = 0x68,
            PreIndexed = true,
            AddOffset = true,
        });
    }

    [Fact]
    public void Transfers()
    {
        AssertDecodes(Expected(0x080AE284, 0x6003) with
        {
            Opcode = Opcode.Str,
            Rd = 3,
            OperandKind = OperandKind.Immediate,
            PreIndexed = true,
            AddOffset = true,
        });

        AssertDecodes(Expected(0x080AE294, 0x7B01) with
        {
            Opcode = Opcode.Ldrb,
            Rd = 1,
            OperandKind = OperandKind.Immediate,
            Immediate = 0xC,
            PreIndexed = true,
            AddOffset = true,
        });

        AssertDecodes(Expected(0, 0x8848) with
        {
            Opcode = Opcode.Ldrh,
            Rn = 1,
            OperandKind = OperandKind.Immediate,
            Immediate = 2,
            PreIndexed = true,
            AddOffset = true,
        });

        AssertDecodes(Expected(0, 0x5688) with
        {
            Opcode = Opcode.Ldrsb,
            Rn = 1,
            OperandKind = OperandKind.ImmediateShift,
            Rm = 2,
            PreIndexed = true,
            AddOffset = true,
        });

        AssertDecodes(Expected(0x080AE2A6, 0x9105) with
        {
            Opcode = Opcode.Str,
            Rd = 1,
            Rn = 13,
            OperandKind = OperandKind.Immediate,
            Immediate = 0x14,
            PreIndexed = true,
            AddOffset = true,
        });
    }

    [Fact]
    public void AddressGeneration()
    {
        AssertDecodes(Expected(0x080AE264, 0xA200) with
        {
            Opcode = Opcode.Add,
            Rd = 2,
            Rn = 15,
            OperandKind = OperandKind.Immediate,
        });

        AssertDecodes(Expected(0, 0xA901) with
        {
            Opcode = Opcode.Add,
            Rd = 1,
            Rn = 13,
            OperandKind = OperandKind.Immediate,
            Immediate = 4,
        });
    }

    [Fact]
    public void StackPointerAdjust()
    {
        AssertDecodes(Expected(0x080AE292, 0xB086) with
        {
            Opcode = Opcode.Sub,
            Rd = 13,
            Rn = 13,
            OperandKind = OperandKind.Immediate,
            Immediate = 0x18,
        });

        AssertDecodes(Expected(0, 0xB002) with
        {
            Opcode = Opcode.Add,
            Rd = 13,
            Rn = 13,
            OperandKind = OperandKind.Immediate,
            Immediate = 8,
        });
    }

    [Fact]
    public void PushPop()
    {
        AssertDecodes(Expected(0x080AE286, 0xB5F0) with
        {
            Opcode = Opcode.Stm,
            Rn = 13,
            RegisterList = 0x40F0,
            PreIndexed = true,
            WriteBack = true,
        });

        AssertDecodes(Expected(0, 0xBD10) with
        {
            Opcode = Opcode.Ldm,
            Rn = 13,
            RegisterList = 0x8010,
            AddOffset = true,
            WriteBack = true,
        });
    }

    [Fact]
    public void BlockTransfers()
    {
        AssertDecodes(Expected(0, 0xC107) with
        {
            Opcode = Opcode.Stm,
            Rn = 1,
            RegisterList = 7,
            AddOffset = true,
            WriteBack = true,
        });

        AssertDecodes(Expected(0, 0xC907) with
        {
            Opcode = Opcode.Ldm,
            Rn = 1,
            RegisterList = 7,
            AddOffset = true,
            WriteBack = true,
        });
    }

    [Fact]
    public void Branches()
    {
        AssertDecodes(Expected(0x080AE27E, 0xD000) with
        {
            Opcode = Opcode.B,
            Condition = Condition.EQ,
            Target = 0x080AE282,
        });

        AssertDecodes(Expected(0x080AE2CA, 0xD904) with
        {
            Opcode = Opcode.B,
            Condition = Condition.LS,
            Target = 0x080AE2D6,
        });

        AssertDecodes(Expected(0x08000100, 0xE7FE) with { Opcode = Opcode.B, Target = 0x08000100 });
    }

    [Fact]
    public void BranchWithLink()
    {
        Assert.Equal(
            new Instruction
            {
                Address = 0x080AE2B0,
                Encoding = 0xF9F1F000,
                Size = 4,
                IsThumb = true,
                Condition = Condition.AL,
                Opcode = Opcode.Bl,
                Target = 0x080AE696,
            },
            ThumbDecoder.Decode(0x080AE2B0, 0xF000, 0xF9F1));

        Assert.Equal(0x08000800u, ThumbDecoder.Decode(0x08001000, 0xF7FF, 0xFBFE).Target);
    }

    [Fact]
    public void Swi()
    {
        AssertDecodes(Expected(0, 0xDF05) with { Opcode = Opcode.Swi, Immediate = 5 });
    }

    [Theory]
    [InlineData(0xDE00, 0)]
    [InlineData(0xE800, 0)]
    [InlineData(0x4780, 0)] // blx r0
    [InlineData(0xBE00, 0)] // bkpt #0
    [InlineData(0xF000, 0x0000)]
    [InlineData(0xF800, 0)]
    [InlineData(0x4608, 0)] // mov r0, r1
    [InlineData(0x4408, 0)] // add r0, r1
    public void UndefinedEncodings(ushort encoding, ushort next)
    {
        Assert.Equal(Opcode.Undefined, ThumbDecoder.Decode(0, encoding, next).Opcode);
    }
}
