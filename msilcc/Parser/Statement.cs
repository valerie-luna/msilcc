namespace Msilcc.Parser;

public abstract record Statement(Location Location) : Node(Location)
{
}
