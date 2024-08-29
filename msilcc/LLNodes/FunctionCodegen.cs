using static System.Reflection.Emit.OpCodes;

namespace Msilcc.LLNodes;

public class FunctionCodegen : LLNodeVisitor
{
    public FunctionCodegen(FunctionContext ctx)
    {
        this.ctx = ctx;
        AddressVisitor = new(ctx);
        ValueVisitor = new(ctx, this, AddressVisitor);
        AddressVisitor.ValueVisitor = ValueVisitor;
    }
    private FunctionContext ctx;

    private LLValueVisitor ValueVisitor;
    private LLAddressVisitor AddressVisitor;
    public override void VisitScope(LLCodeScope node)
    {
        foreach (var n in node.Code)
        {
            n.Visit(this);
        }
    }

    public override void VisitIgnoreValue(LLIgnoreValue node)
    {
        node.Value.Visit(ValueVisitor);
        ctx.IlGen.Emit(Pop);
    }

    public override void VisitConvert(LLConvertTo node)
    {
        node.Visit(ValueVisitor);
        ctx.IlGen.Emit(Pop);
    }

    public override void VisitReturn(LLReturn node)
    {
        node.Expr.Visit(ValueVisitor);
        ctx.IlGen.Emit(Ret);
    }

    public override void VisitFunctionCall(LLFunctionCall node)
    {
        bool voidType = ValueVisitor.VisitFunctionCallInner(node);
        if (!voidType)
        {
            ctx.IlGen.Emit(Pop);
        }
    }

    public override void VisitBranchIf(LLBranchIf node)
    {
        node.Conditional.Visit(ValueVisitor);
        ctx.IlGen.Emit(node.BranchIf ? Brtrue : Brfalse, ctx.GetLabel(node.Target));
    }

    public override void VisitBranch(LLBranch node)
    {
        ctx.IlGen.Emit(Br, ctx.GetLabel(node.Target));
    }

    public override void VisitLabel(LLLabel node)
    {
        ctx.IlGen.MarkLabel(ctx.GetLabel(node));
    }

    public override void VisitAssignment(LLAssignment node)
    {
        switch (node.Target)
        {
            case LLLocalTarget loc:
            {
                node.Value.Visit(ValueVisitor);
                ctx.IlGen.Emit(Stloc, ctx.GetLocal(loc.Identifier, loc.Type));
                break;
            }
            case LLParameterTarget param:
            {
                node.Value.Visit(ValueVisitor);
                ctx.IlGen.Emit(Starg, ctx.ParamIndex[param.Parameter.Identifier]);
                break;
            }
            case LLGlobalTarget glob:
            {
                node.Value.Visit(ValueVisitor);
                ctx.IlGen.Emit(Stsfld, ctx.Globals[glob.Global.Identifier]);
                break;
            }
            case LLPointerTarget ptr:
            {
                ptr.Value.Visit(ValueVisitor);
                node.Value.Visit(ValueVisitor);
                ctx.IlGen.Emit(Stobj, ptr.Type);
                break;
            }
            case LLStructMemberTarget str:
            {
                if (str.Source is LLPointerTarget ptr)
                {
                    ptr.Value.Visit(ValueVisitor);
                }
                else
                {
                    AddressVisitor.VisitAddressOf(str.Source);
                }
                node.Value.Visit(ValueVisitor);
                ctx.IlGen.Emit(Stfld, str.StructMember);
                break;
            }
            case LLArrayAccessTarget arr:
            default: throw new InvalidOperationException(node.Target.GetType().FullName);
        }
    }

    public override void VisitLiteralInteger(LLLiteralInteger node)
    {
        // no side effects, ignore
    }

    public override void VisitSizeOf(LLSizeOf node)
    {
        // no side effects, ignore
    }

    public override void VisitValueOf(LLValueOf node)
    {
        // might have side effects
        ValueVisitor.VisitValueOf(node);
        ctx.IlGen.Emit(Pop);
    }

    public override void VisitBinary(LLBinary node)
    {
        // might have side effects
        ValueVisitor.VisitBinary(node);
        ctx.IlGen.Emit(Pop);
    }
}
