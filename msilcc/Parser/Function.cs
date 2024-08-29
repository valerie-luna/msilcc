using System.Collections.Immutable;
using Msilcc.Types;

namespace Msilcc.Parser;

public record FunctionDeclaration(Location Location, FunctionType FuncType, Location IdentifierLocation)
    : CDefinition(Location, FuncType, DefinitionClass.Function, IdentifierLocation, 0)
{
    public override void Visit(INodeVisitor visitor) => visitor.VisitFunctionDeclaration(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitFunctionDeclaration(this);
    public FunctionReference MakeRef => new FunctionReference(Identifier, FuncType, null);
}

public record Function(Location Location, FunctionType FuncType, Location IdentifierLocation,
    BlockStatement Body, ImmutableArray<CDefinition> Locals) 
    : FunctionDeclaration(Location, FuncType, IdentifierLocation)
{
    public override void Visit(INodeVisitor visitor) => visitor.VisitFunction(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitFunction(this);
}
