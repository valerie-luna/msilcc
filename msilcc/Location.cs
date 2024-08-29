using System.Buffers;
using System.Diagnostics;
using System.Text;

namespace Msilcc;

public readonly record struct Location(string Filename, ReadOnlyMemory<char> InputText, Range Range)
{
    public readonly ReadOnlySpan<char> Span => InputText.Span[Range];
    public bool EqualTo(string str) => Span.SequenceEqual(str);

    public override string ToString()
    {
        return $"{nameof(Location)} {{Filename = {Filename} Text = '{Span.ToString()}' }}";
    }

    public readonly Location ToEnd(Location other)
    {
        Debug.Assert(Filename == other.Filename);
        Debug.Assert(InputText.Span.SequenceEqual(other.InputText.Span));
        int start = Math.Min(
            Range.Start.GetOffset(Span.Length),
            other.Range.Start.GetOffset(Span.Length)
        );
        int end = Math.Max(
            Range.End.GetOffset(Span.Length),
            other.Range.End.GetOffset(Span.Length)
        );
        return new Location(Filename, InputText, start..end);
    }

    public readonly (int lineNumber, int startColumn, int endColumn) GetSourcePosition()
    {
        var (line, lineNumber, errorRegion) = FindLineAndPos();
        (var _, var linelength) = line.GetOffsetAndLength(InputText.Length);
        (var start, var length)= errorRegion.GetOffsetAndLength(linelength);
        return (lineNumber, start, Math.Min(start+length, linelength - 1));
    }

    // todo: multi-line
    public readonly (Range line, int lineNumber, Range errorRegion) FindLineAndPos()
    {
        SearchValues<char> search = SearchValues.Create(['\r', '\n']);

        var span = this.InputText.Span;
        var err = this.Range;
        int movedBy = 0;
        int index = span.IndexOfAny(search);
        while (index == 0)
        {
            movedBy++;
            span = span[1..];
            index = span.IndexOfAny(search);
            err = err.Subtract(1, span.Length);
        }
        Range currentLine = index == -1 ? 0.. : 0..index;
        int lineNumber = 1;
        while (span.Length != 0)
        {
            if (err.Overlaps(currentLine, span.Length))
            {
                return (currentLine.Add(movedBy, this.InputText.Length), lineNumber, err);
            }
            err = err.Subtract(index, span.Length);
            span = span[index..];
            movedBy += index;
            index = span.IndexOfAny(search);
            while (index == 0)
            {
                movedBy++;
                span = span[1..];
                index = span.IndexOfAny(search);
                err = err.Subtract(1, span.Length);
            }
            currentLine = index == -1 ? 0.. : 0..index;
            lineNumber++;
        }
        throw new InvalidOperationException();
    }
}