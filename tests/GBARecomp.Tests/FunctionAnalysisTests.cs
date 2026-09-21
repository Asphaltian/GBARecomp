using GBARecomp.ARM;
using static GBARecomp.Tests.TestCode;

namespace GBARecomp.Tests;

public class FunctionAnalysisTests
{
    private static Context CreateContext(
        ushort[] code, Symbol[] symbols, string[]? noreturnFuncs = null, string[]? armFuncs = null)
    {
        var input = new InputConfig { NoreturnFuncs = [.. noreturnFuncs ?? []], ARMFuncs = [.. armFuncs ?? []] };
        return TestCode.CreateContext(code, symbols, input);
    }

    private static FunctionAnalysis Analyze(Context context)
    {
        return FunctionAnalysis.Analyze(context, context.Functions[0]);
    }

    [Fact]
    public void JumpTableJumpsToEveryEntry()
    {
        var context = CreateContext(
            [
                0x2801,         // 00: cmp r0, #1
                0xD80C,         // 02: bhi 0x1e
                0x0080,         // 04: lsls r0, r0, #2
                0x4902,         // 06: ldr r1, [pc, #8]
                0x1840,         // 08: adds r0, r0, r1
                0x6800,         // 0a: ldr r0, [r0]
                0x4687,         // 0c: mov pc, r0
                0x0000,         // 0e: padding
                0x0014, 0x0800, // 10: .word 0x08000014
                0x001E, 0x0800, // 14: .word 0x0800001e
                0x001C, 0x0800, // 18: .word 0x0800001c
                0x4770,         // 1c: bx lr
                0x4770,         // 1e: bx lr
            ],
            [new Symbol(Base, 0x20, "Switch")]);

        var analysis = Analyze(context);

        Assert.Empty(analysis.Errors);
        Assert.Equal(FlowKind.JumpTable, analysis.Flows[Base + 0x0C].Kind);
        Assert.Equal([Base + 0x1E, Base + 0x1C], analysis.Flows[Base + 0x0C].Targets!);
        Assert.Equal([Base + 0x1C, Base + 0x1E], analysis.Labels);
        Assert.Equal(9, analysis.Instructions.Count);
    }

    [Fact]
    public void JumpTableWithTheTableAddedSecondIsFound()
    {
        var context = CreateContext(
            [
                0x2801,         // 00: cmp r0, #1
                0xD900,         // 02: bls 0x06
                0xE00E,         // 04: b 0x24
                0x0080,         // 06: lsls r0, r0, #2
                0x4902,         // 08: ldr r1, [pc, #8]
                0x1841,         // 0a: adds r1, r0, r1
                0x6809,         // 0c: ldr r1, [r1]
                0x4684,         // 0e: mov ip, r0
                0x468F,         // 10: mov pc, r1
                0x0000,         // 12: padding
                0x0018, 0x0800, // 14: .word 0x08000018
                0x0022, 0x0800, // 18: .word 0x08000022
                0x0020, 0x0800, // 1c: .word 0x08000020
                0x4770,         // 20: bx lr
                0x4770,         // 22: bx lr
                0x4770,         // 24: bx lr
            ],
            [new Symbol(Base, 0x26, "Switch")]);

        var analysis = Analyze(context);

        Assert.Empty(analysis.Errors);
        Assert.Equal([Base + 0x22, Base + 0x20], analysis.Flows[Base + 0x10].Targets!);
    }

    [Fact]
    public void JumpTableIndexScaledBeforeTheRangeCheckIsFound()
    {
        var context = CreateContext(
            [
                0x00A0,         // 00: lsls r0, r4, #2
                0x4684,         // 02: mov ip, r0
                0x2C01,         // 04: cmp r4, #1
                0xD80B,         // 06: bhi 0x20
                0x4801,         // 08: ldr r0, [pc, #4]
                0x4460,         // 0a: add r0, ip
                0x6800,         // 0c: ldr r0, [r0]
                0x4687,         // 0e: mov pc, r0
                0x0014, 0x0800, // 10: .word 0x08000014
                0x001E, 0x0800, // 14: .word 0x0800001e
                0x001C, 0x0800, // 18: .word 0x0800001c
                0x4770,         // 1c: bx lr
                0x4770,         // 1e: bx lr
                0x4770,         // 20: bx lr
            ],
            [new Symbol(Base, 0x22, "Switch")]);

        var analysis = Analyze(context);

        Assert.Empty(analysis.Errors);
        Assert.Equal([Base + 0x1E, Base + 0x1C], analysis.Flows[Base + 0x0E].Targets!);
    }

    [Fact]
    public void JumpTableIndexChangedAfterScalingHasNoRangeCheck()
    {
        var context = CreateContext(
            [
                0x00A0,         // 00: lsls r0, r4, #2
                0x4684,         // 02: mov ip, r0
                0x3401,         // 04: adds r4, #1
                0x2C01,         // 06: cmp r4, #1
                0xD80A,         // 08: bhi 0x20
                0x4802,         // 0a: ldr r0, [pc, #8]
                0x4460,         // 0c: add r0, ip
                0x6800,         // 0e: ldr r0, [r0]
                0x4687,         // 10: mov pc, r0
                0x0000,         // 12: padding
                0x0018, 0x0800, // 14: .word 0x08000018
                0x0020, 0x0800, // 18: .word 0x08000020
                0x0020, 0x0800, // 1c: .word 0x08000020
                0x4770,         // 20: bx lr
            ],
            [new Symbol(Base, 0x22, "Switch")]);

        Assert.Single(Analyze(context).Errors);
    }

    [Fact]
    public void AnAddressWorkedOutFromPCIsALandingPadOnlyWhereABlockStarts()
    {
        var context = CreateContext(
            [
                0xA001, // 00: add r0, pc, #0x4
                0xA100, // 02: add r1, pc, #0x0
                0x2200, // 04: movs r2, #0
                0xE7FF, // 06: b 0x08
                0x4770, // 08: bx lr
            ],
            [new Symbol(Base, 0x0A, "Test")]);
        context.FindPCRelativeAddresses();

        Assert.Equal([Base + 8], Analyze(context).LandingPads);
    }

    [Fact]
    public void JumpTableEntriesDropTheThumbBit()
    {
        var context = CreateContext(
            [
                0x2801,         // 00: cmp r0, #1
                0xD80C,         // 02: bhi 0x1e
                0x0080,         // 04: lsls r0, r0, #2
                0x4902,         // 06: ldr r1, [pc, #8]
                0x1840,         // 08: adds r0, r0, r1
                0x6800,         // 0a: ldr r0, [r0]
                0x4687,         // 0c: mov pc, r0
                0x0000,         // 0e: padding
                0x0014, 0x0800, // 10: .word 0x08000014
                0x001F, 0x0800, // 14: .word 0x0800001f
                0x001D, 0x0800, // 18: .word 0x0800001d
                0x4770,         // 1c: bx lr
                0x4770,         // 1e: bx lr
            ],
            [new Symbol(Base, 0x20, "Switch")]);

        var analysis = Analyze(context);

        Assert.Empty(analysis.Errors);
        Assert.Equal([Base + 0x1E, Base + 0x1C], analysis.Flows[Base + 0x0C].Targets!);
    }

    [Fact]
    public void JumpTableWithoutRangeCheckIsAnError()
    {
        var context = CreateContext(
            [
                0x0080,         // 00: lsls r0, r0, #2
                0x4902,         // 02: ldr r1, [pc, #8]
                0x1840,         // 04: adds r0, r0, r1
                0x6800,         // 06: ldr r0, [r0]
                0x4687,         // 08: mov pc, r0
                0x0000,         // 0a: padding
                0x0010, 0x0800, // 0c: .word 0x08000010
                0x0014, 0x0800, // 10: .word 0x08000014
                0x4770,         // 14: bx lr
            ],
            [new Symbol(Base, 0x16, "Switch")]);

        Assert.Single(Analyze(context).Errors);
    }

    [Fact]
    public void JumpTableBoundLoadedByAPopIsUnknown()
    {
        var context = CreateContext(
            [
                0x2101,         // 00: movs r1, #1
                0xBC02,         // 02: pop {r1}
                0x4288,         // 04: cmp r0, r1
                0xD80C,         // 06: bhi 0x22
                0x0080,         // 08: lsls r0, r0, #2
                0x4902,         // 0a: ldr r1, [pc, #8]
                0x1840,         // 0c: adds r0, r0, r1
                0x6800,         // 0e: ldr r0, [r0]
                0x4687,         // 10: mov pc, r0
                0x0000,         // 12: padding
                0x0018, 0x0800, // 14: .word 0x08000018
                0x0022, 0x0800, // 18: .word 0x08000022
                0x0020, 0x0800, // 1c: .word 0x08000020
                0x4770,         // 20: bx lr
                0x4770,         // 22: bx lr
            ],
            [new Symbol(Base, 0x24, "Switch")]);

        Assert.Single(Analyze(context).Errors);
    }

    [Fact]
    public void CodeDoesNotContinueAfterASoftReset()
    {
        var context = CreateContext(
            [
                0xDF00,         // 00: swi 0x00
                0xE800,         // 02: .hword 0xe800
            ],
            [new Symbol(Base, 4, "Reset")]);

        var analysis = Analyze(context);

        Assert.Empty(analysis.Errors);
        Assert.Single(analysis.Instructions);
    }

    [Fact]
    public void BranchWithLinkInsideTheFunctionIsAJump()
    {
        var context = CreateContext(
            [
                0xB500,         // 00: push {lr}
                0xF000, 0xF803, // 02: bl 0x0c
                0xDE00,         // 06: undefined
                0xDE00,         // 08: undefined
                0xDE00,         // 0a: undefined
                0xBD00,         // 0c: pop {pc}
            ],
            [new Symbol(Base, 0x0E, "FarJump")]);

        var analysis = Analyze(context);

        Assert.Empty(analysis.Errors);
        Assert.Equal(new Flow(FlowKind.Branch, Base + 0x0C), analysis.Flows[Base + 0x02]);
        Assert.Equal([Base + 0x0C], analysis.Labels);
        Assert.Equal([Base, Base + 0x02, Base + 0x0C], analysis.Instructions.Select(i => i.Address));
    }

    [Fact]
    public void BranchWithLinkInsideTheFunctionToCodeThatUsesTheReturnAddressIsACall()
    {
        var context = CreateContext(
            [
                0xB500,         // 00: push {lr}
                0xF000, 0xF801, // 02: bl 0x08
                0xBD00,         // 06: pop {pc}
                0x4770,         // 08: bx lr
            ],
            [new Symbol(Base, 0x0A, "Caller")]);

        var analysis = Analyze(context);

        Assert.Equal(new Flow(FlowKind.Call, Base + 8), analysis.Flows[Base + 2]);
        Assert.Equal([(Base + 8, true)], analysis.UnknownTargets);
    }

    [Fact]
    public void BranchWithLinkToAFunctionInsideTheFunctionIsACall()
    {
        var context = CreateContext(
            [
                0xB500,         // 00: push {lr}
                0xF000, 0xF801, // 02: bl 0x08
                0xBD00,         // 06: pop {pc}
                0x4718,         // 08: bx r3
            ],
            [new Symbol(Base, 0x0A, "Caller"), new Symbol(Base + 8, 2, "call_r3")]);

        var analysis = Analyze(context);

        Assert.Empty(analysis.Errors);
        Assert.Equal(new Flow(FlowKind.Call, Base + 8), analysis.Flows[Base + 2]);
    }

    [Fact]
    public void CodeDoesNotContinueAfterANoreturnCall()
    {
        ushort[] code =
        [
            0xF000, 0xF802, // 00: bl 0x08
            0xDE00,         // 04: undefined
            0xDE00,         // 06: undefined
            0xE7FE,         // 08: b .
        ];
        Symbol[] symbols = [new Symbol(Base, 8, "Caller"), new Symbol(Base + 8, 2, "Abort")];

        Assert.Empty(Analyze(CreateContext(code, symbols, ["Abort"])).Errors);
        Assert.Single(Analyze(CreateContext(code, symbols)).Errors);
    }

    [Fact]
    public void BranchExchangeToALabelSwitchesState()
    {
        var context = CreateContext(
            [
                0xA000,         // 00: adr r0, 0x04
                0x4700,         // 02: bx r0
                0xFF1E, 0xE12F, // 04: bx lr (ARM)
            ],
            [new Symbol(Base, 8, "ThumbToARM")]);

        var analysis = Analyze(context);

        Assert.Empty(analysis.Errors);
        Assert.Equal(new Flow(FlowKind.BranchExchange, Base + 4), analysis.Flows[Base + 2]);

        var last = analysis.Instructions.Last();
        Assert.False(last.IsThumb);
        Assert.Equal(Opcode.Bx, last.Opcode);
    }

    [Fact]
    public void BXPCSwitchesToARMAtTheNextWord()
    {
        var context = CreateContext(
            [
                0x4778,         // 00: bx pc
                0x46C0,         // 02: mov r8, r8
                0xFF1E, 0xE12F, // 04: bx lr (ARM)
            ],
            [new Symbol(Base, 8, "ThumbToARM")]);

        var analysis = Analyze(context);

        Assert.Empty(analysis.Errors);
        Assert.Equal(new Flow(FlowKind.BranchExchange, Base + 4), analysis.Flows[Base]);
        Assert.False(analysis.Instructions.Last().IsThumb);
    }

    [Theory]
    [InlineData(0x019F, 0xE000)] // mul r0, pc, r1
    [InlineData(0x0F11, 0xE1A0)] // mov r0, r1, lsl pc
    [InlineData(0xF000, 0xE10F)] // mrs pc, cpsr
    [InlineData(0x0092, 0xE10F)] // swp r0, r2, [pc]
    [InlineData(0x0004, 0xE5BF)] // ldr r0, [pc, #4]!
    [InlineData(0xF00E, 0xE1B0)] // movs pc, lr
    [InlineData(0x0002, 0xE8D0)] // ldmia r0, {r1}^
    [InlineData(0x0000, 0xE890)] // ldmia r0, {}
    [InlineData(0xF100, 0xE08F)] // add pc, pc, r0, lsl #2
    public void UnsupportedARMInstructionsAreErrors(ushort low, ushort high)
    {
        var context = CreateContext([low, high, 0xFF1E, 0xE12F], [new Symbol(Base, 8, "Unsupported")], armFuncs: ["Unsupported"]);

        Assert.Single(Analyze(context).Errors);
    }

    [Fact]
    public void SettingLRBeforeBXIsACall()
    {
        var context = CreateContext(
            [
                0xE00F, 0xE1A0, // 00: mov lr, pc
                0xFF11, 0xE12F, // 04: bx r1
                0xFF1E, 0xE12F, // 08: bx lr
            ],
            [new Symbol(Base, 0x0C, "CallsPointer")],
            armFuncs: ["CallsPointer"]);

        var analysis = Analyze(context);

        Assert.Empty(analysis.Errors);
        Assert.Equal(new Flow(FlowKind.IndirectCall, Base + 8), analysis.Flows[Base + 4]);
        Assert.Equal(new Flow(FlowKind.IndirectJump), analysis.Flows[Base + 8]);
    }

    [Fact]
    public void FallingOffTheEndContinuesInTheNextFunction()
    {
        var context = CreateContext(
            [
                0x2000, // 00: movs r0, #0
                0x4770, // 02: bx lr
            ],
            [new Symbol(Base, 2, "First"), new Symbol(Base + 2, 2, "Second")]);

        var analysis = Analyze(context);

        Assert.Empty(analysis.Errors);
        Assert.Equal("Second", analysis.FallsThroughTo?.Name);
    }

    [Fact]
    public void LiteralDecodedAsAnInstructionIsAnError()
    {
        var context = CreateContext(
            [
                0x4800,         // 00: ldr r0, [pc, #0]
                0x2000,         // 02: movs r0, #0
                0x2000, 0x2000, // 04: literal
                0x4770,         // 08: bx lr
            ],
            [new Symbol(Base, 0x0A, "MissingReturn")]);

        Assert.Single(Analyze(context).Errors);
    }
}
