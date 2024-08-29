using Msilcc.Metadata;
using Msilcc.Types;

namespace Msilcc.Parser;

public record LiteralStringExpression(Location Location, byte[] Value) : Expression(Location)
{
    public override CType Type(IMetadataResolver resolver) => resolver.GetBaseType(BaseType.UInt8).PointerTo;

    public override void Visit(INodeVisitor visitor) => visitor.VisitLiteralString(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitLiteralString(this);
}