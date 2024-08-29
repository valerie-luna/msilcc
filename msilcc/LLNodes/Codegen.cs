using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using static System.Reflection.Emit.OpCodes;

namespace Msilcc.LLNodes;

public class Codegen(TypeBuilder typeBuilder, MetadataLoadContext metadata) : LLNodeVisitor
{
    public override void VisitModule(LLModule node)
    {
        Dictionary<string, MethodBuilder> Functions = new();
        Dictionary<string, FieldBuilder> Globals = new();
        FunctionContext? staticctor = null;
        foreach (var global in node.Globals)
        {
            var field = typeBuilder.DefineField(
                global.Identifier, global.Type,
                FieldAttributes.Static | FieldAttributes.Public
            );
            if (global.ArrayInitializeCount is not null)
            {
                Debug.Assert(global.Type.IsPointer);
                var staticil = GetStaticConstructorIL();
                staticil.Emit(Sizeof, global.Type.GetElementType().AssertNotNull());
                staticil.Emit(Ldc_I4, global.ArrayInitializeCount.Value);
                staticil.Emit(Mul);
                Type[] arg = [metadata.CoreAssembly!.GetType("System.UIntPtr").AssertNotNull()];
                Type nativememory = metadata.CoreAssembly!.GetType("System.Runtime.InteropServices.NativeMemory")
                    .AssertNotNull();
                var method = nativememory.GetMethod("AllocZeroed", 0, arg).AssertNotNull();
                staticil.EmitCall(Call, method, []);
                staticil.Emit(Stsfld, field);
            }
            Globals[global.Identifier] = field;
        }
        foreach (var func in node.Functions)
        {
            var funcbuilder = typeBuilder.DefineMethod(
                func.Identifier, MethodAttributes.HideBySig | MethodAttributes.Public | MethodAttributes.Static
            );
            var ctx = new FunctionContext(typeBuilder, metadata, funcbuilder, funcbuilder.GetILGenerator(), Functions, Globals);
            Functions[func.Identifier] = funcbuilder;
            funcbuilder.SetReturnType(func.ReturnType);
            funcbuilder.SetParameters(func.Parameters.Select(n => n.Type).ToArray());
            short paramIndex = 0;
            foreach (var param in func.Parameters)
            {
                funcbuilder.DefineParameter(paramIndex + 1, ParameterAttributes.None, param.Identifier);
                ctx.ParamIndex[param.Identifier] = paramIndex++; 
            }
            FunctionCodegen fcg = new(ctx);
            func.Body.Visit(fcg);
            ctx.IlGen.Emit(Newobj, typeof(InvalidProgramException)
                .GetConstructors()
                .Single(c => c.GetParameters().Length == 0));
            ctx.IlGen.Emit(Throw);
        }
        staticctor?.IlGen.Emit(Ret);
        ILGenerator GetStaticConstructorIL()
        {
            if (staticctor is null)
            {
                var method = typeBuilder.DefineMethod(".cctor", 
                    MethodAttributes.Static 
                    | MethodAttributes.Private
                    | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName);
                method.SetReturnType(metadata.CoreAssembly!.GetType("System.Void"));
                staticctor = new FunctionContext(typeBuilder, metadata, method, method.GetILGenerator(), [], []);
            }
            return staticctor.IlGen;
        }
    }
}
