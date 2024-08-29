using Msilcc.Metadata;
using Msilcc.Types;

namespace Msilcc.Parser;

public record AssignmentExpression(Location Location, ILValue LHS, Expression RHS) : Expression(Location)
{
    public override CType Type(IMetadataResolver res) => LHS.Type(res);

    public override void Visit(INodeVisitor visitor) => visitor.VisitAssignment(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitAssignment(this);
}