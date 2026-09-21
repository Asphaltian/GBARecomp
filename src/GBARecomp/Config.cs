using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;

namespace GBARecomp;

/// <summary>
/// The TOML config you run GBARecomp with. Each key is named after its property in snake_case, so
/// <see cref="InputConfig.ROMFilePath"/> becomes <c>rom_file_path</c>.
/// </summary>
public sealed class Config
{
    private static readonly JsonNamingPolicy NamingPolicy = JsonNamingPolicy.SnakeCaseLower;

    public InputConfig Input { get; set; } = new();

    public PatchesConfig Patches { get; set; } = new();

    /// <summary>
    /// Reads a config file. Keep in mind that every path in it is relative to the config file itself,
    /// not to where you run GBARecomp from.
    /// </summary>
    public static Config Load(string path)
    {
        string text = File.ReadAllText(path);
        var table = TomlSerializer.Deserialize<TomlTable>(text) ?? throw new InvalidDataException($"{path} is empty.");
        var unknownKeys = new List<string>();
        FindUnknownKeys(table, typeof(Config), "", unknownKeys);
        if (unknownKeys.Count > 0)
        {
            throw new InvalidDataException($"{path} has keys GBARecomp doesn't know: {string.Join(", ", unknownKeys)}.");
        }

        if (!table.TryGetValue("input", out object? inputKeys) || inputKeys is not TomlTable inputTable
            || !inputTable.ContainsKey("text_address") || !inputTable.ContainsKey("text_size"))
        {
            throw new InvalidDataException("input.text_address and input.text_size are required.");
        }

        var options = new TomlSerializerOptions { PropertyNamingPolicy = NamingPolicy };
        var config = TomlSerializer.Deserialize<Config>(text, options)!;

        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var input = config.Input;
        input.ROMFilePath = Resolve(directory, input.ROMFilePath, "input.rom_file_path");
        input.OutputFuncPath = Resolve(directory, input.OutputFuncPath, "input.output_func_path");

        if ((input.ELFPath.Length == 0) == (input.SymbolsFilePath.Length == 0))
        {
            throw new InvalidDataException("Give one of input.elf_path and input.symbols_file_path.");
        }

        if (input.ELFPath.Length > 0)
        {
            input.ELFPath = Path.GetFullPath(input.ELFPath, directory);
        }
        else
        {
            input.SymbolsFilePath = Path.GetFullPath(input.SymbolsFilePath, directory);
        }

        if (input.TextSize == 0)
        {
            throw new InvalidDataException("input.text_size has to be more than 0.");
        }

        if (input.FunctionsPerOutputFile <= 0)
        {
            throw new InvalidDataException("input.functions_per_output_file has to be more than 0.");
        }

        return config;
    }

    private static void FindUnknownKeys(TomlTable table, Type type, string path, List<string> unknownKeys)
    {
        var properties = type.GetProperties().ToDictionary(p => NamingPolicy.ConvertName(p.Name), p => p.PropertyType);
        foreach (var (key, value) in table)
        {
            string name = path.Length == 0 ? key : $"{path}.{key}";
            if (!properties.TryGetValue(key, out var propertyType))
            {
                unknownKeys.Add(name);
                continue;
            }

            var elementType = propertyType.IsGenericType ? propertyType.GetGenericArguments()[0] : propertyType;
            IEnumerable<TomlTable> children = value switch
            {
                TomlTable child => [child],
                TomlTableArray tables => tables,
                TomlArray array => array.OfType<TomlTable>(),
                _ => [],
            };

            foreach (var child in children)
            {
                FindUnknownKeys(child, value is TomlTable ? propertyType : elementType, name, unknownKeys);
            }
        }
    }

    private static string Resolve(string directory, string path, string key)
    {
        if (path.Length == 0)
        {
            throw new InvalidDataException($"{key} is required.");
        }

        return Path.GetFullPath(path, directory);
    }
}

/// <summary>The <c>[input]</c> table, which tells GBARecomp what to recompile and where to put the result.</summary>
public sealed class InputConfig
{
    /// <summary><c>rom_file_path</c>: the ROM you want to recompile.</summary>
    public string ROMFilePath { get; set; } = "";

    /// <summary>
    /// <c>elf_path</c>: an ELF file with the ROM's symbols, like the one a decompilation builds. You
    /// need either this or <c>symbols_file_path</c>.
    /// </summary>
    public string ELFPath { get; set; } = "";

    /// <summary>
    /// <c>symbols_file_path</c>: a symbol list like the <c>.sym</c> files decompilations publish, with
    /// one <c>address binding size name</c> entry per line. You need either this or <c>elf_path</c>.
    /// </summary>
    public string SymbolsFilePath { get; set; } = "";

    /// <summary><c>output_func_path</c>: the folder the generated C# files go in.</summary>
    public string OutputFuncPath { get; set; } = "";

    /// <summary><c>functions_per_output_file</c>: how many functions go in each generated file. It's 50 if you leave it out.</summary>
    public int FunctionsPerOutputFile { get; set; } = 50;

    /// <summary>
    /// <c>recomp_include</c>: the using directives at the top of every generated file. If your hooks
    /// need other namespaces, add them here.
    /// </summary>
    public string RecompInclude { get; set; } = "using AGBModern;\nusing LibRecomp;";

    /// <summary>
    /// <c>trace_mode</c>: makes every function call <c>Funcs.TraceEntry(name)</c> as it starts, which
    /// is handy for finding out what the game is doing. Don't forget to implement that partial method
    /// in the project you build the output in.
    /// </summary>
    public bool TraceMode { get; set; }

    /// <summary>
    /// <c>text_address</c>: where the game's code starts. Every symbol with a size inside the
    /// <c>text_size</c> bytes from here gets recompiled.
    /// </summary>
    public uint TextAddress { get; set; }

    /// <summary><c>text_size</c>: how many bytes of code there are, counting from <c>text_address</c>.</summary>
    public uint TextSize { get; set; }

    /// <summary>
    /// <c>arm_funcs</c>: functions that start in ARM state. Everything else starts in Thumb state,
    /// unless your ELF file says otherwise.
    /// </summary>
    public List<string> ARMFuncs { get; set; } = [];

    /// <summary><c>noreturn_funcs</c>: functions that never return. Whatever comes after a call to one of these isn't treated as code.</summary>
    public List<string> NoreturnFuncs { get; set; } = [];

    /// <summary>
    /// <c>ram_funcs</c>: functions the game copies into RAM and runs from there. Keep in mind that if
    /// you miss one, the game stops with an error as soon as it runs the copy.
    /// </summary>
    public List<string> RAMFuncs { get; set; } = [];

    /// <summary>
    /// <c>manual_funcs</c>: functions the symbols don't describe, like ones listed without a size, or
    /// code that's built to run from RAM.
    /// </summary>
    public List<ManualFunction> ManualFuncs { get; set; } = [];

    /// <summary><c>function_sizes</c>: sizes to use instead of the ones in the symbols, for when those are wrong.</summary>
    public List<FunctionSize> FunctionSizes { get; set; } = [];
}

/// <summary>One entry of <c>manual_funcs</c>.</summary>
public sealed class ManualFunction
{
    public string Name { get; set; } = "";

    public uint Address { get; set; }

    public uint Size { get; set; }

    /// <summary><c>rom_address</c>: where the function's bytes are in the ROM, if it's built to run from RAM. Leave it out otherwise.</summary>
    public uint ROMAddress { get; set; }
}

/// <summary>One entry of <c>function_sizes</c>.</summary>
public sealed class FunctionSize
{
    public string Name { get; set; } = "";

    public uint Size { get; set; }
}

/// <summary>The <c>[patches]</c> table, for changing the game while it gets recompiled.</summary>
public sealed class PatchesConfig
{
    /// <summary><c>stubs</c>: functions to recompile as empty ones that do nothing.</summary>
    public List<string> Stubs { get; set; } = [];

    /// <summary>
    /// <c>ignored</c>: functions to leave out of the recompile completely. Calling one of these throws,
    /// so make sure something replaces it through <c>Funcs.Patches</c>.
    /// </summary>
    public List<string> Ignored { get; set; } = [];

    /// <summary><c>[[patches.instruction]]</c>: instructions to change before recompiling.</summary>
    public List<InstructionPatch> Instruction { get; set; } = [];

    /// <summary><c>[[patches.hook]]</c>: your own C# code to put inside recompiled functions.</summary>
    public List<FunctionHook> Hook { get; set; } = [];
}

/// <summary>One entry of <c>[[patches.instruction]]</c>.</summary>
public sealed class InstructionPatch
{
    /// <summary><c>func</c>: the function the instruction is in.</summary>
    public string Func { get; set; } = "";

    /// <summary><c>address</c>: where the instruction is.</summary>
    public uint Address { get; set; }

    /// <summary><c>value</c>: the new instruction. It's a halfword in Thumb code and a word in ARM code.</summary>
    public uint Value { get; set; }
}

/// <summary>One entry of <c>[[patches.hook]]</c>.</summary>
public sealed class FunctionHook
{
    /// <summary><c>func</c>: the function to put your code in.</summary>
    public string Func { get; set; } = "";

    /// <summary>
    /// <c>before_address</c>: the instruction your code runs before. Leave it out and your code runs at
    /// the start of the function.
    /// </summary>
    public uint BeforeAddress { get; set; }

    /// <summary><c>text</c>: your code. The CPU's registers are in <c>ctx</c>.</summary>
    public string Text { get; set; } = "";
}
