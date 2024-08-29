using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Msilcc.Parser;

namespace Msilcc.LLNodes;

public abstract class LLNodeVisitor
{
    public virtual void VisitGlobal(LLGlobal node) => Throw();
    public virtual void VisitModule(LLModule node) => Throw();
    public virtual void VisitFunction(LLFunction node) => Throw();
    public virtual void VisitScope(LLCodeScope node) => Throw();
    public virtual void VisitReturn(LLReturn node) => Throw();
    public virtual void VisitFunctionCall(LLFunctionCall node) => Throw();
    public virtual void VisitLiteralInteger(LLLiteralInteger node) => Throw();
    public virtual void VisitLiteralString(LLLiteralString node) => Throw();
    public virtual void VisitBinary(LLBinary node) => Throw();
    public virtual void VisitConvert(LLConvertTo node) => Throw();
    public virtual void VisitNegate(LLNegate node) => Throw();
    public virtual void VisitLabel(LLLabel node) => Throw();
    public virtual void VisitBranch(LLBranch node) => Throw();
    public virtual void VisitBranchIf(LLBranchIf node) => Throw();
    public virtual void VisitAssignment(LLAssignment node) => Throw();
    public virtual void VisitValueOf(LLValueOf node) => Throw();
    public virtual void VisitAddressOf(LLAddressOf node) => Throw();
    public virtual void VisitSizeOf(LLSizeOf node) => Throw();
    public virtual void VisitSizeOfArray(LLSizeofArray node) => Throw();
    public virtual void VisitIgnoreValue(LLIgnoreValue node) => Throw();

    private void Throw([CallerMemberName] string member = null!)
    {
        throw new NotImplementedException($"{member} not implemented in {GetType().Name}");
    }

}

public abstract record LLNode
{
    public abstract void Visit(LLNodeVisitor visitor);
}

public record LLGlobal(Type Type, string Identifier, int? ArrayInitializeCount) : LLNode
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitGlobal(this);
}

public record LLModule(ImmutableArray<LLGlobal> Globals, ImmutableArray<LLFunction> Functions) : LLNode
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitModule(this);
}

public record LLFunction(string Identifier, Type ReturnType, ImmutableArray<LLParameter> Parameters, LLCodeScope Body) : LLNode
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitFunction(this);
}

public record LLParameter(string Identifier, Type Type);

public record LLCodeScope(ImmutableArray<LLCode> Code) : LLNode
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitScope(this);
}

public abstract record LLCode : LLNode;

public abstract record LLProducesValue(Type Type) : LLCode;

public abstract record LLValueless : LLCode;

public record LLReturn(LLProducesValue Expr) : LLValueless
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitReturn(this);
}

public record LLFunctionCall(LLFunctionReference Method, ImmutableArray<LLProducesValue> Arguments) : LLProducesValue(Method.ReturnType)
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitFunctionCall(this);
}

public abstract record LLFunctionReference
{
    public static implicit operator LLFunctionReference?(MethodInfo? minfo) => minfo is null ? null :new LLMethodInfo(minfo);
    public static implicit operator LLFunctionReference?(LLFunction? func) => func is null ? null : new LLFunctionInfo(func);
    public abstract Type ReturnType { get; }
}
public record LLMethodInfo(MethodInfo Method) : LLFunctionReference
{
    public override Type ReturnType => Method.ReturnType;
}

public record LLFunctionInfo(LLFunction Method) : LLFunctionReference
{
    public override Type ReturnType => Method.ReturnType;
}

public record LLRecursiveCall(Type ReturnType) : LLFunctionReference
{
    public override Type ReturnType { get; } = ReturnType;
}

public record LLLiteralInteger(long Value, Type Type) : LLProducesValue(Type)
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitLiteralInteger(this);
}

public record LLLiteralString(byte[] Value, Type Type) : LLProducesValue(Type)
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitLiteralString(this);
}

public record LLBinary(LLProducesValue Left, LLProducesValue Right, BinaryKind Kind, Type Type) : LLProducesValue(Type)
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitBinary(this);
}

public record LLConvertTo(LLProducesValue Value, Type ConvertTo) : LLProducesValue(ConvertTo)
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitConvert(this);
}

public record LLNegate(LLProducesValue Value) : LLProducesValue(Value.Type)
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitNegate(this);
}

public record LLLabel(Guid Id) : LLValueless
{
    public LLLabel() : this(Guid.NewGuid()) {}
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitLabel(this);
}

public record LLBranchIf(LLProducesValue Conditional, bool BranchIf, LLLabel Target) : LLValueless
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitBranchIf(this);
}

public record LLBranch(LLLabel Target) : LLValueless
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitBranch(this);
}

public record LLAssignment(LLValueLocation Target, LLProducesValue Value) : LLProducesValue(Value.Type)
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitAssignment(this);
}

public record LLIgnoreValue(LLProducesValue Value) : LLValueless
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitIgnoreValue(this);
}


public abstract record LLValueLocation()
{
    public abstract Type Type { get; init; }
}
public record LLLocalTarget(string Identifier, Type Type) : LLValueLocation;
public record LLLocalArrayTarget(string Identifier, Type Type, int Count) : LLValueLocation;
public record LLParameterTarget(LLParameter Parameter) : LLValueLocation
{
    public override Type Type { get => Parameter.Type; init => throw new NotImplementedException(); }
}
public record LLGlobalTarget(LLGlobal Global) : LLValueLocation
{
    public override Type Type { get => Global.Type; init => throw new NotImplementedException(); }
}

public record LLPointerTarget(LLProducesValue Value) : LLValueLocation
{
    public override Type Type
    {
        get
        {
            var produced = Value.Type;
            Debug.Assert(produced.IsPointer);
            return produced.GetElementType().AssertNotNull();
        }
        init => throw new NotImplementedException();
    }
}

public record LLArrayAccessTarget(LLProducesValue Value) : LLValueLocation
{
    public override Type Type { get => Value.Type; init => throw new NotImplementedException(); }
}

public record LLStructMemberTarget(LLValueLocation Source, FieldInfo StructMember) : LLValueLocation
{
    public override Type Type { get => StructMember.FieldType; init => throw new NotImplementedException(); }
}

public record LLStructArrayMemberTarget(LLValueLocation Source, FieldInfo StructMember, Type ArrayType) : LLValueLocation
{
    public override Type Type { get => ArrayType; init => throw new NotImplementedException(); }
}

public record LLValueOf(LLValueLocation Target) : LLProducesValue(Target.Type)
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitValueOf(this);
}

public record LLAddressOf(LLValueLocation Target) : LLProducesValue(Target.Type.MakePointerType())
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitAddressOf(this);
}

public record LLSizeOf(Type SizeofType, Type Int32Type) : LLProducesValue(Int32Type)
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitSizeOf(this);
}

public record LLSizeofArray(int Size, Type Int32Type) : LLProducesValue(Int32Type)
{
    public override void Visit(LLNodeVisitor visitor) => visitor.VisitSizeOfArray(this);
}