using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

public static unsafe partial class MainType
{
    [LibraryImport("libc")]
    private static partial int strlen(byte* str);
    public static int assert<TExpected, TActual>(TExpected expected, TActual actual, byte* code)
        where TExpected : INumber<TExpected>
        where TActual : INumber<TActual>
    {
        string str = Encoding.ASCII.GetString(code, strlen(code));
        // it's probably fine to do this
        bool equal = decimal.CreateSaturating(expected) == decimal.CreateSaturating(actual);
        if (equal) {
            Console.WriteLine($"{str} => {actual}");
        } else {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{str} => {expected} expected but got {actual}");
            Environment.Exit(1);
        }
        return 0;
    }

    public static int printf(byte* str)
    {
        string csstr = Encoding.ASCII.GetString(str, strlen(str));
        Console.WriteLine(csstr);
        return 0;
    }
}
