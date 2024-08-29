using Msilcc.Metadata;
using Msilcc.Types;

namespace Msilcc.Parser;

public record StructMemberRValue(Location Location, Expression Node, CDefinition Member) : Expression(Location), IStructMember
{
    public override CType Type(IMetadataResolver resolver) => Member.Type;

    public override void Visit(INodeVisitor visitor) => visitor.VisitMemberAccessR(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitMemberAccessR(this);
}
