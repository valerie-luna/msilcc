using System.Diagnostics;
using Msilcc.Metadata;
using Msilcc.Parser;
using Msilcc.Types;

namespace Msilcc.LLNodes;

public class LLModuleParser(IMetadataResolver resolver) : NodeVisitor<LLNode>
{
    public override LLModule VisitProgram(ProgramNode node)
    {
        List<LLGlobal> globals = [];
        List<LLFunction> funcs = [];
        foreach (var subn in node.Nodes)
        {
            if (subn is Function fn)
            {
                var rettype = fn.FuncType.ReturnType.CLRType(resolver);
                var param = fn.FuncType.Parameters
                    .Select(p => new LLParameter(p.Identifier, p.Type.CLRType(resolver)));
                var funcparse = new LLFunctionBodyParser(globals, [.. param], funcs, 
                    fn.Identifier, rettype, resolver);
                var scope = funcparse.Visit(fn.Body);
                funcs.Add(new LLFunction(fn.Identifier, rettype, [.. param], scope));
            }
            else if (subn is FunctionDeclaration fndecl)
            {
                // we don't need to generate any code
            }
            else if (subn is CDefinition global)
            {
                Debug.Assert(global.DefClass is DefinitionClass.Global);
                int? ArrayCount = global.Type is ArrayType ar ? ar.Count : null;
                globals.Add(new LLGlobal(global.Type.CLRType(resolver), global.Identifier, ArrayCount));
            }
        }
        return new LLModule([.. globals], [.. funcs]);
    }
}
