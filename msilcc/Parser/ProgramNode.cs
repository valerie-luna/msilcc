using System.Collections.Immutable;

namespace Msilcc.Parser;

public record ProgramNode(Location Location, ImmutableArray<Node> Nodes) : Node(Location)
{
    public override void Visit(INodeVisitor visitor) => visitor.VisitProgram(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitProgram(this);
}