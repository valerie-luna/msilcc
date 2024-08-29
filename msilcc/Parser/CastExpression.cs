using Msilcc.Metadata;
using Msilcc.Types;

namespace Msilcc.Parser;

public record CastExpression(Location Location, Expression Expression, CType CastTo) : Expression(Location)
{
    public override CType Type(IMetadataResolver resolver) => CastTo;

    public override void Visit(INodeVisitor visitor) => visitor.VisitCast(this);

    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitCast(this);
}