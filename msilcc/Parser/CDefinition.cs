using Msilcc.Types;

namespace Msilcc.Parser;

public enum DefinitionClass
{
    Undefined = 0,
    LocalVariable = 0b1,
    Parameter = 0b10,
    Function = 0b100,
    Global = 0b1000,
    StructMember = 0b10000,
    Typedef = 0b100000,
}

public record CDefinition(Location Location, CType Type, DefinitionClass DefClass, Location IdentifierLocation, int ScopeCount)
    : Node(Location)
{
    public CDefinition(CType Type, DefinitionClass DefClass, Location IdentifierLocation, int ScopeCount)
        : this (IdentifierLocation, Type, DefClass, IdentifierLocation, ScopeCount)
    {}
    
    public string Identifier => IdentifierLocation == default ? "<Unidentified>" : IdentifierLocation.Span.ToString();

    public string ClrIdentifier => $"{Identifier}_{ScopeCount}_{Type.GetHashCode()}";

    public override void Visit(INodeVisitor visitor) => visitor.VisitDefinition(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitDefinition(this);
}
