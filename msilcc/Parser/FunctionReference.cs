using System.Reflection;
using Msilcc.Types;

namespace Msilcc.Parser;

// Method info is null when the function is declared in our file
public record FunctionReference(string Name, FunctionType FunctionType, MethodInfo? Method);
