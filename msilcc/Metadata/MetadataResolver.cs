using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Msilcc.Parser;
using Msilcc.Types;

namespace Msilcc.Metadata;

public class MetadataResolver(MetadataLoadContext ctx, ModuleBuilder mb, string CMainTypeName) : IMetadataResolver
{
    private readonly Dictionary<string, MethodInfo> funcrefs = [];
    private readonly Dictionary<INamedType, TypeBuilder> types = [];
    private readonly Dictionary<string, TypeBuilder> arrayTypes = [];

    public Type ValueType => ctx.CoreAssembly!.GetType("System.ValueType").AssertNotNull();

    public Type VoidType => ctx.CoreAssembly!.GetType("System.Void").AssertNotNull();

    public FunctionReference? GetMethod(string name, FunctionType type)
    {
        var minfo = FindStaticMethod(name, type);
        if (minfo is null) return null;
        return new FunctionReference(
            name,
            type,
            minfo
        );
    }

    public CType GetBaseType(BaseType Type)
    {
        var basetype = FindType(
            (Type switch
            {
                BaseType.NativeInteger => ctx.CoreAssembly!.GetType("System.IntPtr"),
                BaseType.Int8 => ctx.CoreAssembly!.GetType("System.SByte"),
                BaseType.Int16 => ctx.CoreAssembly!.GetType("System.Int16"),
                BaseType.Int32 => ctx.CoreAssembly!.GetType("System.Int32"),
                BaseType.Int64 => ctx.CoreAssembly!.GetType("System.Int64"),
                BaseType.UInt8 => ctx.CoreAssembly!.GetType("System.Byte"),
                BaseType.UNativeInteger => ctx.CoreAssembly!.GetType("System.UIntPtr"),
                BaseType.UInt16 => ctx.CoreAssembly!.GetType("System.UInt16"),
                BaseType.UInt32 => ctx.CoreAssembly!.GetType("System.UInt32"),
                BaseType.UInt64 => ctx.CoreAssembly!.GetType("System.UInt64"),
                BaseType.Float32 => ctx.CoreAssembly!.GetType("System.Single"),
                BaseType.Float64 => ctx.CoreAssembly!.GetType("System.Double"),
                _ => throw new InvalidOperationException()
            }).AssertNotNull()
        );
        return new IntegralType(Type, basetype);
    }

    // Converts a not-metadata type to a metadata type
    private Type FindType(Type type)
    {
        return ctx.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Module.FullyQualifiedName == type.Module.FullyQualifiedName)
            .SingleOrDefault(t => t.FullName == type.FullName)
            ?? throw new InvalidOperationException($"Cannot find {type}");
    }

    private MethodInfo? FindStaticMethod(string Name, FunctionType funcType)
    {
        var rettype = funcType.ReturnType.CLRType(this);
        var paramtypes = funcType.Parameters.Select(p => p.Type.CLRType(this)).ToArray();
        if (funcrefs.TryGetValue(Name, out MethodInfo? minfo))
        {
            if (minfo.ReturnType == rettype && minfo.GetParameters()
                .Select(p => p.ParameterType)
                .SequenceEqual(paramtypes))
            {
                return minfo;
            }
            else
            {
                throw new InvalidOperationException("Type mismatch");
            }
        }
        else
        {
            return FindStaticMethod(Name, rettype, paramtypes);
        }
    }

    // Given metadata types, finds a method
    private MethodInfo? FindStaticMethod(string Name, Type ReturnType, Type[] ParameterTypes)
    {
        return ctx.GetAssemblies()
            .Select(a => a.GetType(CMainTypeName))
            .Where(t => t is not null)
            .SelectMany(t => t!.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(t => t.Name == Name)
            .Where(t => t.ReturnType == ReturnType)
            .Where(t => t.GetParameters().Select(p => p.ParameterType).SequenceEqual(ParameterTypes))
            .SingleOrDefault() ?? FindGenericStaticMethod(Name, ReturnType, ParameterTypes);
    }

    // looking for a generic method that is compatible
    private MethodInfo? FindGenericStaticMethod(string Name, Type ReturnType, Type[] ParameterTypes)
    {
        return ctx.GetAssemblies()
            .Select(a => a.GetType(CMainTypeName))
            .Where(t => t is not null)
            .SelectMany(t => t!.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(t => t.Name == Name)
            .Where(t => t.ContainsGenericParameters)
            .Where(t => t.GetParameters().Length == ParameterTypes.Length)
            .Where(t =>
            {
                if (t.ReturnType.IsGenericParameter)
                {
                    var constraints = t.ReturnType.GetGenericParameterConstraints();
                    return constraints.All(c => c.IsAssignableFrom(ReturnType));
                }
                else
                {
                    return t.ReturnType == ReturnType;
                }
            })
            .Select(t =>
            {
                List<Type> TypeParamList = [];
                if (t.ReturnType.IsGenericParameter)
                    TypeParamList.Add(ReturnType);
                foreach (var (type, paramt) in ParameterTypes.Zip(t.GetParameters().Select(p => p.ParameterType)))
                    if (paramt.IsGenericParameter)
                        TypeParamList.Add(type);
                return t.MakeGenericMethod([.. TypeParamList]);
            })
            .Where(t => t.ReturnType == ReturnType)
            .Where(t => t.GetParameters().Select(p => p.ParameterType).SequenceEqual(ParameterTypes))
            .SingleOrDefault();
    }

    public Type? GetCLRTypeDirectly(string FullName)
    {
        var asm = ctx.CoreAssembly.AssertNotNull();
        var module = asm.GetModules().Single();
        return module.GetType(FullName);
    }

    public Type AddOrGetStructType(StructType structType)
    {
        if (!types.TryGetValue(structType, out TypeBuilder? type))
        {
            var name = structType.GenerateName(this);
            var size = GetStructSize(structType, structType.IsUnion);
            type = mb.DefineType(
                name, 
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.AnsiClass | TypeAttributes.Sealed | TypeAttributes.ExplicitLayout, 
                ValueType,
                size
            );
            foreach (var (member, offset) in GetOffsets(structType))
            {
                var ftype = member.Type is ArrayType ar
                    ? AddOrGetArrayType(ar)
                    : member.Type.CLRType(this);
                var field = type.DefineField(member.Identifier, ftype, FieldAttributes.Public);
                field.SetOffset(offset);
            }
            type.CreateType();
            types[structType] = type;
        }
        return type;
    }

    private IEnumerable<(CDefinition def, int offset)> GetOffsets(StructType st)
        => GetSizeAndOffsetsInner(st.Members, st.IsUnion).SkipLast(1)
#if DEBUG
        .Select(s =>
        {
            Debug.Assert(s.def is not null);
            return s;
        })
#endif
        !;

    private int GetStructSize(StructType st, bool isUnion)
    {
        var enumer = GetSizeAndOffsetsInner(st.Members, isUnion);
        var (def, offset) = enumer.Last();
        Debug.Assert(def is null);
        return offset;
    }

    private IEnumerable<(CDefinition? def, int offset)> GetSizeAndOffsetsInner(IEnumerable<CDefinition> cdef, bool isUnion)
    { // todo: maybe a custom attribute to set the size, otherwise we layout sequentially
        var defs = cdef
            .Select(def => new {def, size = GetSize(def.Type) });
        int offset = 0;
        int maxSize = 1;
        int maxAlignment = 1;
        foreach (var def in defs)
        {
            maxSize = Math.Max(maxSize, def.size);
            int alignment = GetAlignment(def.def.Type);
            maxAlignment = Math.Max(alignment, maxAlignment);
            Align(alignment, ref offset);
            Debug.Assert(!isUnion || offset == 0);
            yield return (def.def, offset);
            if (!isUnion)
            {
                offset += def.size;
            }
        }
        if (isUnion)
        {
            offset += maxSize;
        }
        Align(maxAlignment, ref offset);
        yield return (null, offset);
        static void Align(int size, ref int offset)
        {
            if (offset % size != 0)
            {
                offset += size - (offset % size);
                // the sheer confidence I have in my own maths
                Debug.Assert(offset % size == 0);
            }
        }
    }

    private int GetAlignment(CType Type)
    {
        if (Type is ArrayType ar)
            return GetAlignment(ar.IntegralType);
        if (Type is StructType str)
        {
            return Math.Max(1, str.Members.Select(m => m.Type).Select(GetAlignment).Max());
        }
        else
        {
            return GetSize(Type);
        }
    }

    private Type AddOrGetArrayType(ArrayType ar)
    {
        var basetype = ar.IntegralType.CLRType(this);
        var count = GetCount(ar);
        string identifier = $"<>Array_{basetype.FullName}_{count}";
        if (!arrayTypes.TryGetValue(identifier, out TypeBuilder? type))
        {
            var size = GetSize(basetype);
            var pack = count * size;
            type = mb.DefineType(identifier, 
                TypeAttributes.ExplicitLayout | TypeAttributes.Class | TypeAttributes.AnsiClass | TypeAttributes.Sealed | TypeAttributes.Public, 
                ValueType, pack);           
            type.CreateType();
            arrayTypes[identifier] = type;
        }
        return type;
    }

    private static int GetCount(ArrayType ar)
    {
        if (ar.Internal is ArrayType ar2)
            return ar.Count * GetCount(ar2);
        return ar.Count;
    }

    private int GetSize(CType type)
    {
        if (type is IntegralType or PointerToType) return GetSize(type.CLRType(this));
        if (type is ArrayType ar) return GetSize(AddOrGetArrayType(ar));
        if (type is StructType st) return GetSize(AddOrGetStructType(st));
        throw new InvalidOperationException();
    }

    private int GetSize(Type type)
    {
        return type.FullName switch
        {
            "System.SByte" => 1,
            "System.Byte" => 1,
            "System.Int16" => 2,
            "System.UInt16" => 2,
            "System.Int32" => 4,
            "System.UInt32" => 4,
            "System.Int64" => 8,
            "System.UInt64" => 8,
            "System.IntPtr" => 8,
            "System.UIntPtr" => 8,
            "System.Single" => 4,
            "System.Double" => 8,
            _ => OtherType(type)
        };
        int OtherType(Type type)
        {
            if (type.IsPointer)
            {
                return 8;
            }
            // type.ValueType doesn't work right now (it's being patched at least)
            if (type.BaseType != ValueType)
            {
                // managed ptr, really shouldn't allow this
                throw new InvalidOperationException($"reference type: {type.FullName}");
            }
            if (type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Length != 0)
            {
                throw new InvalidOperationException("nonpublic fields");
            }
            if (type.GetFields().Any(f => !f.FieldType.IsValueType))
            {
                throw new InvalidOperationException($"reference type fields: {type.FullName}");
            }
            if (!type.IsExplicitLayout)
            {
                throw new InvalidOperationException($"type must be explicit layout: {type.FullName}");
            }
            if (type is TypeBuilder tb)
            {
                return tb.Size;
            }
            throw new NotImplementedException(type.FullName);
        }
    }
}
