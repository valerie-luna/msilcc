namespace Msilcc.Parser;

public interface IStructMember
{
    Expression Node { get; }
    CDefinition Member { get; }
}
