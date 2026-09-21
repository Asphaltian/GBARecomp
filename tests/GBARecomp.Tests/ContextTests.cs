using static GBARecomp.Tests.TestCode;

namespace GBARecomp.Tests;

public class ContextTests
{
    private static readonly ushort[] CallIntoAnotherFunction =
    [
        0xB500,         // 00: push {lr}
        0xF000, 0xF802, // 02: bl 0x0a
        0xBD00,         // 06: pop {pc}
        0x2000,         // 08: movs r0, #0
        0x4770,         // 0a: bx lr
    ];

    [Fact]
    public void CalledLabelBecomesAFunctionNamedAfterIt()
    {
        var context = CreateContext(
            CallIntoAnotherFunction,
            [new Symbol(Base, 8, "Caller"), new Symbol(Base + 8, 4, "Outer"), new Symbol(Base + 0x0A, 0, "inner")]);

        context.AddStaticFunctions();

        var inner = context.FindFunction(Base + 0x0A);
        Assert.Equal(new Function("inner", Base + 0x0A, Base + 0x0A, 2, IsThumb: true, IsNoreturn: false, IsCopiedToRAM: false), inner);
        Assert.Empty(FunctionAnalysis.Analyze(context, context.Functions[0]).Errors);
    }

    [Fact]
    public void CalledAddressWithoutALabelIsNamedAfterIt()
    {
        var context = CreateContext(CallIntoAnotherFunction, [new Symbol(Base, 8, "Caller"), new Symbol(Base + 8, 4, "Outer")]);

        context.AddStaticFunctions();

        Assert.Equal("static_0800000A", context.FindFunction(Base + 0x0A)?.Name);
    }

    [Fact]
    public void CalledAddressBetweenFunctionsEndsAtTheNextOne()
    {
        var context = CreateContext(
            [
                0xB500,         // 00: push {lr}
                0xF000, 0xF801, // 02: bl 0x08
                0xBD00,         // 06: pop {pc}
                0x2000,         // 08: movs r0, #0
                0x4770,         // 0a: bx lr
                0x4770,         // 0c: bx lr
            ],
            [new Symbol(Base, 8, "Caller"), new Symbol(Base + 0x0C, 2, "Next")]);

        context.AddStaticFunctions();

        Assert.Equal(Base + 0x0C, context.FindFunction(Base + 8)?.End);
    }

    [Fact]
    public void CalledAddressInCodeCopiedToRAMIsCopiedToo()
    {
        var input = new InputConfig { RAMFuncs = ["Caller"] };
        var context = CreateContext(CallIntoAnotherFunction, [new Symbol(Base, 8, "Caller"), new Symbol(Base + 8, 4, "Outer")], input);

        context.AddStaticFunctions();

        Assert.True(context.FindFunction(Base + 0x0A)?.IsCopiedToRAM);
    }

    [Fact]
    public void ELFFunctionWithoutASizeEndsWhereTheNextOneStarts()
    {
        var context = CreateContext(
            [0x2000, 0x4770, 0x4770],
            [new Symbol(Base, 0, "Unsized", IsThumb: true), new Symbol(Base + 4, 0, "Last", IsThumb: true)]);

        Assert.Equal([4u, 2u], context.Functions.Select(f => f.Size));
    }

    [Fact]
    public void SymbolsOutsideTheCodeAreData()
    {
        var context = CreateContext(
            CallIntoAnotherFunction,
            [
                new Symbol(Base, 8, "Caller"),
                new Symbol(Base + 8, 4, "Outer"),
                new Symbol(Base + 0x0A, 0, "inner"),
                new Symbol(Base + 0x100, 0, "sTable"),
                new Symbol(0x02000010, 4, "gCounter"),
                new Symbol(0x02000010, 4, "gCounter"),
            ]);

        Assert.Equal([new Symbol(0x02000010, 4, "gCounter"), new Symbol(Base + 0x100, 0, "sTable")], context.DataSymbols);
    }

    [Fact]
    public void CodeOutsideTheTextIsNotData()
    {
        var input = new InputConfig { ManualFuncs = [new ManualFunction { Name = "FastCopy", Address = 0x03000000, Size = 4, ROMAddress = Base + 8 }] };
        var context = CreateContext(CallIntoAnotherFunction, [new Symbol(Base, 8, "Caller"), new Symbol(0x03000000, 4, "FastCopy")], input);

        Assert.Empty(context.DataSymbols);
    }

    [Fact]
    public void FunctionSizesReplaceTheSymbolSize()
    {
        var input = new InputConfig { FunctionSizes = [new FunctionSize { Name = "Short", Size = 4 }] };

        var context = CreateContext([0x2000, 0x4770], [new Symbol(Base, 2, "Short")], input);

        Assert.Equal(4u, context.Functions[0].Size);
    }

    [Fact]
    public void InstructionPatchReplacesTheInstruction()
    {
        var patches = new PatchesConfig { Instruction = [new InstructionPatch { Func = "Zero", Address = Base, Value = 0x2001 }] };

        var context = CreateContext([0x2000, 0x4770], [new Symbol(Base, 4, "Zero")], patches: patches);

        Assert.Equal(0x2001, context.ROM.ReadUInt16(Base));
    }

    [Theory]
    [InlineData(Base + 1, 0x2001u)]
    [InlineData(Base + 4, 0x2001u)]
    [InlineData(Base, 0x12001u)]
    public void InstructionPatchHasToFitAnInstructionOfTheFunction(uint address, uint value)
    {
        var patches = new PatchesConfig { Instruction = [new InstructionPatch { Func = "Zero", Address = address, Value = value }] };

        Assert.Throws<InvalidDataException>(() => CreateContext([0x2000, 0x4770], [new Symbol(Base, 4, "Zero")], patches: patches));
    }

    [Fact]
    public void ConfigNamingAnUnknownFunctionIsAnError()
    {
        var patches = new PatchesConfig { Stubs = ["Missing"] };

        Assert.Throws<InvalidDataException>(() => CreateContext([0x4770], [new Symbol(Base, 2, "Present")], patches: patches));
    }

    [Fact]
    public void ConfigNamingTwoFunctionsWithOneNameIsAnError()
    {
        var patches = new PatchesConfig { Ignored = ["Twice"] };

        Assert.Throws<InvalidDataException>(() => CreateContext([0x4770, 0x4770], [new Symbol(Base, 2, "Twice"), new Symbol(Base + 2, 2, "Twice")], patches: patches));
    }

    [Fact]
    public void AnAliasIsTheSameFunction()
    {
        var context = CreateContext([0x2000, 0x4770], [new Symbol(Base, 4, "Original"), new Symbol(Base, 4, "Alias")]);

        Assert.Equal("Original", Assert.Single(context.Functions).Name);
    }

    [Fact]
    public void ConfigNamingAFunctionByItsAliasIsAnError()
    {
        var input = new InputConfig { NoreturnFuncs = ["Alias"] };

        var error = Assert.Throws<InvalidDataException>(() => CreateContext([0x2000, 0x4770], [new Symbol(Base, 4, "Original"), new Symbol(Base, 4, "Alias")], input));

        Assert.Contains("use Original for Alias", error.Message);
    }

    [Fact]
    public void ManualFunctionWithAnAliasNameIsAnError()
    {
        var input = new InputConfig { ManualFuncs = [new ManualFunction { Name = "Alias", Address = Base + 4, Size = 2 }] };

        Assert.Throws<InvalidDataException>(() => CreateContext([0x2000, 0x4770, 0x4770], [new Symbol(Base, 4, "Original"), new Symbol(Base, 4, "Alias")], input));
    }

    [Fact]
    public void SizingAFunctionTwiceIsAnError()
    {
        var input = new InputConfig { FunctionSizes = [new FunctionSize { Name = "Twice", Size = 2 }, new FunctionSize { Name = "Twice", Size = 4 }] };

        Assert.Throws<InvalidDataException>(() => CreateContext([0x4770, 0x4770], [new Symbol(Base, 2, "Twice")], input));
    }

    [Fact]
    public void CodePastTheEndOfTheROMIsAnError()
    {
        var input = new InputConfig { TextAddress = Base, TextSize = 0x100 };

        Assert.Throws<InvalidDataException>(() => Context.Create(new ROM(new byte[4]), [new Symbol(Base, 2, "Short")], input));
    }

    [Fact]
    public void ManualFunctionWithASymbolNameIsAnError()
    {
        var input = new InputConfig { ManualFuncs = [new ManualFunction { Name = "Taken", Address = Base + 2, Size = 2 }] };

        Assert.Throws<InvalidDataException>(() => CreateContext([0x4770, 0x4770], [new Symbol(Base, 2, "Taken")], input));
    }
}
