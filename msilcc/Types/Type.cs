using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Msilcc.Metadata;
using Msilcc.Parser;

namespace Msilcc.Types;

public abstract record CType
{
    public CType PointerTo => new PointerToType(this);
    public abstract Type CLRType(IMetadataResolver res);
    public abstract CType Base { get; }
    public string TypeName(IMetadataResolver res) => CLRType(res).Name;

    protected virtual bool PrintMembers(StringBuilder builder)
    {
        try
        {
            builder.Append($"CLRType = {CLRType}, ");
        } catch (NotImplementedException) { }
        if (Base == this)
        {
            builder.Append("same base");
        }
        else
        {
            builder.Append($"Base = {Base}");
        }
        return true;
    }

    public abstract bool CanBeAssignedTo(CType Type);
}

public record VoidType : CType
{
    public override CType Base => this;

    public override bool CanBeAssignedTo(CType Type)
    {
        return false;
    }

    public override Type CLRType(IMetadataResolver res) => res.VoidType;
}

public enum BaseType : int
{
    Undefined = 0,
    UInt8 =           8 | Numeric | Unsigned,
    UInt16 =         16 | Numeric | Unsigned,
    UInt32 =         32 | Numeric | Unsigned,
    UInt64 =         64 | Numeric | Unsigned,
    UNativeInteger = 32 | Numeric | Unsigned | Native,
    Int8 =            8 | Numeric | Signed,
    Int16 =          16 | Numeric | Signed,
    Int32 =          32 | Numeric | Signed,
    Int64 =          64 | Numeric | Signed,
    NativeInteger =  32 | Numeric | Signed | Native,
    Float32 =        32 | FloatingPoint | Signed,
    Float64 =        64 | FloatingPoint | Signed,

    Numeric =       0b0_00_10_00000000,
    FloatingPoint = 0b0_00_01_00000000,
    Signed =        0b0_01_00_00000000,
    Unsigned =      0b0_10_00_00000000,
    Native =        0b1_00_00_00000000,
    BitsMask =      0b0_00_00_11111111
}

public record IntegralType(BaseType Type, Type clrtype) : CType
{
    public override Type CLRType(IMetadataResolver res) => clrtype;

    public override CType Base => this;
    public bool IsNumeric => Type.HasFlag(BaseType.Numeric);
    public bool IsFloatingPoint => Type.HasFlag(BaseType.FloatingPoint);
    // if false, unsigned
    public bool IsSigned
    {
        get
        {
            Debug.Assert(Type.HasFlag(BaseType.Signed) || Type.HasFlag(BaseType.Unsigned));
            return Type.HasFlag(BaseType.Signed);
        }
    }

    public bool IsNative = Type.HasFlag(BaseType.Native);
    public int Bits => (int)(Type & BaseType.BitsMask);

    public IntegralType? UpgradedType(IntegralType other)
    {
        if (this == other)
            return this;
        if (this.IsFloatingPoint && other.IsFloatingPoint)
            return ReturnGreaterBits(this, other);
        if (this.IsFloatingPoint)
            return this;
        if (other.IsFloatingPoint)
            return other;
        Debug.Assert(this.IsNumeric && other.IsNumeric);
        if (this.IsSigned != other.IsSigned)
        {
            // there's a few special rules if the signage is different
            // we can only do arith if the signed one can contain every unsigned value
            var signedType = this.IsSigned ? this : other;
            var unsignedType = this.IsSigned ? other : this;
            if (signedType.Bits > unsignedType.Bits)
                return signedType;
            else
                return null;        
        }
        return ReturnGreaterBits(this, other);
        static IntegralType ReturnGreaterBits(IntegralType l, IntegralType r)
        {
            if (l.Bits == r.Bits)
            {
                // if one of them is native, we want that one
                if (l.IsNative)
                    return l;
                return r;
            }
            if (l.Bits > r.Bits)
                return l;
            return r;
        }
    }

    public override bool CanBeAssignedTo(CType Type)
    {
        // TURNS OUT C JUST LETS YOU DO WHATEVER
        // i don't believe it but I don't have a spec to check
        return Type is IntegralType;
    }
}


public record PointerToType(CType Internal) : CType, IPtrType
{
    public override Type CLRType(IMetadataResolver res) => ArrayIntegral.CLRType(res).MakePointerType();
    public override CType Base => Internal;
    // pointer to array shouldn't convert
    // but array to pointer should
    public CType IntegralType => Internal is IPtrType ptr ? ptr.IntegralType : Internal;
    private CType ArrayIntegral => Internal is ArrayType ar ? ar.IntegralType : Internal;

    public override bool CanBeAssignedTo(CType Type)
    {
        // we don't have void pointers yet
        return Type == this;
    }
}

public record FunctionType(CType ReturnType, ImmutableArray<CDefinition> Parameters) : CType
{
    public FunctionType(CType ReturnType, IEnumerable<CType> ParameterTypes)
        : this(ReturnType, ParameterTypes.Select(t => new CDefinition(t, DefinitionClass.Parameter, default, 0)).ToImmutableArray())
        { }

    // can have this return the delegate type, buuut I'm not sure what to do with that yet.
    // minfo? will probably need that.
    // let's just start here and see what happens
    public override Type CLRType(IMetadataResolver res) => throw new NotImplementedException();
    public override CType Base => this;

    public virtual bool Equals(FunctionType? other)
    {
        return other is not null
            && other.ReturnType == ReturnType
            && other.Parameters.Select(p => (p.Type, p.DefClass))
        .SequenceEqual(Parameters.Select(p => (p.Type, p.DefClass)));
    }

    public override int GetHashCode()
    {
        return ReturnType.GetHashCode() ^ Parameters.Select(p => p.GetHashCode()).Sum();
    }

    public override bool CanBeAssignedTo(CType Type)
    {
        return Type is FunctionType ft
            && ft.ReturnType.CanBeAssignedTo(ReturnType)
            && ft.Parameters.Length == Parameters.Length
            && ft.Parameters.Zip(Parameters)
                .Select((f) => f.First.Type.CanBeAssignedTo(f.Second.Type))
                .All(f => f);
    }
}

public record ArrayType(CType Internal, int Count) : CType, IPtrType
{
    // okay because this one's messy I don't want to be accessing this directly...
    public override Type CLRType(IMetadataResolver res) => IntegralType.CLRType(res).MakePointerType();
    public CType IntegralType => Internal is ArrayType ar ? ar.IntegralType : Internal;
    public override CType Base => Internal;
    public int TotalCount => Count * (Internal is ArrayType ar ? ar.TotalCount : 1);

    public override bool CanBeAssignedTo(CType Type)
    {
        return Type == this
            || Type == new PointerToType(IntegralType);
    }
}

public interface IPtrType
{
    CType Internal { get; }
    CType IntegralType { get; }
}

public interface INamedType
{
    string? Name { get; }
}

public record StructType(string? Name, ImmutableArray<CDefinition> Members, bool IsUnion) : CType, INamedType
{
    public override Type CLRType(IMetadataResolver res) => res.AddOrGetStructType(this);

    public override CType Base => this;

    public override bool CanBeAssignedTo(CType Type) => Type == this;

    public virtual bool Equals(StructType? other)
    {
        return other is StructType s
            && Members.Length == s.Members.Length
            && Members.SequenceEqual(s.Members);
    }

    public override int GetHashCode()
    {
        return Name?.GetHashCode() ?? 0 + Members
            .Select(m => m.GetHashCode())
            .Aggregate(0, (a, b) => a+b);
    }

    public string GenerateName(IMetadataResolver res)
    {
        if (Name is not null)
            return Name;
        if (Members.Length == 0)
            return "<>_MemberlessType";
        else
        {
            return Members.Aggregate("<>", (acc, def) =>
            {
                return acc + "_" + def.Identifier + "_" + def.Type.CLRType(res).Name;
            });
        }
    }
}