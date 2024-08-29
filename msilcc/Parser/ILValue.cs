using Msilcc.Metadata;
using Msilcc.Types;

namespace Msilcc.Parser;

public interface ILValue
{
    Location Location { get; }
    CType Type(IMetadataResolver resolver);
}
