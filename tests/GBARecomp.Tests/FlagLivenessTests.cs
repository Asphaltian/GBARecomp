using static GBARecomp.Tests.TestCode;

namespace GBARecomp.Tests;

public class FlagLivenessTests
{
    private static Dictionary<uint, StatusFlags> Compute(ushort[] code)
    {
        var context = CreateContext(code, [new Symbol(Base, (uint)code.Length * 2, "Flags")]);
        return FlagLiveness.Compute(FunctionAnalysis.Analyze(context, context.Functions[0]));
    }

    [Fact]
    public void FlagsOverwrittenBeforeTheyAreReadAreDead()
    {
        var live = Compute(
        [
            0x1840, // 00: adds r0, r0, r1
            0x2800, // 02: cmp r0, #0
            0xD000, // 04: beq 0x08
            0x4770, // 06: bx lr
            0x4770, // 08: bx lr
        ]);

        Assert.Equal(StatusFlags.None, live[Base]);
    }

    [Fact]
    public void FlagsAreLiveWhenTheFunctionReturns()
    {
        var live = Compute(
        [
            0x1840, // 00: adds r0, r0, r1
            0x4770, // 02: bx lr
        ]);

        Assert.Equal(StatusFlags.All, live[Base]);
    }

    [Fact]
    public void AnAddWithCarryKeepsTheCarryBeforeItLive()
    {
        var live = Compute(
        [
            0x1840, // 00: adds r0, r0, r1
            0x4150, // 02: adcs r0, r2
            0x2000, // 04: movs r0, #0
            0x2800, // 06: cmp r0, #0
            0x4770, // 08: bx lr
        ]);

        Assert.Equal(StatusFlags.C, live[Base]);
        Assert.Equal(StatusFlags.None, live[Base + 2]);
    }
}
