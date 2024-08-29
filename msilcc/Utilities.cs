using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Msilcc;

public static class Utilities
{
    public static T AssertNotNull<T>(this T? tee)
        where T : class
        {
            Debug.Assert(tee is not null);
            return tee;
        }

    public static T AssertNotNull<T>(this T? tee)
        where T : struct
    {
        Debug.Assert(tee.HasValue);
        return tee.Value;
    }

    [DoesNotReturn]
    public static void ErrorAt(Location loc, string error)
    {
        if (Debugger.IsAttached) Debugger.Break();
        try
        {
            (Range line, int LineNumber, Range errorrange) = loc.FindLineAndPos();
            ReadOnlySpan<char> linestr = loc.InputText.Span[line];
            string file = $"{loc.Filename}:{LineNumber}: ";
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.Write(file);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Error.WriteLine(linestr);
            (int offset, int length) = errorrange.GetOffsetAndLength(linestr.Length);
            offset += file.Length;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            for (int i = 0; i < (offset + length); i++)
            {
                if (i < offset || i >= offset + length)
                    Console.Write(' ');
                else
                    Console.Write('^');
            }
            Console.ForegroundColor = ConsoleColor.White;
            Console.Error.Write(" ");
            Console.Error.WriteLine(error);
        }
        catch (InvalidOperationException)
        {
            string file = $"{loc.Filename}: ";
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.Write(file);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Error.WriteLine("<<FAILED TO PARSE ERROR LOCATION>>");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Error.WriteLine(error);
        }
        Environment.Exit(1);
    }

    public static void Log(string log)
    {
        Console.Error.WriteLine(log);
    }

    public static Range Subtract(this Range a, int amount, int length)
    {
        int start = a.Start.GetOffset(length) - amount;
        int end = a.End.GetOffset(length) - amount;
        Debug.Assert(start >= 0 && end >= 0);
        return start..end;
    }

    public static Range Add(this Range a, int amount, int length)
    {
        int start = a.Start.GetOffset(length) + amount;
        int end = a.End.GetOffset(length) + amount;
        Debug.Assert(start >= 0 && end >= 0);
        return start..end;
    }

    public static bool Overlaps(this Range left, Range right, int length)
    {
        var (ll, lr) = left.GetOffsetAndLength(length);
        lr = ll+lr;
        var (rl, rr) = right.GetOffsetAndLength(length);
        rr = rl + rr;
        return lr > rl && ll < rr;
    }

    [DoesNotReturn]
    public static void ErrorToken(Token tok, string error) => ErrorAt(tok.Location, error);

    public static bool Contains<T>(T lowest, T check, T highest)
        where T : IComparisonOperators<T, T, bool>
    {
        return check >= lowest && check <= highest;
    }
}