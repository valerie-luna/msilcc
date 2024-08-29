using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using static System.Reflection.Emit.OpCodes;

namespace Msilcc.LLNodes;

public record FunctionContext(TypeBuilder Builder, MetadataLoadContext metadata, 
    MethodInfo Self, ILGenerator IlGen, 
    Dictionary<string, MethodBuilder> Functions, 
    Dictionary<string, FieldBuilder> Globals)
{
    public readonly Dictionary<string, short> LocalIndex = new();
    public readonly Dictionary<string, short> ParamIndex = new();
    
    private readonly List<(byte[] bytes, string identifier, FieldBuilder field)> StringLiterals = new();
    public FieldBuilder AddOrGetStringLiteral(byte[] lit)
    {
        var strlit = StringLiterals.SingleOrDefault(l => l.bytes.SequenceEqual(lit));
        if (strlit == default)
        {
            string name = "StringLit" 
                + StringLiterals.Count.ToString()
                + "_"
                + string.Join("", lit
                    .Select(c => (char)c)
                    .Where(char.IsAsciiLetterOrDigit)
                    .Take(10));
            var field = Builder.DefineInitializedData(name, lit, FieldAttributes.Public | FieldAttributes.Static);
            var type = field.FieldType;
            type.GetType().GetField("_typeParent", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(type, metadata.CoreAssembly!.GetType("System.ValueType"));
            strlit = (lit, name, field);
            StringLiterals.Add(strlit);
        }
        return strlit.field;
    }

    private readonly Dictionary<LLLabel, Label> labels = new();
    public Label GetLabel(LLLabel Label)
    {
        if (!labels.TryGetValue(Label, out var slabel))
        {
            slabel = IlGen.DefineLabel();
            labels[Label] = slabel;
        }
        return slabel;
    }

    private readonly Dictionary<string, LocalBuilder> Locals = new();

    public LocalBuilder GetLocal(string Identifier, Type Type, int? ArrayCount = null)
    {
        if (!Locals.TryGetValue(Identifier, out var local))
        {
            local = IlGen.DeclareLocal(Type);
            Locals[Identifier] = local;
            if (ArrayCount is not null)
            {
                Debug.Assert(Type.IsPointer);
                Debug.Assert(ArrayCount.Value > 0);
                IlGen.Emit(Ldc_I4, ArrayCount.Value);
                IlGen.Emit(Sizeof, Type.GetElementType().AssertNotNull());
                IlGen.Emit(Mul);
                IlGen.Emit(Localloc);
                IlGen.Emit(Stloc, local);
            }
        }
        // since we can't rely on types being the same actually...
        // this could mess up but whatever it's probably fine
        Debug.Assert(local.LocalType.FullName == Type.FullName);
        return local;
    }
}
