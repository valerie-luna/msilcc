using Msilcc.Metadata;
using Msilcc.Types;

namespace Msilcc.Parser;

public record AddressOfExpression(Location Location, ILValue Node) : Expression(Location)
{
    public override CType Type(IMetadataResolver res) => Node.Type(res).PointerTo;

    public override void Visit(INodeVisitor visitor) => visitor.VisitAddressOf(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitAddressOf(this);
}