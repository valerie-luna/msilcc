using System.Reflection.Emit;
using System.Reflection;
using static System.Reflection.Emit.OpCodes;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;
using System.Diagnostics;
using System.CommandLine;
using System.Diagnostics.SymbolStore;
using System.Collections.Immutable;
using Msilcc.Metadata;
using Msilcc.Parser;
using Msilcc.LLNodes;

namespace Msilcc;

internal class Program
{
    public static int Main(string[] args)
    {
        var root = new RootCommand("msilcc C to CIL compiler");

        var debugOption = new Option<bool>(
            aliases: ["--debug", "-d"],
            description: "Wait for a debugger to attach."
        );

        var outputOption = new Option<FileInfo>(
            aliases: ["--output", "--out", "-o"],
            getDefaultValue: () => new FileInfo("out.exe"),
            description: "Output file to write."
        );

        var files = new Argument<IEnumerable<string>>(
            name: "files",
            description: "Input files"
        );

        root.AddOption(debugOption);
        root.AddOption(outputOption);
        root.AddArgument(files);

        root.SetHandler((debug, output, files) => 
        {
            if (debug)
            {
                Console.Error.WriteLine("Waiting for debugger...");
                while (!Debugger.IsAttached) ;
            }
            foreach (var file in files)
            {
                Compile(file, output);
            }
        }, debugOption, outputOption, files);

        return root.Invoke(args);
    }

    private static void Compile(string filename, FileInfo output)
    {
        const string MainTypeName = "MainType";

        var baseAssemblyDir = new FileInfo(typeof(object).Assembly.Location).Directory.AssertNotNull();
        var dlls = baseAssemblyDir.EnumerateFiles("*.dll").Select(f => f.FullName);
        IEnumerable<string> extra = ["test/MsilccAuxLibrary.dll"];

        var pathres = new PathAssemblyResolver(
            dlls.Concat(extra).ToArray()
        );
        var mlc = new MetadataLoadContext(pathres, typeof(object).Assembly.GetName().Name);
        mlc.LoadFromAssemblyPath("test/MsilccAuxLibrary.dll"); // we use GetAssemblies later so we need to pre-load it


        PersistedAssemblyBuilder pab = new PersistedAssemblyBuilder(
            new AssemblyName("MyAssembly"),
            mlc.CoreAssembly!
        );

        ModuleBuilder mb = pab.DefineDynamicModule("Module");
        ISymbolDocumentWriter symwriter = mb.DefineDocument(filename, SymLanguageType.C);
        TypeBuilder tb = mb.DefineType(MainTypeName, TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract);

        var resolver = new MetadataResolver(mlc, mb, MainTypeName);
        IEnumerable<Token> tok;

        if (filename == "-")
        {
            tok = Token.Tokenize("<standard input>", Console.In);
        }
        else
        {
            var finfo = new FileInfo(filename);
            using var reader = finfo.OpenText();
            tok = Token.Tokenize(filename, reader);
        }

        var parser = new NodeParser(resolver);
        Node ast = parser.Parse(tok);

        var thing = new LLModuleParser(resolver).VisitProgram((ProgramNode)ast);

        new Codegen(tb, mlc).VisitModule(thing);

        //using (var cg = new Codegen(tb, symwriter, resolver))
        //{
        //    ast.Visit(cg);
        //}
        tb.CreateType();

        var entryPoint = tb.GetMethod("main");
        BlobBuilder peBlob = new BlobBuilder();

        if (entryPoint is not null)
        {
            var programtype = mb.DefineType("Program", TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract);
            var newentrypoint = programtype.DefineMethod("_Main", MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.HideBySig);
            newentrypoint.SetReturnType(mlc.CoreAssembly!.GetType("System.Int32"));
            var il = newentrypoint.GetILGenerator();
            il.EmitCall(Call, entryPoint, []);
            il.Emit(Conv_I4);
            il.Emit(Ret);

            programtype.CreateType();

            var pdb = new FileInfo(
                Path.Combine(
                    output.DirectoryName.AssertNotNull(),
                    Path.GetFileNameWithoutExtension(output.FullName) + ".pdb"
                )
            );

            MetadataBuilder metadataBuilder = pab.GenerateMetadata(out BlobBuilder ilStream, out BlobBuilder fieldData, out MetadataBuilder pdbBuilder);
            PEHeaderBuilder peHeaderBuilder = new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage);
            var entrypointhandle = MetadataTokens.MethodDefinitionHandle(newentrypoint.MetadataToken);
            DebugDirectoryBuilder debugdir = GeneratePdb(pdbBuilder, metadataBuilder.GetRowCounts(), entrypointhandle,
                pdb);

            ManagedPEBuilder peBuilder = new ManagedPEBuilder(
                            header: peHeaderBuilder,
                            metadataRootBuilder: new MetadataRootBuilder(metadataBuilder),
                            ilStream: ilStream,
                            mappedFieldData: fieldData,
                            debugDirectoryBuilder: debugdir,
                            entryPoint: entrypointhandle);

            peBuilder.Serialize(peBlob);
        }
        else
        {
            MetadataBuilder metadataBuilder = pab.GenerateMetadata(out BlobBuilder ilStream, out BlobBuilder fieldData);
            PEHeaderBuilder peHeaderBuilder = new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll);

            ManagedPEBuilder peBuilder = new ManagedPEBuilder(
                            header: peHeaderBuilder,
                            metadataRootBuilder: new MetadataRootBuilder(metadataBuilder),
                            ilStream: ilStream,
                            mappedFieldData: fieldData);

            peBuilder.Serialize(peBlob);   
        }


        using var fileStream = output.OpenWrite();
        peBlob.WriteContentTo(fileStream);

        // emit runtime config file
        const string runtimeconfig = """
        {
          "runtimeOptions": {
            "tfm": "net9.0",
            "framework": {
            "name": "Microsoft.NETCore.App",
            "version": "9.0.0-rc.1.24403.1"
            }
          }
        }
        """;
        var configfile = new FileInfo(
            Path.Combine(
                output.DirectoryName.AssertNotNull(),
                Path.GetFileNameWithoutExtension(output.FullName) + ".runtimeconfig.json"
            )
        );
        using var writer = new StreamWriter(configfile.OpenWrite());
        writer.Write(runtimeconfig);
    }


    static DebugDirectoryBuilder GeneratePdb(MetadataBuilder pdbBuilder, ImmutableArray<int> rowCounts, 
        MethodDefinitionHandle entryPointHandle, FileInfo pdb)
    {
        BlobBuilder portablePdbBlob = new BlobBuilder();
        PortablePdbBuilder portablePdbBuilder = new PortablePdbBuilder(pdbBuilder, rowCounts, entryPointHandle);
        BlobContentId pdbContentId = portablePdbBuilder.Serialize(portablePdbBlob);
        // In case saving PDB to a file
        using FileStream fileStream = pdb.OpenWrite();
        portablePdbBlob.WriteContentTo(fileStream);

        DebugDirectoryBuilder debugDirectoryBuilder = new DebugDirectoryBuilder();
        debugDirectoryBuilder.AddCodeViewEntry($"{Path.GetFileNameWithoutExtension(pdb.Name)}.pdb", pdbContentId, portablePdbBuilder.FormatVersion);
        // In case embedded in PE:
        // debugDirectoryBuilder.AddEmbeddedPortablePdbEntry(portablePdbBlob, portablePdbBuilder.FormatVersion);
        return debugDirectoryBuilder;
    }
}