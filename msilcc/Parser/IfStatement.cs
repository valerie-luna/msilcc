namespace Msilcc.Parser;

public record IfStatement(Location Location, Expression Conditional, Statement IfTrue, Statement? IfFalse) : Statement(Location)
{
    public override void Visit(INodeVisitor visitor) => visitor.VisitIf(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitIf(this);
}
