namespace Msilcc.Parser;

public record EmptyStatement(Location Location) : Statement(Location)
{
    public override void Visit(INodeVisitor visitor) => visitor.VisitEmpty(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitEmpty(this);
}