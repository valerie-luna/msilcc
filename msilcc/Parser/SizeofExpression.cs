using Msilcc.Metadata;
using Msilcc.Types;

namespace Msilcc.Parser;

public record SizeofExpression(Location Location, Expression? Expression, CType CType) : Expression(Location)
{
    public override CType Type(IMetadataResolver res) => res.GetBaseType(BaseType.Int32);

    public override void Visit(INodeVisitor visitor) => visitor.VisitSizeof(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitSizeof(this);
}
