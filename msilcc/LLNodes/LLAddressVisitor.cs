using static System.Reflection.Emit.OpCodes;

namespace Msilcc.LLNodes;

public class LLAddressVisitor(FunctionContext ctx) : LLNodeVisitor
{
    public LLValueVisitor ValueVisitor = null!;
    public override void VisitAddressOf(LLAddressOf node)
    {
        VisitAddressOf(node.Target);
    }

    public void VisitAddressOf(LLValueLocation location)
    {
        switch (location)
        {
            case LLLocalTarget loc:
            {
                ctx.IlGen.Emit(Ldloca, ctx.GetLocal(loc.Identifier, loc.Type));
                break;
            }
            case LLLocalArrayTarget loca:
            {
                ctx.IlGen.Emit(Ldloc, ctx.GetLocal(loca.Identifier, loca.Type, loca.Count));
                break;
            }
            case LLParameterTarget param:
            {
                ctx.IlGen.Emit(Ldarga, ctx.ParamIndex[param.Parameter.Identifier]);
                break;
            }
            case LLGlobalTarget glob:
            {
                ctx.IlGen.Emit(Ldsflda, ctx.Globals[glob.Global.Identifier]);
                break;
            }
            case LLStructMemberTarget str:
            {
                VisitAddressOf(str.Source);
                ctx.IlGen.Emit(Ldflda, str.StructMember);
                break;
            }
            case LLPointerTarget ptr:
            {
                // i'm pretty sure this'll work for all cases
                ptr.Value.Visit(ValueVisitor);
                break;
            }
            case LLArrayAccessTarget arr:
            default: throw new InvalidOperationException(location.GetType().FullName);
        }
    }
}