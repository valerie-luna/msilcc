using Msilcc.Metadata;
using Msilcc.Types;

namespace Msilcc.Parser;

public record VariableExpression(Location Location, CDefinition Variable) : Expression(Location), ILValue
{
    public override CType Type(IMetadataResolver res) => Variable.Type;

    public override void Visit(INodeVisitor visitor) => visitor.VisitVariable(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitVariable(this);
}
