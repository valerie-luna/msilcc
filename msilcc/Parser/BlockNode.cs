using System.Collections.Immutable;

namespace Msilcc.Parser;

public record BlockStatement(Location Location, ImmutableArray<Statement> Nodes) : Statement(Location)
{
    public override void Visit(INodeVisitor visitor) => visitor.VisitBlock(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitBlock(this);
}
