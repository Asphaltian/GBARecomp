namespace GBARecomp.Tests;

public class ConfigTests : IDisposable
{
    private const string Minimal = """
        [input]
        rom_file_path = "game.gba"
        symbols_file_path = "game.sym"
        output_func_path = "out"
        text_address = 0x08000000
        text_size = 0x100

        """;

    private readonly string _folder = Directory.CreateTempSubdirectory("config").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Write(string toml)
    {
        string path = Path.Combine(_folder, "game.toml");
        File.WriteAllText(path, toml);
        return path;
    }

    [Fact]
    public void PathsAreRelativeToTheConfig()
    {
        var config = Config.Load(Write(Minimal));

        Assert.Equal(Path.Combine(_folder, "game.gba"), config.Input.ROMFilePath);
        Assert.Equal(0x08000000u, config.Input.TextAddress);
    }

    [Fact]
    public void UnknownKeysAreErrors()
    {
        string toml = Minimal + """
            arm_func = ["Foo"]
            manual_funcs = [{ name = "Bar", adress = 0x08000000 }]

            [[patches.hook]]
            func = "Foo"
            txt = "Hooked(ctx);"
            """;

        var error = Assert.Throws<InvalidDataException>(() => Config.Load(Write(toml)));

        Assert.Contains("input.arm_func", error.Message);
        Assert.Contains("input.manual_funcs.adress", error.Message);
        Assert.Contains("patches.hook.txt", error.Message);
    }

    [Fact]
    public void TheTextAddressIsRequired()
    {
        string toml = Minimal.Replace("text_address = 0x08000000", "");

        Assert.Throws<InvalidDataException>(() => Config.Load(Write(toml)));
    }
}
