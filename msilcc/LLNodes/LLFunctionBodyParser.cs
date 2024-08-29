using System.Collections.Immutable;
using System.Diagnostics;
using Msilcc.Metadata;
using Msilcc.Parser;
using Msilcc.Types;

namespace Msilcc.LLNodes;

public class LLFunctionBodyParser(IReadOnlyList<LLGlobal> Globals, 
    IReadOnlyList<LLParameter> Parameters, IReadOnlyList<LLFunction> Functions,
    string OurIdentifier, Type OurReturnType, IMetadataResolver resolver) : NodeVisitor<LLCode?>
{
    private Stack<LLCode> Code = default!;
    public LLCodeScope Visit(BlockStatement Body)
    {
        Code = new();
        foreach (var statement in Body.Nodes)
        {
            VisitOrAdd(statement);
        }
        Debug.Assert(Code.All(c => c is not null));
        var scope = new LLCodeScope([.. Code.Reverse()]);
        Code = null!;
        return scope;
    }

    public override LLCode? VisitBlock(BlockStatement node)
    {
        foreach (var statement in node.Nodes)
        {
            VisitOrAdd(statement);
        }
        return null;
    }

    public override LLCode VisitUnary(UnaryStatement node)
    {
        if (node.Kind is UnaryStmtKind.Return)
        {
            var expr = node.Node.Visit(this).AssertNotNull();
            return new LLReturn((LLProducesValue)expr);
        }
        else if (node.Kind is UnaryStmtKind.Expression)
        {
            return node.Node.Visit(this).AssertNotNull();
        }
        else throw new InvalidOperationException();
    }

    public override LLFunctionCall VisitFunctionCall(FunctionCall node)
    {
        LLFunctionReference? method;
        if (node.FunctionIdentifier == OurIdentifier)
        {
            method = new LLRecursiveCall(OurReturnType);
        }
        else
        {
            method = (LLFunctionReference?)node.Reference.Method 
                ?? Functions.Single(f => f.Identifier == node.FunctionIdentifier);
        }
        Debug.Assert(method is not null);
        var args = node.Args
            .Select(n => n.Visit(this))
            .Cast<LLProducesValue>()
            .ToImmutableArray();
        return new LLFunctionCall(method, args);
    }

    public override LLLiteralInteger VisitLiteralInteger(LiteralIntegerExpression node)
    {
        return new LLLiteralInteger(node.Value, node.Type(resolver).CLRType(resolver));
    }

    public override LLLiteralString VisitLiteralString(LiteralStringExpression node)
    {
        return new LLLiteralString(node.Value, node.Type(resolver).CLRType(resolver));
    }

    public override LLCode VisitBinary(BinaryExpression node)
    {
        var int32 = resolver.GetBaseType(BaseType.Int32).CLRType(resolver);
        var (leftConvType, rightConvType, finalType) = node.NumericOperandResult(resolver).AssertNotNull();
        var leftClr = leftConvType.CLRType(resolver);
        var rightClr = rightConvType.CLRType(resolver);
        var finalClr = finalType.CLRType(resolver);

        var left = (LLProducesValue)node.LHS.Visit(this).AssertNotNull();
        var right = (LLProducesValue)node.RHS.Visit(this).AssertNotNull();

        if (left.Type != leftClr)
            left = new LLConvertTo(left, leftClr);
        if (right.Type != rightClr)
            right = new LLConvertTo(right, rightClr);

        if (node.NeedsSizeofPointerArith(resolver))
        {
            var ptr = (leftConvType as IPtrType)?.Internal;
            Debug.Assert(ptr is not null);
            var type = ptr.CLRType(resolver);
            LLProducesValue multiplier = new LLSizeOf(type, int32);
            if (leftConvType is ArrayType { Internal: ArrayType iar } )
            {
                multiplier = new LLBinary(multiplier, new LLLiteralInteger(iar.TotalCount, int32), 
                    BinaryKind.Mul, multiplier.Type);
            }
            right = new LLBinary(right, multiplier,
                BinaryKind.Mul, right.Type);
        }

        var ret = new LLBinary(left, right, node.Kind, finalClr);

        if (node.Kind is BinaryKind.Sub && node.NeedsSizeofPointerSub(resolver))
        {
            Debug.Assert(rightClr.IsPointer);
            ret = new LLBinary(ret, new LLSizeOf(rightClr.GetElementType()!, int32), BinaryKind.Div, left.Type);
        }
        return ret;
    }

    public override LLCode VisitNegation(NegationExpression node)
    {
        return new LLNegate((LLProducesValue)node.Node.Visit(this).AssertNotNull());
    }

    public override LLProducesValue VisitStatementExpression(StatementExpression node)
    {
        _ = VisitBlock(node.Statements);
        var last = Code.Pop();
        return (LLProducesValue)last;
    }

    public override LLCode? VisitIf(IfStatement node)
    {
        var conditional = node.Conditional.Visit(this);
        Debug.Assert(conditional is LLProducesValue);
        var label = new LLLabel();
        Code.Push(new LLBranchIf((LLProducesValue)conditional, false, label));
        VisitOrAdd(node.IfTrue);
        if (node.IfFalse is null)
        {
            Code.Push(label);
        }
        else
        {
            var endlabel = new LLLabel();
            Code.Push(new LLBranch(endlabel));
            Code.Push(label);
            VisitOrAdd(node.IfFalse);
            Code.Push(endlabel);
        }
        return null;
    }

    private void VisitOrAdd(Node node)
    {
        var n = node.Visit(this);
        if (n is not null) Code.Push(n);
    }

    public override LLCode VisitAssignment(AssignmentExpression node)
    {
        LLValueLocation target = GetTarget(node.LHS);
        var value = (LLProducesValue?)node.RHS.Visit(this);
        Debug.Assert(value is not null);
        return new LLAssignment(target, value);
    }

    private LLValueLocation GetTarget(ILValue ilv) => GetTarget((Expression)ilv);
    private LLValueLocation GetTarget(Expression expr)
    {
        if (expr is VariableExpression var)
        {
            switch (var.Variable.DefClass)
            {
                case DefinitionClass.LocalVariable:
                    if (var.Variable.Type is ArrayType arr)
                    {
                        var integral = arr.IntegralType.CLRType(resolver);
                        return new LLLocalArrayTarget(var.Variable.ClrIdentifier, 
                            integral.MakePointerType(),
                            arr.TotalCount);
                    }
                    else
                    {
                        var type = var.Variable.Type.CLRType(resolver);
                        return new LLLocalTarget(var.Variable.ClrIdentifier, type);
                    }
                case DefinitionClass.Parameter:
                    var param = Parameters.Single(p => p.Identifier == var.Variable.Identifier);
                    return new LLParameterTarget(param);
                case DefinitionClass.Global:
                    var global = Globals.Single(g => g.Identifier == var.Variable.Identifier);
                    return new LLGlobalTarget(global);
                case DefinitionClass.StructMember:
                case DefinitionClass.Function:
                default:
                    throw new InvalidOperationException();
            }
        }
        else if (expr is StructMemberLValue smv)
        {
            var ctype = (StructType)smv.Node.Type(resolver);
            var mtype = smv.Member.Type;
            var clrtype = resolver.AddOrGetStructType(ctype);
            var field = clrtype.GetField(smv.Member.Identifier).AssertNotNull();
            if (mtype is ArrayType arr)
            {
                var clrarrtype = arr.IntegralType.CLRType(resolver); 
                return new LLStructArrayMemberTarget(GetTarget(smv.Node), field, clrarrtype);
            }
            else
            {
                return new LLStructMemberTarget(GetTarget(smv.Node), field);
            }
        }
        else if (expr is DereferenceExpression deref)
        {
            var type = deref.Node.Type(resolver);
            if (type is IPtrType ar && ar.Internal is ArrayType arit)
            {
                var arrptr = deref.Node.Visit(this) as LLProducesValue;
                Debug.Assert(arrptr is not null);
                return new LLArrayAccessTarget(arrptr);
            }
            else
            {
                var ptr = deref.Node.Visit(this);
                Debug.Assert(ptr is LLProducesValue);
                return new LLPointerTarget((LLProducesValue)ptr);
            }
        }
        else if (expr is CommaExpression comma)
        {
            VisitOrAdd(comma.LHS);
            return GetTarget((ILValue)comma.RHS);
        }
        else
        {
            throw new InvalidOperationException();
        }

    }

    public override LLValueOf VisitVariable(VariableExpression node)
    {
        return new LLValueOf(GetTarget((Expression)node));
    }

    public override LLCode? VisitFor(ForStatement node)
    {
        LLLabel bodyStart = new();
        LLLabel? cond = null;
        VisitOrAdd(node.Initializer);
        if (node.Conditional is not null)
        {
            cond = new();
            Code.Push(new LLBranch(cond));
        }
        Code.Push(bodyStart);
        VisitOrAdd(node.Body);
        if (node.Increment is not null)
        {
            VisitOrAdd(node.Increment);
        }
        if (node.Conditional is not null)
        {
            Debug.Assert(cond is not null);
            Code.Push(cond);
            Code.Push(new LLBranchIf((LLProducesValue)node.Conditional.Visit(this).AssertNotNull(), true, bodyStart));
        }
        else
        {
            Code.Push(new LLBranch(bodyStart));
        }
        return null;
    }

    public override LLCode? VisitEmpty(EmptyStatement node)
    {
        return null;
    }

    public override LLCode? VisitComma(CommaExpression node)
    {
        VisitOrAdd(node.LHS);
        return node.RHS.Visit(this);
    }

    public override LLCode VisitDereference(DereferenceExpression node)
    {
        return new LLValueOf(GetTarget((Expression)node));
    }

    public override LLCode VisitAddressOf(AddressOfExpression node)
    {
        return new LLAddressOf(GetTarget(node.Node));
    }

    public override LLCode VisitMemberAccessL(StructMemberLValue node)
    {
        return new LLValueOf(GetTarget((Expression)node));
    }

    public override LLCode VisitCast(CastExpression node)
    {
        if (node.CastTo is VoidType)
        {
            return new LLIgnoreValue((LLProducesValue)node.Expression.Visit(this)!);
        }
        else
        {
            return new LLConvertTo((LLProducesValue)node.Expression.Visit(this)!, node.CastTo.CLRType(resolver));
        }
    }

    public LLCode VisitSizeof(CType type)
    {
        var int32 = resolver.GetBaseType(BaseType.Int32).CLRType(resolver);
        if (type is ArrayType ar)
        {
            var integral = ar.IntegralType.CLRType(resolver);
            return new LLBinary(
                new LLSizeOf(integral, int32),
                new LLLiteralInteger(ar.TotalCount, int32),
                BinaryKind.Mul,
                int32
            );
        }
        else
        {
            return new LLSizeOf(
                type.CLRType(resolver),
                int32
            );
        }
    }

    public override LLCode VisitSizeof(SizeofExpression node)
    {
        if (node.Expression is LiteralStringExpression str)
        {
            var int32 = resolver.GetBaseType(BaseType.Int32).CLRType(resolver);
            return new LLLiteralInteger(str.Value.Length, int32);
        }
        else
        {
            return VisitSizeof(node.CType);
        }
    }
}
