using Msilcc.Metadata;
using Msilcc.Types;

namespace Msilcc.Parser;

public abstract record Expression(Location Location) : Node(Location)
{
    public abstract CType Type(IMetadataResolver resolver);
}
