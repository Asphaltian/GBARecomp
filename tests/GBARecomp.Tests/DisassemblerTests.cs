using GBARecomp.ARM;

namespace GBARecomp.Tests;

public class DisassemblerTests
{
    [Theory]
    [InlineData(0x00910002u, "addeqs r0, r1, r2")]
    [InlineData(0xE1B00101u, "movs r0, r1, lsl #2")]
    [InlineData(0x00100291u, "muleqs r0, r1, r2")]
    [InlineData(0x18B00006u, "ldmneia r0!, {r1, r2}")]
    [InlineData(0x05D10004u, "ldreqb r0, [r1, #0x4]")]
    [InlineData(0xEB00003Eu, "bl 0x08000100")]
    [InlineData(0xEF000005u, "swi 0x5")]
    public void ARMInstructions(uint encoding, string text)
    {
        Assert.Equal(text, Disassembler.Format(ARMDecoder.Decode(TestCode.Base, encoding)));
    }

    [Theory]
    [InlineData((ushort)0xB510, "stmdb sp!, {r4, lr}")]
    [InlineData((ushort)0x4802, "ldr r0, [pc, #0x8]")]
    [InlineData((ushort)0xDE00, ".hword 0xde00")]
    public void ThumbInstructions(ushort encoding, string text)
    {
        Assert.Equal(text, Disassembler.Format(ThumbDecoder.Decode(TestCode.Base, encoding, 0)));
    }
}
