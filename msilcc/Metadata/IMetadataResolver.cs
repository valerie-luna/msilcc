using System.Reflection;
using Msilcc.Parser;
using Msilcc.Types;

namespace Msilcc.Metadata;

public interface IMetadataResolver
{
    FunctionReference? GetMethod(string name, FunctionType FunctionType);
    CType GetBaseType(BaseType Type);
    Type ValueType { get; }
    Type VoidType { get; }
    Type AddOrGetStructType(StructType structType);
}
