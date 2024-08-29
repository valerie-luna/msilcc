namespace Msilcc.Parser;

public record ForStatement(Location Location, Statement Initializer, Expression? Conditional, Expression? Increment, Statement Body) : Statement(Location)
{
    public override void Visit(INodeVisitor visitor) => visitor.VisitFor(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitFor(this);
}