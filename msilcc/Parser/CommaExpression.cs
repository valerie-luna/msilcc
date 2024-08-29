using Msilcc.Metadata;
using Msilcc.Types;

namespace Msilcc.Parser;

public record CommaExpression(Location Location, Expression LHS, Expression RHS) : Expression(Location), ILValue
{
    public override CType Type(IMetadataResolver resolver) => RHS.Type(resolver);

    public override void Visit(INodeVisitor visitor) => visitor.VisitComma(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitComma(this);
}
