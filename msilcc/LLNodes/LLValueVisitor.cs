using System.Reflection;
using Msilcc.Parser;
using static System.Reflection.Emit.OpCodes;

namespace Msilcc.LLNodes;

public class LLValueVisitor(FunctionContext ctx, FunctionCodegen FuncCodegen, LLAddressVisitor AddressVisitor) : LLNodeVisitor
{
    public override void VisitAssignment(LLAssignment node)
    {
        FuncCodegen.VisitAssignment(node);
        VisitValueOf(node.Target);
    }

    public override void VisitLiteralInteger(LLLiteralInteger node)
    {
        // this is easier than thinking about other solutions
        try
        {
            ctx.IlGen.Emit(Ldc_I4, checked((int)node.Value));
        }
        catch (OverflowException)
        {
            ctx.IlGen.Emit(Ldc_I8, node.Value);
        }
    }

    public override void VisitLiteralString(LLLiteralString node)
    {
        var field = ctx.AddOrGetStringLiteral(node.Value);
        ctx.IlGen.Emit(Ldsflda, field);
        ctx.IlGen.Emit(Conv_U);
    }

    public override void VisitBinary(LLBinary node)
    {
        node.Left.Visit(this);
        node.Right.Visit(this);
        ctx.IlGen.Emit(node.Kind switch
        {
            BinaryKind.Add => Add,
            BinaryKind.Sub => Sub,
            BinaryKind.Mul => Mul,
            BinaryKind.Div => Div,
            BinaryKind.Equal => Ceq,
            BinaryKind.NotEqual => Ceq,
            BinaryKind.LessThan => Clt,
            BinaryKind.LessThanOrEqual => Cgt,
            _ => throw new InvalidOperationException()
        });
        if (node.Kind is BinaryKind.LessThanOrEqual or BinaryKind.NotEqual)
        {
            ctx.IlGen.Emit(Ldc_I4_0);
            ctx.IlGen.Emit(Ceq);
        }
    }

    public override void VisitNegate(LLNegate node)
    {
        node.Value.Visit(this);
        ctx.IlGen.Emit(Neg);
    }

    public override void VisitValueOf(LLValueOf node)
    {
        VisitValueOf(node.Target);
    }

    public void VisitValueOf(LLValueLocation locate)
    {
        switch (locate)
        {
            case LLLocalTarget loc:
            {
                ctx.IlGen.Emit(Ldloc, ctx.GetLocal(loc.Identifier, loc.Type));
                break;
            }
            case LLLocalArrayTarget loca:
            {
                ctx.IlGen.Emit(Ldloc, ctx.GetLocal(loca.Identifier, loca.Type, loca.Count));
                break;
            }
            case LLParameterTarget param:
            {
                ctx.IlGen.Emit(Ldarg, ctx.ParamIndex[param.Parameter.Identifier]);
                break;
            }
            case LLGlobalTarget glob:
            {
                ctx.IlGen.Emit(Ldsfld, ctx.Globals[glob.Global.Identifier]);
                break;
            }
            case LLPointerTarget ptr:
            {
                ptr.Value.Visit(this);
                ctx.IlGen.Emit(Ldind_I);
                break;
            }
            case LLStructMemberTarget str:
            {
                if (str.Source is LLLocalArrayTarget or LLStructArrayMemberTarget)
                {
                    AddressVisitor.VisitAddressOf(str.Source);
                }
                else if (str.Source is LLPointerTarget ptr)
                {
                    ptr.Value.Visit(this);
                }
                else
                {
                    VisitValueOf(str.Source);
                }
                ctx.IlGen.Emit(Ldfld, str.StructMember);
                break;
            }
            case LLStructArrayMemberTarget stra:
            {
                AddressVisitor.VisitAddressOf(stra.Source);
                ctx.IlGen.Emit(Ldflda, stra.StructMember);
                break;
            }
            case LLArrayAccessTarget arr:
            {
                arr.Value.Visit(this);
                break;
            }
            default: throw new InvalidOperationException(locate.GetType().FullName);
        }
    }

    public override void VisitFunctionCall(LLFunctionCall node)
    {
        VisitFunctionCallInner(node);
    }

    public bool VisitFunctionCallInner(LLFunctionCall node)
    {
        foreach (var arg in node.Arguments)
        {
            arg.Visit(this);
        }
        MethodInfo minfo;
        if (node.Method is LLMethodInfo lmf)
        {
            minfo = lmf.Method;
        }
        else if (node.Method is LLFunctionInfo func)
        {
            minfo = ctx.Functions[func.Method.Identifier];
        }
        else if (node.Method is LLRecursiveCall)
        {
            minfo = ctx.Self;
        }
        else throw new InvalidOperationException();
        ctx.IlGen.Emit(Call, minfo);
        return minfo.ReturnType.FullName == "System.Void";
    }

    public override void VisitAddressOf(LLAddressOf node)
    {
        AddressVisitor.VisitAddressOf(node);
    }

    public override void VisitConvert(LLConvertTo node)
    {
        node.Value.Visit(this);
        ctx.IlGen.Emit(node.ConvertTo switch
        {
            { IsPointer: true } => Conv_U, 
            { FullName: "System.SByte" } => Conv_I1,
            { FullName: "System.Byte" } => Conv_U1,
            { FullName: "System.Int16" } => Conv_I2,
            { FullName: "System.UInt16" } => Conv_U2,
            { FullName: "System.Int32" } => Conv_I4,
            { FullName: "System.UInt32" } => Conv_U4,
            { FullName: "System.Int64" } => Conv_I8,
            { FullName: "System.UInt64" } => Conv_U8,
            { FullName: "System.IntPtr" } => Conv_I,
            { FullName: "System.UIntPtr" } => Conv_U,
            { FullName: "System.Single" } => Conv_R4,
            { FullName: "System.Double" } => Conv_R8,
            _ => throw new InvalidOperationException(node.ConvertTo.FullName)
        });
    }

    public override void VisitSizeOf(LLSizeOf node)
    {
        ctx.IlGen.Emit(Sizeof, node.SizeofType);
    }
}
