using System.Numerics;
using Msilcc.Metadata;
using Msilcc.Types;

namespace Msilcc.Parser;

public record LiteralIntegerExpression(Location Location, long Value) : Expression(Location)
{
    public override CType Type(IMetadataResolver res)
    {
        return Value switch
        {
            >= byte.MinValue  and <= byte.MaxValue => res.GetBaseType(BaseType.UInt8),
            >= short.MinValue and <= short.MaxValue => res.GetBaseType(BaseType.Int16),
            >= int.MinValue   and <= int.MaxValue => res.GetBaseType(BaseType.Int32),
            >= long.MinValue  and <= long.MaxValue => res.GetBaseType(BaseType.Int64),
        };
    }

    public override void Visit(INodeVisitor visitor) => visitor.VisitLiteralInteger(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitLiteralInteger(this);
}
