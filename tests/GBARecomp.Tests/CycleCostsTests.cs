namespace GBARecomp.Tests;

public class CycleCostsTests
{
    private static string JumpCycles(CodeRegion region, bool isThumb)
    {
        var cycles = new CodeCycles(region);
        CycleCosts.AddJumpCycles(cycles, isThumb);
        return cycles.ToString();
    }

    [Fact]
    public void AJumpFetchesTwiceAtTheSpeedOfItsRegion()
    {
        Assert.Equal("CPUTiming.ROMFetch(1, 1, 0, 0, 0)", JumpCycles(CodeRegion.ROM, isThumb: true));
        Assert.Equal("CPUTiming.ROMFetch(3, 1, 0, 0, 0)", JumpCycles(CodeRegion.ROM, isThumb: false));
        Assert.Equal("CPUTiming.EWRAMFetch(4)", JumpCycles(CodeRegion.EWRAM, isThumb: false));
        Assert.Equal("CPUTiming.CopiedToRAMFetch(2, 2)", JumpCycles(CodeRegion.CopiedToRAM, isThumb: true));
        Assert.Equal("2", JumpCycles(CodeRegion.IWRAM, isThumb: false));
        Assert.Equal("2", JumpCycles(CodeRegion.BIOS, isThumb: false));
        Assert.Equal("4", JumpCycles(CodeRegion.VRAM, isThumb: false));
        Assert.Equal("2", JumpCycles(CodeRegion.VRAM, isThumb: true));
    }

    [Fact]
    public void AFetchAtTheStartOfA128KBlockIsNonSequential()
    {
        var cycles = new CodeCycles(CodeRegion.ROM);
        cycles.Add(Fetch.Sequential, 2, isWordAccess: true, 0x08020000);

        Assert.Equal("CPUTiming.ROMFetch(1, 1, 0, 0, 0)", cycles.ToString());
    }

    [Fact]
    public void InternalCyclesAloneNeedNoFetch()
    {
        var cycles = new CodeCycles(CodeRegion.ROM) { Internal = 3 };

        Assert.Equal("3", cycles.ToString());
    }

    [Fact]
    public void ReplacingAFetchCostsTheDifference()
    {
        var cycles = new CodeCycles(CodeRegion.ROM);
        cycles.Replace(Fetch.Sequential, Fetch.NonSequential, 0x08000004);

        Assert.Equal("CPUTiming.ROMFetch(-1, 1, 0, 0, 0)", cycles.ToString());
        Assert.False(cycles.IsEmpty);

        cycles.Replace(Fetch.NonSequential, Fetch.Sequential, 0x08000004);
        Assert.True(cycles.IsEmpty);
    }
}
