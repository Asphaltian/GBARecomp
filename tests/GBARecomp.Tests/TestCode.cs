using System.Buffers.Binary;

namespace GBARecomp.Tests;

internal static class TestCode
{
    public const uint Base = ROM.BaseAddress;

    public static Context CreateContext(ushort[] code, Symbol[] symbols, InputConfig? input = null, PatchesConfig? patches = null)
    {
        byte[] data = new byte[code.Length * 2];
        for (int i = 0; i < code.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * 2), code[i]);
        }

        input ??= new InputConfig();
        input.TextAddress = Base;
        input.TextSize = (uint)data.Length;
        return Context.Create(new ROM(data), symbols, input, patches);
    }
}
