namespace GBARecomp.Tests;

public class SymbolTests
{
    private static byte[] ReadData(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name));

    [Fact]
    public void ELFSymbolsSayWhichFunctionsAreThumb()
    {
        var symbols = Symbol.ReadELF(ReadData("elf_test.elf"));

        Assert.Equal(
            [
                new Symbol(0x08000010, 4, "ThumbHelper", IsThumb: true),
                new Symbol(0x0800000E, 0, "local_label"),
                new Symbol(0x08000008, 8, "ThumbMain", IsThumb: true),
                new Symbol(0x08000000, 8, "ARMStart", IsThumb: false),
                new Symbol(0x08000014, 0, "SomeData"),
            ],
            symbols);
    }

    [Fact]
    public void AbsoluteELFSymbolsAreSkipped()
    {
        Assert.DoesNotContain(Symbol.ReadELF(ReadData("elf_test.elf")), s => s.Name == "SomeConstant");
    }

    [Fact]
    public void ELFFunctionsAnalyzeInTheirOwnState()
    {
        var input = new InputConfig { TextAddress = ROM.BaseAddress, TextSize = 0x14 };
        var context = Context.Create(new ROM(ReadData("elf_test.gba")), Symbol.ReadELF(ReadData("elf_test.elf")), input);

        Assert.Equal(["ARMStart", "ThumbMain", "ThumbHelper"], context.Functions.Select(f => f.Name));
        Assert.Equal([false, true, true], context.Functions.Select(f => f.IsThumb));
        Assert.All(context.Functions, f => Assert.Empty(FunctionAnalysis.Analyze(context, f).Errors));
    }

    [Fact]
    public void NonELFFileIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Symbol.ReadELF(ReadData("elf_test.gba")));
    }

    [Fact]
    public void CutShortELFFileIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Symbol.ReadELF(ReadData("elf_test.elf")[..0x100]));
    }

    [Fact]
    public void SymbolFileNeedsAddressBindingSizeAndName()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "08000000 g 00000010 Main\n\n08000010 l 00000000 label\n");
            Assert.Equal([new Symbol(0x08000000, 0x10, "Main"), new Symbol(0x08000010, 0, "label")], Symbol.ReadFile(path));

            File.WriteAllText(path, "08000000 x 00000010 Main\n");
            Assert.Throws<InvalidDataException>(() => Symbol.ReadFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
