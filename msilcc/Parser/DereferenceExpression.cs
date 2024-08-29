using Msilcc.Metadata;
using Msilcc.Types;

namespace Msilcc.Parser;

public record DereferenceExpression(Location Location, Expression Node) : Expression(Location), ILValue
{
    public override CType Type(IMetadataResolver res) => Node.Type(res) switch
    {
        
        IPtrType pt => pt.Internal,
        _ => throw new InvalidOperationException()
    };

    public override void Visit(INodeVisitor visitor) => visitor.VisitDereference(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitDereference(this);
}
