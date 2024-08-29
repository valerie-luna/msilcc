using System.Collections.Immutable;
using Msilcc.Metadata;
using Msilcc.Types;

namespace Msilcc.Parser;

public record FunctionCall(Location Location, string FunctionIdentifier, 
    FunctionReference Reference, ImmutableArray<Expression> Args) : Expression(Location)
{
    public override CType Type(IMetadataResolver res) => Reference.FunctionType.ReturnType;

    public override void Visit(INodeVisitor visitor) => visitor.VisitFunctionCall(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitFunctionCall(this);
}