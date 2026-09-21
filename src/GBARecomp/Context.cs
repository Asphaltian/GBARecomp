namespace GBARecomp;

internal enum CodeRegion
{
    BIOS,
    EWRAM,
    IWRAM,
    PaletteRAM,
    VRAM,
    OAM,
    ROM,
    CopiedToRAM,
}

internal sealed record Function(
    string Name, uint Address, uint ROMAddress, uint Size, bool IsThumb, bool IsNoreturn, bool IsCopiedToRAM)
{
    public CodeRegion Region => IsCopiedToRAM ? CodeRegion.CopiedToRAM : RegionOf(Address);

    public bool RunsFromROM => Region == CodeRegion.ROM;

    public bool IsInROM => RegionOf(Address) == CodeRegion.ROM;

    public uint End => Address + Size;

    private static CodeRegion RegionOf(uint address) => (address >> 24) switch
    {
        0x0 => CodeRegion.BIOS,
        0x2 => CodeRegion.EWRAM,
        0x5 => CodeRegion.PaletteRAM,
        0x6 => CodeRegion.VRAM,
        0x7 => CodeRegion.OAM,
        >= 0x8 => CodeRegion.ROM,
        _ => CodeRegion.IWRAM,
    };
}

internal sealed class Context
{
    private readonly List<Function> _functions;
    private readonly Dictionary<uint, Function> _functionsByAddress;
    private readonly Function[] _declaredFunctions;
    private readonly Dictionary<uint, string> _labels;
    private readonly SortedSet<uint> _pcRelativeAddresses = [];
    private readonly (uint Start, uint End) _text;

    private Context(ROM rom, List<Function> functions, Dictionary<uint, string> labels, List<Symbol> dataSymbols, InputConfig input, PatchesConfig patches)
    {
        ROM = rom;
        DataSymbols = dataSymbols;
        _functions = functions;
        _functionsByAddress = functions.ToDictionary(f => f.Address);
        _declaredFunctions = [.. functions];
        _labels = labels;
        _text = (input.TextAddress, input.TextAddress + input.TextSize);
        TraceMode = input.TraceMode;
        RecompInclude = input.RecompInclude;
        Stubs = patches.Stubs.ToHashSet();
        Ignored = patches.Ignored.ToHashSet();
        Hooks = patches.Hook.ToLookup(hook => hook.Func);
    }

    public ROM ROM { get; }

    public IReadOnlyList<Function> Functions => _functions;

    public IReadOnlyList<Symbol> DataSymbols { get; }

    public bool TraceMode { get; }

    public string RecompInclude { get; }

    public IReadOnlySet<string> Stubs { get; }

    public IReadOnlySet<string> Ignored { get; }

    public ILookup<string, FunctionHook> Hooks { get; }

    public Function? FindFunction(uint address) => _functionsByAddress.GetValueOrDefault(address);

    public bool IsRecompiled(Function function) => !Stubs.Contains(function.Name) && !Ignored.Contains(function.Name);

    public IEnumerable<uint> PCRelativeAddressesIn(Function function) => _pcRelativeAddresses.GetViewBetween(function.Address + 1, function.End - 1);

    public ushort ReadUInt16(Function function, uint address) => ROM.ReadUInt16(address - function.Address + function.ROMAddress);

    public uint ReadUInt32(Function function, uint address) => ROM.ReadUInt32(address - function.Address + function.ROMAddress);

    public ushort ReadOpcode16(Function function, uint address) => ROM.ReadOpcode16(address - function.Address + function.ROMAddress);

    public static Context Load(Config config)
    {
        var input = config.Input;
        var rom = new ROM(File.ReadAllBytes(input.ROMFilePath));
        var symbols = input.ELFPath.Length > 0
            ? Symbol.ReadELF(File.ReadAllBytes(input.ELFPath))
            : Symbol.ReadFile(input.SymbolsFilePath);

        var context = Create(rom, symbols, input, config.Patches);
        context.AddStaticFunctions();
        context.FindPCRelativeAddresses();
        return context;
    }

    public static Context Create(ROM rom, IEnumerable<Symbol> symbols, InputConfig input, PatchesConfig? patches = null)
    {
        patches ??= new PatchesConfig();
        uint textEnd = input.TextAddress + input.TextSize;
        if (!rom.Contains(input.TextAddress, input.TextSize))
        {
            throw new InvalidDataException("input.text_address and input.text_size go past the end of the ROM.");
        }

        var armFuncs = input.ARMFuncs.ToHashSet();
        var noreturnFuncs = input.NoreturnFuncs.ToHashSet();
        var ramFuncs = input.RAMFuncs.ToHashSet();
        var sizedTwice = input.FunctionSizes.GroupBy(f => f.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (sizedTwice.Count > 0)
        {
            throw new InvalidDataException($"input.function_sizes has {string.Join(", ", sizedTwice)} more than once.");
        }

        var sizes = input.FunctionSizes.ToDictionary(f => f.Name, f => f.Size);
        var inText = symbols.Where(s => s.Address >= input.TextAddress && s.Address < textEnd).ToList();
        var starts = inText.Where(s => s.IsFunction).Select(s => s.Address)
            .Concat(input.ManualFuncs.Select(m => m.Address).Where(a => a >= input.TextAddress && a < textEnd))
            .Append(textEnd)
            .Distinct()
            .Order()
            .ToList();
        var sameCode = inText
            .Where(s => s.IsFunction)
            .Select(s => s.Size > 0 ? s : s with { Size = starts[starts.BinarySearch(s.Address) + 1] - s.Address })
            .GroupBy(s => (s.Address, s.Size))
            .ToList();
        var declared = sameCode.Select(g => g.First()).ToList();
        var aliases = sameCode.SelectMany(g => g.Skip(1), (g, alias) => (alias.Name, Function: g.First().Name)).ToList();

        var functions = declared
            .Select(s => (s.Name, s.Address, ROMAddress: s.Address, Size: sizes.GetValueOrDefault(s.Name, s.Size), IsThumb: s.IsThumb ?? true))
            .Concat(input.ManualFuncs.Select(m => (m.Name, m.Address, ROMAddress: m.ROMAddress == 0 ? m.Address : m.ROMAddress, m.Size, IsThumb: true)))
            .Select(f => new Function(
                f.Name,
                f.Address,
                f.ROMAddress,
                f.Size,
                IsThumb: f.IsThumb && !armFuncs.Contains(f.Name),
                IsNoreturn: noreturnFuncs.Contains(f.Name),
                IsCopiedToRAM: ramFuncs.Contains(f.Name)))
            .OrderBy(f => f.Address)
            .ToList();

        var byName = functions.ToLookup(f => f.Name);
        var configNames = armFuncs.Concat(noreturnFuncs).Concat(ramFuncs).Concat(sizes.Keys)
            .Concat(patches.Stubs).Concat(patches.Ignored)
            .Concat(patches.Instruction.Select(p => p.Func)).Concat(patches.Hook.Select(h => h.Func))
            .Distinct()
            .ToList();

        var aliasNames = aliases.Where(a => configNames.Contains(a.Name)).ToList();
        if (aliasNames.Count > 0)
        {
            throw new InvalidDataException($"The config names functions by an alias: {string.Join(", ", aliasNames.Select(a => $"use {a.Function} for {a.Name}"))}.");
        }

        var unknownNames = configNames.Where(n => !byName.Contains(n)).ToList();
        if (unknownNames.Count > 0)
        {
            throw new InvalidDataException($"The config names functions that do not exist: {string.Join(", ", unknownNames)}. Check the spelling against the symbols.");
        }

        var ambiguousNames = configNames.Where(n => byName[n].Count() > 1).ToList();
        if (ambiguousNames.Count > 0)
        {
            throw new InvalidDataException($"The config names more than one function with each of these: {string.Join(", ", ambiguousNames)}.");
        }

        var symbolNames = sameCode.SelectMany(g => g).Select(s => s.Name).ToHashSet();
        if (input.ManualFuncs.FirstOrDefault(m => symbolNames.Contains(m.Name)) is { } manual)
        {
            throw new InvalidDataException($"input.manual_funcs has {manual.Name}, which the symbols already have.");
        }

        for (int i = 0; i < functions.Count; i++)
        {
            var function = functions[i];
            if (!rom.Contains(function.ROMAddress, function.Size))
            {
                throw new InvalidDataException($"{function.Name} is outside the ROM. Check its address and size.");
            }

            if (i > 0 && functions[i - 1].Address == function.Address)
            {
                throw new InvalidDataException($"{functions[i - 1].Name} and {function.Name} have the same address.");
            }
        }

        foreach (var patch in patches.Instruction)
        {
            var function = byName[patch.Func].Single();
            int size = function.IsThumb ? 2 : 4;
            if (patch.Address < function.Address || patch.Address + size > function.End || patch.Address % size != 0)
            {
                throw new InvalidDataException($"The instruction patch at 0x{patch.Address:X8} is not an instruction of {function.Name}.");
            }

            if (function.IsThumb && patch.Value > ushort.MaxValue)
            {
                throw new InvalidDataException($"The instruction patch at 0x{patch.Address:X8} is in Thumb code, so its value has to fit in 16 bits.");
            }

            rom.Write(patch.Address - function.Address + function.ROMAddress, patch.Value, size);
        }

        foreach (var hook in patches.Hook)
        {
            var function = byName[hook.Func].Single();
            if (hook.BeforeAddress != 0 && (hook.BeforeAddress < function.Address || hook.BeforeAddress >= function.End))
            {
                throw new InvalidDataException($"The hook at 0x{hook.BeforeAddress:X8} is outside {function.Name}.");
            }
        }

        var labels = inText.Where(s => !s.IsFunction).GroupBy(s => s.Address).ToDictionary(g => g.Key, g => g.First().Name);
        var dataSymbols = symbols
            .Where(s => (s.Address < input.TextAddress || s.Address >= textEnd) && !byName.Contains(s.Name))
            .DistinctBy(s => (s.Name, s.Address))
            .OrderBy(s => s.Address)
            .ToList();

        return new Context(rom, functions, labels, dataSymbols, input, patches);
    }

    public void AddStaticFunctions()
    {
        var pending = new Queue<Function>(_functions);
        while (pending.TryDequeue(out var caller))
        {
            if (!IsRecompiled(caller))
            {
                continue;
            }

            foreach (var (target, isThumb) in FunctionAnalysis.Analyze(this, caller).UnknownTargets)
            {
                if (target < _text.Start || target >= _text.End || !caller.IsInROM || FindFunction(target) is not null)
                {
                    continue;
                }

                var function = new Function(
                    _labels.GetValueOrDefault(target, $"static_{target:X8}"),
                    target,
                    target,
                    StaticFunctionEnd(target) - target,
                    isThumb,
                    IsNoreturn: false,
                    IsCopiedToRAM: caller.IsCopiedToRAM);

                int index = _functions.BinarySearch(function, Comparer<Function>.Create((a, b) => a.Address.CompareTo(b.Address)));
                _functions.Insert(~index, function);
                _functionsByAddress.Add(target, function);
                pending.Enqueue(function);
            }
        }
    }

    public void FindPCRelativeAddresses()
    {
        foreach (var function in _functions.Where(IsRecompiled))
        {
            foreach (var instruction in FunctionAnalysis.Analyze(this, function).Instructions)
            {
                if (instruction.PCRelativeAddress is { } address)
                {
                    _pcRelativeAddresses.Add(address & ~1u);
                }
            }
        }
    }

    private uint StaticFunctionEnd(uint address)
    {
        var around = _declaredFunctions.LastOrDefault(f => f.Address < address && f.End > address && f.IsInROM);
        if (around is not null)
        {
            return around.End;
        }

        var next = _declaredFunctions.FirstOrDefault(f => f.Address > address && f.IsInROM);
        return next?.Address ?? _text.End;
    }
}
