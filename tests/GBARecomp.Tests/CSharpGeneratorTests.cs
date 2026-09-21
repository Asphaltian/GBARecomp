using static GBARecomp.Tests.TestCode;

namespace GBARecomp.Tests;

public class CSharpGeneratorTests
{
    private static readonly ushort[] ReturnZero =
    [
        0x2000, // 00: movs r0, #0
        0x4770, // 02: bx lr
    ];

    private static string Generate(Context context)
    {
        return new CSharpGenerator(context).Generate(FunctionAnalysis.Analyze(context, context.Functions[0]));
    }

    [Fact]
    public void DataNamesAreValidAndUnique()
    {
        var context = CreateContext(
            ReturnZero,
            [new Symbol(Base, 4, "Zero"), new Symbol(0x02000000, 4, "gValue"), new Symbol(0x02000004, 4, "gValue"), new Symbol(0x02000008, 0, "3.table")]);

        string data = new CSharpGenerator(context).GenerateData();

        Assert.Contains("    public const uint gValue = 0x02000000;\n", data);
        Assert.Contains("    public const uint gValue_02000004 = 0x02000004;\n", data);
        Assert.Contains("    public const uint _3_table = 0x02000008;\n", data);
    }

    [Fact]
    public void StubDoesNothing()
    {
        var context = CreateContext(ReturnZero, [new Symbol(Base, 4, "Zero")], patches: new PatchesConfig { Stubs = ["Zero"] });

        string method = new CSharpGenerator(context).GenerateUnrecompiled(context.Functions[0]);

        Assert.Contains("public static void Zero(RecompContext ctx)\n        {\n        }\n", method);
    }

    [Fact]
    public void IgnoredFunctionThrows()
    {
        var context = CreateContext(ReturnZero, [new Symbol(Base, 4, "Zero")], patches: new PatchesConfig { Ignored = ["Zero"] });

        string method = new CSharpGenerator(context).GenerateUnrecompiled(context.Functions[0]);

        Assert.Contains("public static void Zero(RecompContext ctx) => throw new NotImplementedException(", method);
    }

    [Fact]
    public void HookRunsBeforeItsInstruction()
    {
        var patches = new PatchesConfig { Hook = [new FunctionHook { Func = "Zero", BeforeAddress = Base + 2, Text = "Hooked(ctx);" }] };
        var context = CreateContext(ReturnZero, [new Symbol(Base, 4, "Zero")], patches: patches);

        string method = Generate(context);

        Assert.True(method.IndexOf("// 08000000:") < method.IndexOf("Hooked(ctx);"));
        Assert.True(method.IndexOf("Hooked(ctx);") < method.IndexOf("// 08000002:"));
    }

    [Fact]
    public void HookAtNoInstructionIsAnError()
    {
        var patches = new PatchesConfig { Hook = [new FunctionHook { Func = "Zero", BeforeAddress = Base + 4, Text = "Hooked(ctx);" }] };
        var context = CreateContext(
            [
                .. ReturnZero,
                0x2000, // 04: movs r0, #0
                0x4770, // 06: bx lr
            ],
            [new Symbol(Base, 8, "Zero")],
            patches: patches);

        Assert.Single(FunctionAnalysis.Analyze(context, context.Functions[0]).Errors);
    }

    [Fact]
    public void TraceEntryAndStartHookComeFirst()
    {
        var input = new InputConfig { TraceMode = true };
        var patches = new PatchesConfig { Hook = [new FunctionHook { Func = "Zero", Text = "Hooked(ctx);" }] };
        var context = CreateContext(ReturnZero, [new Symbol(Base, 4, "Zero")], input, patches);

        string method = Generate(context);

        Assert.Contains("{\n            TraceEntry(\"Zero\");\n            Hooked(ctx);\n", method);
        Assert.Contains("    static partial void TraceEntry(string function);\n", new CSharpGenerator(context).GenerateTables());
    }

    [Fact]
    public void ASWIFetchesFromTheBIOSAndAgainOnTheWayBack()
    {
        var context = CreateContext(
            [
                0xDF02, // 00: swi 0x02
                0x4770, // 02: bx lr
            ],
            [new Symbol(Base, 4, "Halt")]);

        string method = Generate(context);

        Assert.Contains("Scheduler.Cycles += 2;\n                Recomp.SWI(ctx, 0x02);\n                Scheduler.Cycles += CPUTiming.ROMFetch(1, 1, 0, 0, 0);\n", method);
    }

    [Fact]
    public void AReturnFetchesWhereItLands()
    {
        var context = CreateContext(ReturnZero, [new Symbol(Base, 4, "Zero")]);

        Assert.Contains("Scheduler.Cycles += CPUTiming.JumpCycles(target, (target & 1) != 0);", Generate(context));
    }

    [Fact]
    public void WritingTheCPSRControlFieldChecksForInterrupts()
    {
        var input = new InputConfig { ARMFuncs = ["EnableIRQs"] };
        var context = CreateContext(
            [
                0xF000, 0xE121, // 00: msr cpsr_c, r0
                0xFF1E, 0xE12F, // 04: bx lr
            ],
            [new Symbol(Base, 8, "EnableIRQs")],
            input);

        Assert.Contains("ctx.WriteCPSR(ctx.R0, 0b0001);\n                if (Scheduler.Cycles >= Scheduler.NextEvent) Recomp.HandleEvents(ctx);\n", Generate(context));
    }

    [Fact]
    public void ASkippedStoreOnlyChargesItsNonSequentialFetchWhenItRuns()
    {
        var input = new InputConfig { ARMFuncs = ["Store"] };
        var context = CreateContext(
            [
                0x0000, 0x1581, // 00: strne r0, [r1]
                0xFF1E, 0xE12F, // 04: bx lr
            ],
            [new Symbol(Base, 8, "Store")],
            input);

        string method = Generate(context);

        Assert.Contains("Scheduler.Cycles += CPUTiming.ROMFetch(2, 0, 2, 0, 0);", method);
        Assert.Contains("if (!ctx.Z)\n            {\n                Scheduler.Cycles += CPUTiming.ROMFetch(-1, 1, 0, 0, 0);\n", method);
    }

    [Fact]
    public void ASkippedLiteralLoadOnlyReadsItsLiteralWhenItRuns()
    {
        var input = new InputConfig { ARMFuncs = ["Load"] };
        var context = CreateContext(
            [
                0x0000, 0x159F, // 00: ldrne r0, [pc]
                0xFF1E, 0xE12F, // 04: bx lr
                0x5678, 0x1234, // 08: .word 0x12345678
            ],
            [new Symbol(Base, 12, "Load")],
            input);

        string method = Generate(context);

        Assert.Contains("Scheduler.Cycles += CPUTiming.ROMFetch(2, 0, 2, 0, 0);", method);
        Assert.Contains("if (!ctx.Z)\n            {\n                Scheduler.Cycles += 1 + CPUTiming.ROMFetch(0, 1, 0, 0, 1);\n", method);
    }

    [Fact]
    public void RecompIncludeStartsEveryFile()
    {
        var input = new InputConfig { RecompInclude = "using MyRuntime;" };
        var context = CreateContext(ReturnZero, [new Symbol(Base, 4, "Zero")], input);

        Assert.Contains("\nusing MyRuntime;\n\nnamespace RecompiledFuncs;\n", new CSharpGenerator(context).GenerateFile([]));
    }
}
