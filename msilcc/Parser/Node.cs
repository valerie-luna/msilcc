namespace Msilcc.Parser;

public abstract record Node(Location Location)
{
    public abstract void Visit(INodeVisitor visitor);
    public abstract T Visit<T>(NodeVisitor<T> visitor);
}
