# GBARecomp

GBARecomp is a static recompiler that turns a Game Boy Advance ROM into C# code. Every function in the game becomes a C# method, which you then build and run with [GBA Modern Runtime](https://github.com/Asphaltian/GBAModernRuntime).

Keep in mind that GBARecomp needs to know where every function is, so you'll need a decompilation or disassembly of the game.

## Building

You will need the .NET 10 SDK.

```
git clone https://github.com/Asphaltian/GBARecomp
cd GBARecomp
dotnet build -c Release
```

## Running

Run GBARecomp with a TOML config for your game:

```
dotnet run --project src/GBARecomp -c Release -- game.toml
```

It generates `Funcs`, which has a method for every function in the game, and `Data`, which has the address of every other symbol. Only the files that actually changed get rewritten, so your build doesn't have to start over every time. If something can't be recompiled, GBARecomp lists every problem it found and writes nothing.

## Config

Paths in the config are relative to the config file itself. Here's an example you can start from:

```toml
[input]
rom_file_path = "game.gba"
# Or elf_path, if your decompilation builds an ELF
symbols_file_path = "game.sym"
output_func_path = "RecompiledFuncs"

# Where the game's code starts, and how many bytes of it there are
text_address = 0x080000C0
text_size = 0x100000

# Functions that start in ARM state. Everything else is Thumb, unless your ELF says otherwise
arm_funcs = ["AgbMain"]

# Functions that never return
noreturn_funcs = ["Hang"]

# Functions the game copies into RAM before it runs them
ram_funcs = ["SoundMainRAM"]

# Functions the symbols don't have, like code built to run from IWRAM
manual_funcs = [
    { name = "FastCopy", address = 0x03000000, size = 0x40, rom_address = 0x08100000 },
]

# Sizes to use when the symbols get one wrong
function_sizes = [
    { name = "SoundMain", size = 0x90 },
]

[patches]
# These become empty functions
stubs = ["DebugPrint"]

# These aren't recompiled at all, so you'll have to replace them yourself
ignored = ["StrangeSwitch"]
```
