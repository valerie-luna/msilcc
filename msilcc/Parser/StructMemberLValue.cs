using Msilcc.Metadata;
using Msilcc.Types;

namespace Msilcc.Parser;

public record StructMemberLValue(Location Location, ILValue Node, CDefinition Member) : Expression(Location), ILValue, IStructMember
{
    Expression IStructMember.Node => (Expression)Node;

    public override CType Type(IMetadataResolver resolver) => Member.Type;

    public override void Visit(INodeVisitor visitor) => visitor.VisitMemberAccessL(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitMemberAccessL(this);
}