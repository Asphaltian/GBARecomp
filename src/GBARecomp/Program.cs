using GBARecomp;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: GBARecomp <config.toml>");
    return 1;
}

Config config;
Context context;
try
{
    config = Config.Load(args[0]);
    context = Context.Load(config);
}
catch (Exception e) when (e is IOException or InvalidDataException or Tomlyn.TomlException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

var generator = new CSharpGenerator(context);
var methods = new List<string>();
int errorCount = 0;

foreach (var function in context.Functions)
{
    if (!context.IsRecompiled(function))
    {
        methods.Add(generator.GenerateUnrecompiled(function));
        continue;
    }

    var analysis = FunctionAnalysis.Analyze(context, function);
    foreach (string error in analysis.Errors)
    {
        Console.Error.WriteLine($"{function.Name}: {error}");
        errorCount++;
    }

    if (analysis.Errors.Count == 0)
    {
        methods.Add(generator.Generate(analysis));
    }
}

if (errorCount > 0)
{
    Console.Error.WriteLine($"{errorCount} {(errorCount == 1 ? "error" : "errors")}. Nothing was written.");
    return 1;
}

string outputPath = config.Input.OutputFuncPath;
Directory.CreateDirectory(outputPath);

var files = methods.Chunk(config.Input.FunctionsPerOutputFile)
    .Select((chunk, i) => (Name: $"funcs_{i}.cs", Text: generator.GenerateFile(chunk)))
    .Append((Name: "func_table.cs", Text: generator.GenerateTables()))
    .Append((Name: "data.cs", Text: generator.GenerateData()))
    .ToList();

foreach (string stale in Directory.EnumerateFiles(outputPath, "funcs_*.cs").Where(path => files.All(f => f.Name != Path.GetFileName(path))))
{
    File.Delete(stale);
}

int writeCount = 0;
foreach (var (name, text) in files)
{
    string path = Path.Combine(outputPath, name);
    if (!File.Exists(path) || File.ReadAllText(path) != text)
    {
        File.WriteAllText(path, text);
        writeCount++;
    }
}

Console.WriteLine($"Recompiled {context.Functions.Count} functions into {files.Count} files in {outputPath}. {writeCount} changed.");
return 0;
