using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public static class MainType
{

    public unsafe static nint main()
    {
        aBytebByte aBytebByte2 = default(aBytebByte);
        byte* ptr = (byte*)(&aBytebByte2);
        nint intPtr = (nint)(void*)((long)(nint)aBytebByte2.a + (long)(nint)(void*)(0L * (long)sizeof(byte)));
        *(nint*)intPtr = (nint)6;
        nint intPtr2 = *(nint*)intPtr;
        nint intPtr3 = *(nint*)(ptr + 0L * (long)sizeof(byte));
        return (nint)0;
    }
}

[StructLayout(LayoutKind.Explicit, Size = 9)]
public struct aBytebByte
{
    [FieldOffset(1)]
    public Byte_3 a;

    [FieldOffset(4)]
    public Byte_5 b;
}

[StructLayout(LayoutKind.Explicit, Size = 3)]
public struct Byte_3
{
}
[StructLayout(LayoutKind.Explicit, Size = 5)]
public struct Byte_5
{
}