using Msilcc.Metadata;
using Msilcc.Types;

namespace Msilcc.Parser;

public record NegationExpression(Location Location, Expression Node) : Expression(Location)
{
    public override CType Type(IMetadataResolver res) => Node.Type(res);

    public override void Visit(INodeVisitor visitor) => visitor.VisitNegation(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitNegation(this);
}
