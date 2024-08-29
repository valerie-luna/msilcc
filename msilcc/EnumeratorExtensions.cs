using System.Diagnostics;

namespace Msilcc;

public static class EnumeratorExtensions
{
    public static void Skip(this IEnumerator<Token> Enumerator, string str)
    {
        if (!Enumerator.Current.EqualTo(str))
            Utilities.ErrorToken(Enumerator.Current, $"expected {str}");
        Enumerator.MoveNext();
    }

    public static bool EqualToConsume(this IEnumerator<Token> Enumerator, string str)
    {
        if (Enumerator.Current.EqualTo(str))
        {
            Enumerator.MoveNext();
            return true;
        }
        return false;
    }

    public static void Consume(this IEnumerator<Token> Enumerator)
    {
        bool b = Enumerator.MoveNext();
        Debug.Assert(b);
    }
}
