using System.Diagnostics;
using Msilcc.Metadata;
using Msilcc.Types;

namespace Msilcc.Parser;

public record BinaryExpression(Location Location, BinaryKind Kind, Expression LHS, Expression RHS) : Expression(Location)
{
    public override CType Type(IMetadataResolver res)
    {
        var tuple = NumericOperandResult(res,
            LHS.Type(res),
            Kind,
            RHS.Type(res)
        );
        Debug.Assert(tuple is not null);
        (_, _, var result) = tuple.Value;
        return result;
    }

    public bool NeedsSizeofPointerArith(IMetadataResolver res)
    {
        if (Kind is not (BinaryKind.Add or BinaryKind.Sub))
            return false;
        var tuple = NumericOperandResult(res,
            LHS.Type(res),
            Kind,
            RHS.Type(res)
        );
        Debug.Assert(tuple is not null);
        (var lhs, var rhs, _) = tuple.Value;
        Debug.Assert(!(lhs is IntegralType && rhs is IPtrType));
        return lhs is IPtrType && rhs is IntegralType;
    }

    public bool NeedsSizeofPointerSub(IMetadataResolver res)
    {
        if (Kind is not BinaryKind.Sub)
            return false;
        var tuple = NumericOperandResult(res,
            LHS.Type(res),
            Kind,
            RHS.Type(res)
        );
        Debug.Assert(tuple is not null);
        (var lhs, var rhs, _) = tuple.Value;
        Debug.Assert(!(lhs is IntegralType && rhs is IPtrType));
        return lhs is IPtrType && rhs is IPtrType;
    }

    public (CType right, CType left, CType result)? NumericOperandResult(IMetadataResolver resolver)
    {
        return NumericOperandResult(resolver, LHS.Type(resolver), Kind, RHS.Type(resolver));
    }

    // right and left refer to promotions if needed
    // ALWAYS pass ptr/num as ptr/num and not num/ptr
    // Always pass float/num as float/num and not float/ptr
    public static (CType left, CType right, CType result)? NumericOperandResult(IMetadataResolver res, CType left, BinaryKind kind, CType right)
    {
        // you can't do that here what are you doing
        Debug.Assert(left is not FunctionType);
        Debug.Assert(right is not FunctionType);
        // always have ptr type on lhs
        Debug.Assert(!(left is not IPtrType && right is IPtrType));
        if (kind.HasFlag(BinaryKind.Numeric))
        {
            if (left is IPtrType lptr && right is IPtrType rptr)
            {
                if (lptr.Internal != rptr.Internal)
                    return null;
                if (kind == BinaryKind.Sub)
                    return (left, right, res.GetBaseType(BaseType.Int32));
                else
                    return null;
            }
            else if (left is IPtrType && right is IntegralType it)
            {
                if (kind is not (BinaryKind.Add or BinaryKind.Sub))
                    return null;
                if (it.Type.HasFlag(BaseType.Numeric))
                    return (left, res.GetBaseType(BaseType.Int32), left);
                else
                    return null;
            }
            else
            {
                IntegralType lt, rt;
                Debug.Assert(left is IntegralType && right is IntegralType);
                lt = (IntegralType)left;
                rt = (IntegralType)right;
                if (lt == rt)
                    return (lt, rt, lt);
                // make sure floating point is lhs, again
                Debug.Assert(!(lt.IsNumeric && rt.IsFloatingPoint));
                if (lt.IsNumeric && rt.IsNumeric)
                {
                    var uptype = lt.UpgradedType(rt);
                    if (uptype is null) return null;
                    return (uptype, uptype, uptype);
                }
                else
                {
                    // any case when a floating point is involved
                    // we want the greater of floating points
                    var type = lt.UpgradedType(rt);
                    Debug.Assert(type is not null);
                    return (type, type, type);
                }
            }
        }
        else
        {
            Debug.Assert(kind.HasFlag(BinaryKind.Comparision));
            if (left is IPtrType lptr && right is IPtrType rptr)
            {
                if (lptr.Internal != rptr.Internal)
                    return null;
                return (left, right, res.GetBaseType(BaseType.Int32));
            }
            else if (left is IPtrType && right is IntegralType it)
            {
                if (it.IsNumeric && it.IsNative && kind is BinaryKind.Equal or BinaryKind.NotEqual)
                {
                    return (left, right, res.GetBaseType(BaseType.Int32));
                }
                else return null;
            }
            else
            {
                IntegralType lt, rt;
                Debug.Assert(left is IntegralType && right is IntegralType);
                lt = (IntegralType)left;
                rt = (IntegralType)right;
                if (lt == rt)
                    return (lt, rt, lt);
                // make sure floating point is lhs, again
                Debug.Assert(!(lt.IsNumeric && rt.IsFloatingPoint));
                if (lt.IsNumeric && rt.IsNumeric)
                {
                    var uptype = lt.UpgradedType(rt);
                    if (uptype is null) return null;
                    return (uptype, uptype, res.GetBaseType(BaseType.Int32));
                }
                else
                {
                    // any case when a floating point is involved
                    // we want the greater of floating points
                    var type = lt.UpgradedType(rt);
                    Debug.Assert(type is not null);
                    return (type, type, res.GetBaseType(BaseType.Int32));
                }
            }
        }
    }

    public override void Visit(INodeVisitor visitor) => visitor.VisitBinary(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitBinary(this);
}
