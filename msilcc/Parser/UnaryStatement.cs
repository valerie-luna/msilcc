namespace Msilcc.Parser;

public record UnaryStatement(Location Location, UnaryStmtKind Kind, Expression Node) : Statement(Location)
{
    public override void Visit(INodeVisitor visitor) => visitor.VisitUnary(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitUnary(this);
}
