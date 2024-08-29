using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata;
using Msilcc.Metadata;
using Msilcc.Types;

// This file contains a recursive descent parser for C.
//
// Most functions in this file are named after the symbols they are
// supposed to read from an input token list. For example, stmt() is
// responsible for reading a statement from a token list. The function
// then construct an AST node representing a statement.
//
// Unlike chibicc, we only use a single data structure - the IENumerator 
// to a stream of tokens. This data structure encapsulates the state
// of how many tokens are left.
//
// Which means we do have a conception of 'input token stream', and in
// fact, we essentially cannot perform look-ahead as it is implemented
// right now. This is not impossible to fix, if we materialize the
// enumerator beforehand, but that has not yet been implemented.

namespace Msilcc.Parser;


public class NodeParser(IMetadataResolver tres)
{
    private class DuplicableEnumerator<T>(IEnumerable<T> enumerable, int skip = -1) : IEnumerator<T>
        where T : IEquatable<T>
    {
        private int Skip = skip;
        private readonly IEnumerable<T> enumerable = enumerable;
        private readonly IEnumerator<T> enumerator = enumerable.Skip(skip).GetEnumerator();

        public T Current => enumerator.Current;

        object IEnumerator.Current => ((IEnumerator)enumerator).Current;

        public void Dispose() => enumerator.Dispose();

        public bool MoveNext()
        {
            Skip++;
            return enumerator.MoveNext();
        }

        public void Reset()
        {
            Skip = -1;
            enumerator.Reset();
        }

        public DuplicableEnumerator<T> Duplicate()
        {
            var enumer = new DuplicableEnumerator<T>(enumerable, Skip);
            enumer.enumerator.MoveNext(); // avoid incrementing Skip
            Debug.Assert(enumer.Current.Equals(Current));
            return enumer;
        }

        public void SkipTo(DuplicableEnumerator<T> other)
        {
            Debug.Assert(other.enumerable == enumerable);
            while (!Current.Equals(other.Current))
                MoveNext();
        }
    }
    private record Scope()
    {
        public HashSet<CDefinition> Locals { get; } = new();
        public HashSet<INamedType> NamedTypes { get; } = new();
    }

    private record FunctionContext(CDefinition Definition, IReadOnlySet<CDefinition> Parameters)
    {
        public void BeginScope()
        {
            Scopes.Push(new());
        }
        public void EndScope()
        {
            Scopes.Pop();
        }


        public readonly List<CDefinition> AllDefinitions = new();
        public readonly Stack<Scope> Scopes = new();
        public Scope CurrentScope => Scopes.Peek();
        public int ScopeCount => Scopes.Count;
        public IEnumerable<CDefinition> AllVisibleLocals
        {
            get
            {
                foreach (var scope in Scopes)
                {
                    foreach (var cdef in scope.Locals)
                        yield return cdef;
                }
            }
        }
    }
    public IEnumerable<INamedType> AllVisibileNamedTypes
    {
        get
        {
            if (ctx is not null)
            {
                foreach (var scope in ctx.Scopes)
                {
                    foreach (var st in scope.NamedTypes)
                        yield return st;
                }
            }
            foreach (var st in GlobalNamedTypes)
            {
                yield return st;
            }
        }
    }

    public bool AddScopedDefinition(CDefinition def)
    {
        if (ctx is not null)
        {
            ctx.AllDefinitions.Add(def);
            return ctx.Scopes.Peek().Locals.Add(def);
        }
        else
        {
            return Globals.Add(def);
        }
    }
    private bool AddScopedNamedType(INamedType nt)
    {
        return ctx is not null 
            ? ctx.CurrentScope.NamedTypes.Add(nt) 
            : GlobalNamedTypes.Add(nt);
    }
    private FunctionContext ctx = default!;
    private readonly HashSet<INamedType> GlobalNamedTypes = [];
    private readonly HashSet<CDefinition> Globals = [];
    private readonly List<FunctionDeclaration> Functions = [];

    public IEnumerable<CDefinition> Definitions => (ctx?.AllVisibleLocals ?? [])
        .Concat(((IEnumerable<CDefinition>?)ctx?.Parameters) ?? [])
        .Concat(Functions)
        .Concat(Globals);

    public CDefinition? FindDefinition(string Identifier, DefinitionClass Classes) => Definitions
        .Where(d => Classes.HasFlag(d.DefClass))
        .FirstOrDefault(d => d.Identifier == Identifier);
    
    // program = (typedef | function-definition | global-variable)*
    public ProgramNode Parse(IEnumerable<Token> Enumerable)
    {
        using DuplicableEnumerator<Token> Enumerator = new(Enumerable);
        // prime the enumerator, move to first value
        Enumerator.MoveNext();
        var loc = Enumerator.Current.Location;
        List<Node> nodes = [];
        while (Enumerator.Current.Kind is not TokenKind.EOF)
        {
            var (basetype, typedef) = Typespec(in Enumerator);
            if (typedef)
            {
                ParseTypedef(in Enumerator, basetype);
                continue;
            }

            var decl = Declarator(in Enumerator, basetype, DefinitionClass.Undefined);
            if (FindDefinition(decl.Identifier, DefinitionClass.Function | DefinitionClass.Global) is not null)
                Utilities.ErrorAt(decl.IdentifierLocation, "Already defined");                
            if (decl.Type is FunctionType)
            {
                decl = decl with { DefClass = DefinitionClass.Function };
                var func = Function(in Enumerator, decl);
                Functions.Add(func);
                nodes.Add(func);
            }
            else
            {
                decl = decl with { DefClass = DefinitionClass.Global };
                Globals.Add(decl);
                nodes.Add(decl);
                int count = 1;
                while (!Enumerator.EqualToConsume(";"))
                {
                    if (count++ > 0)
                        Enumerator.Skip(",");

                    CDefinition variable = Declarator(in Enumerator, basetype, DefinitionClass.Global);
                    if (FindDefinition(variable.Identifier, DefinitionClass.Function | DefinitionClass.Global) is not null)
                        Utilities.ErrorAt(variable.IdentifierLocation, "Already defined");        
                    Globals.Add(variable);
                    nodes.Add(variable);
                }

            }
        }
        return new ProgramNode(loc.ToEnd(Enumerator.Current.Location), [.. nodes]);
    }

    private FunctionDeclaration Function(in DuplicableEnumerator<Token> Enumerator, CDefinition decl)
    {
        var functype = (FunctionType)decl.Type;
        var loc = Enumerator.Current.Location;

        if (Enumerator.EqualToConsume(";"))
        {
            return new FunctionDeclaration(
                decl.Location.ToEnd(loc), functype, decl.IdentifierLocation
            );
        }

        Debug.Assert(ctx is null);
        ctx = new FunctionContext(decl, functype.Parameters.ToImmutableHashSet());
        Enumerator.Skip("{");
        var statement = CompoundStatement(loc, in Enumerator);
        var vars = ctx.AllDefinitions.ToImmutableArray();
        var func = new Function(decl.Location.ToEnd(statement.Location),
            functype,
            decl.IdentifierLocation,
            statement,
            vars);
        ctx = null!;
        return func;
    }

    // stmt = "return" expr ";"
    //      | "if" "(" expr ")" stmt ("else" stmt)?
    //      | "for" "(" expr-stmt expr? ";" expr? ")" stmt
    //      | "while" "(" expr ")" stmt
    //      | "{" compound-stmt
    //      | expr-stmt
    private Statement Statement(in DuplicableEnumerator<Token> Enumerator)
    {
        Location loc = Enumerator.Current.Location;
        if (Enumerator.EqualToConsume("return"))
        {
            Expression expr = Expression(in Enumerator);
            loc = loc.ToEnd(Enumerator.Current.Location);
            Enumerator.Skip(";");
            var node = new UnaryStatement(loc, UnaryStmtKind.Return, expr);
            return node;
        }
        else if (Enumerator.EqualToConsume("{"))
        {
            return CompoundStatement(loc, in Enumerator);
        }
        else if (Enumerator.EqualToConsume("if"))
        {
            Enumerator.Skip("(");
            Expression expr = Expression(in Enumerator);
            Enumerator.Skip(")");
            Statement truestmt = Statement(in Enumerator);
            Statement? falsestmt = null;
            if (Enumerator.EqualToConsume("else"))
            {
                falsestmt = Statement(in Enumerator);
            }
            loc = loc.ToEnd(falsestmt?.Location ?? truestmt.Location);
            return new IfStatement(loc, expr, truestmt, falsestmt);
        }
        else if (Enumerator.EqualToConsume("for"))
        {
            Enumerator.Skip("(");
            Statement initializer = ExpressionStatement(in Enumerator);
            Expression? conditional = null, increment = null;
            if (!Enumerator.EqualToConsume(";"))
            {
                conditional = Expression(in Enumerator);
                Enumerator.Skip(";");
            }
            if (!Enumerator.EqualToConsume(")"))
            {
                increment = Expression(in Enumerator);
                Enumerator.Skip(")");
            }
            Statement body = Statement(in Enumerator);
            loc = loc.ToEnd(body.Location);
            return new ForStatement(
                loc,
                initializer,
                conditional,
                increment,
                body  
            );
        }
        else if (Enumerator.EqualToConsume("while"))
        {
            Enumerator.Skip("(");
            Expression conditional = Expression(in Enumerator);
            Enumerator.Skip(")");
            Statement body = Statement(in Enumerator);
            loc = loc.ToEnd(body.Location);
            var empty = loc.Range.Start.GetOffset(loc.InputText.Length);
            return new ForStatement(
                loc,
                new EmptyStatement(new Location(loc.Filename, loc.InputText, empty..empty)),
                conditional,
                null,
                body
            );
        }
        else
        {
            return ExpressionStatement(in Enumerator);
        }
    }

    // compound-stmt = (typedef | declaration | stmt)* "}"
    private BlockStatement CompoundStatement(Location location, in DuplicableEnumerator<Token> Enumerator)
    {
        ctx.BeginScope();
        List<Statement> nodes = [];
        while (!Enumerator.Current.EqualTo("}"))
        {
            if (IsTypename(Enumerator.Current))
            {
                var (basetype, typedef) = Typespec(in Enumerator);

                if (typedef)
                {
                    ParseTypedef(in Enumerator, basetype);
                }
                else
                {
                    nodes.Add(Declaration(in Enumerator, basetype));
                }

            }
            else
            {
                nodes.Add(Statement(in Enumerator));
            }
        }
        location = location.ToEnd(Enumerator.Current.Location);
        Enumerator.MoveNext();
        ctx.EndScope();
        return new BlockStatement(location, [.. nodes]);
    }
    
    private void ParseTypedef(in DuplicableEnumerator<Token> Enumerator, CType typedef)
    {
        bool first = true;

        while (!Enumerator.EqualToConsume(";"))
        {
            if (!first)
                Enumerator.Skip(",");
            first = false;

            var definition = Declarator(in Enumerator, typedef, DefinitionClass.Typedef);
            AddScopedDefinition(definition);
        }
    }

    private bool IsTypename(Token tok)
    {
        Span<string> keywords = ["char", "short", "int", "long", "union", "struct", "void", "typedef"];
        return keywords.Contains(tok.Identifier) || Definitions.Where(n => n.Identifier == tok.Identifier).FirstOrDefault()?.DefClass == DefinitionClass.Typedef;
    }

    private Statement Declaration(in DuplicableEnumerator<Token> Enumerator, CType basetype)
    {
        Location loc = Enumerator.Current.Location;
        List<Statement> Statements = []; 
        int count = 0;
        while (!Enumerator.Current.EqualTo(";"))
        {
            if (count++ > 0)
                Enumerator.Skip(",");

            CDefinition variable = Declarator(in Enumerator, basetype, DefinitionClass.LocalVariable);
            if (variable.Type is VoidType)
                Utilities.ErrorAt(variable.Location, "Variable cannot be void type");

            if (ctx.Parameters.Any(p => p.Identifier == variable.Identifier)
                || ctx.CurrentScope.Locals.Any(p => p.Identifier == variable.Identifier))
                Utilities.ErrorAt(variable.IdentifierLocation, "Variable already defined");
            bool added = AddScopedDefinition(variable);
            Debug.Assert(added);

            if (!Enumerator.EqualToConsume("="))
                continue;

            var lhs = new VariableExpression(variable.IdentifierLocation, variable);
            var rhs = Assign(in Enumerator);
            var node = new AssignmentExpression(lhs.Location.ToEnd(rhs.Location), lhs, rhs);
            Statements.Add(new UnaryStatement(node.Location, UnaryStmtKind.Expression, node));
        }

        loc = loc.ToEnd(Enumerator.Current.Location);
        Enumerator.Consume();
        return new BlockStatement(loc, [.. Statements]);
    }

    // typespec = typename typename*
    // typename = "void" | "char" | "short" | "int" | "long"
    //            | struct-decl | union-decl | typedef-name
    //
    // The order of typenames in a type-specifier doesn't matter. For
    // example, `int long static` means the same as `static long int`.
    // That can also be written as `static long` because you can omit
    // `int` if `long` or `short` are specified. However, something like
    // `char int` is not a valid type specifier. We have to accept only a
    // limited combinations of the typenames.
    //
    // In this function, we count the number of occurrences of each typename
    // while keeping the "current" type object that the typenames up
    // until that point represent. When we reach a non-typename token,
    // we returns the current type object.
    private (CType type, bool isTypedef) Typespec(in DuplicableEnumerator<Token> Enumerator, bool disallowStorageClass = false)
    {
        // todo: varattr* storage class specifier stuff
        const int Void  = 1 << 0;
        const int Char  = 1 << 2;
        const int Short = 1 << 4;
        const int Int   = 1 << 6;
        const int Long  = 1 << 8;
        const int Other = 1 << 10;

        CType? type = tres.GetBaseType(BaseType.Int32);
        int counter = default;
        bool isTypedef = false;

        while (IsTypename(Enumerator.Current))
        {
            if (Enumerator.EqualToConsume("typedef"))
            {
                if (disallowStorageClass)
                    Utilities.ErrorToken(Enumerator.Current, "Storage class not allowed here");
                if (isTypedef)
                    Utilities.ErrorToken(Enumerator.Current, "Typedef already specified");
                isTypedef = true;
                continue;
            }

            var id = Enumerator.Current.Identifier;
            var foundType = Definitions.Where(t => t.DefClass == DefinitionClass.Typedef).SingleOrDefault(td => td.Identifier == id);
            if (Enumerator.Current.EqualTo("struct") 
                || Enumerator.Current.EqualTo("union")
                || foundType is not null)
            {
                if (counter != 0) break;
                var tok = Enumerator.Current;
                Enumerator.Consume();
                if (foundType is not null)
                {
                    type = foundType.Type;
                }
                else
                {
                    type = StructOrUnionDeclaration(in Enumerator, IsUnion: tok.EqualTo("union"));
                }
                counter += Other;
                continue;
            }


            if (Enumerator.EqualToConsume("void"))
                counter += Void;
            else if (Enumerator.EqualToConsume("char"))
                counter += Char;
            else if (Enumerator.EqualToConsume("short"))
                counter += Short;
            else if (Enumerator.EqualToConsume("int"))
                counter += Int;
            else if (Enumerator.EqualToConsume("long"))
                counter += Long;
            else
                Debug.Assert(false);

            type = counter switch
            {
                Void => new VoidType(),
                Char => tres.GetBaseType(BaseType.UInt8),
                Short => tres.GetBaseType(BaseType.Int16),
                Short + Int => tres.GetBaseType(BaseType.Int16),
                Int => tres.GetBaseType(BaseType.Int32),
                Long => tres.GetBaseType(BaseType.Int64),
                Long + Int => tres.GetBaseType(BaseType.Int64),
                Long + Long => tres.GetBaseType(BaseType.Int64),
                Long + Long + Int => tres.GetBaseType(BaseType.Int64),
                _ => null
            };

            if (type is null)
            {
                if (counter == Long + Long + Long)
                    Utilities.ErrorToken(Enumerator.Current, "Too long");
                else
                    Utilities.ErrorToken(Enumerator.Current, "Invalid type");
            }
        }
        return (type, isTypedef);
    }

    private CType StructOrUnionDeclaration(in DuplicableEnumerator<Token> Enumerator, bool IsUnion)
    {
        Location loc = Enumerator.Current.Location;
        string? tag =  null;

        if (Enumerator.Current.Kind is TokenKind.Identifier)
        {
            tag = Enumerator.Current.Identifier;
            loc = loc.ToEnd(Enumerator.Current.Location);
            Enumerator.Consume();
        }

        if (tag is not null && !Enumerator.Current.EqualTo("{"))
        {
            var st = AllVisibileNamedTypes.FirstOrDefault(t => t.Name == tag);
            if (st is null)
            {
                Utilities.ErrorAt(loc, "unknown struct or union type");
                throw new InvalidOperationException();
            }
            return (CType)st;
        }

        Enumerator.Skip("{");

        List<CDefinition> members = new();
        while (!Enumerator.EqualToConsume("}"))
        {
            var (basetype, typedef) = Typespec(in Enumerator, disallowStorageClass: true);
            Debug.Assert(!typedef);
            int i = 0;
            while (!Enumerator.EqualToConsume(";"))
            {
                if (i++ != 0)
                    Enumerator.Skip(",");
                members.Add(Declarator(in Enumerator, basetype, DefinitionClass.StructMember));
            }
            loc = loc.ToEnd(Enumerator.Current.Location);
        }

        if (members.Select(m => m.Identifier).Distinct().Count() != members.Count)
        {
            Utilities.ErrorAt(loc, "struct with duplicate names");
        }

        var type = new StructType(tag, [.. members], IsUnion);
        if (tag is not null)
        {
            AddScopedNamedType(type);
        }
        return type;
    }

    // func-params = (param ("," param)*)? ")"
    // param       = typespec declarator
    private CType FuncParams(in DuplicableEnumerator<Token> Enumerator, CType type)
    {
        List<CDefinition> args = [];
        while (!Enumerator.EqualToConsume(")"))
        {
            if (args.Count != 0)
                Enumerator.Skip(",");
            var (basetype, typedef) = Typespec(in Enumerator, disallowStorageClass: true);
            Debug.Assert(!typedef);
            var decl = Declarator(in Enumerator, basetype, DefinitionClass.Parameter); 
            args.Add(decl);
        }
        return new FunctionType(type, [.. args]);
    }

    // type-suffix = "(" func-params
    //             | "[" num "]" type-suffix
    //             | ε
    private CType TypeSuffix(in DuplicableEnumerator<Token> Enumerator, CType type)
    {
        if (Enumerator.EqualToConsume("("))
            return FuncParams(in Enumerator, type);
        
        if (Enumerator.EqualToConsume("["))
        {
            if (Enumerator.Current.Kind is not TokenKind.Number)
                Utilities.ErrorAt(Enumerator.Current.Location, "Expected number");
            long count = Enumerator.Current.NumericValue;
            if (!Utilities.Contains(uint.MinValue, count, uint.MaxValue))
                Utilities.ErrorAt(Enumerator.Current.Location, "Too large");
            Enumerator.Consume();
            Enumerator.Skip("]");
            type = TypeSuffix(in Enumerator, type);
            return new ArrayType(type, (int)count);
        }

        return type;
    }

    // declarator = "*"* ("(" ident ")" | "(" declarator ")" | ident) type-suffix
    private CDefinition Declarator(in DuplicableEnumerator<Token> Enumerator, CType type, DefinitionClass Class)
    {
        while (Enumerator.EqualToConsume("*"))
            type = type.PointerTo;

        if (Enumerator.EqualToConsume("("))
        {
            using var duplicated = Enumerator.Duplicate();
            _ = Declarator(in duplicated, type, Class);
            duplicated.Skip(")");
            type = TypeSuffix(in duplicated, type);
            var def = Declarator(in Enumerator, type, Class);
            Enumerator.SkipTo(duplicated);
            return def;
        }
        
        if (Enumerator.Current.Kind != TokenKind.Identifier)
            Utilities.ErrorToken(Enumerator.Current, "Expected an identifier");
        Location ident = Enumerator.Current.Location;
        Enumerator.Consume();

        type = TypeSuffix(in Enumerator, type);
        var cvar = new CDefinition(type, Class, ident, ctx?.ScopeCount ?? 0);        
        return cvar;
    }

    // expr-stmt = expr? ";"
    private Statement ExpressionStatement(in DuplicableEnumerator<Token> Enumerator)
    {
        Location loc = Enumerator.Current.Location;
        if (Enumerator.EqualToConsume(";"))
        {
            return new EmptyStatement(loc);
        }
        var expr =  Expression(in Enumerator);
        loc = loc.ToEnd(Enumerator.Current.Location);
        var node = new UnaryStatement(loc, UnaryStmtKind.Expression, expr);
        Enumerator.Skip(";");
        return node;
    }

    // expr = assign ("," expr)?
    private Expression Expression(in DuplicableEnumerator<Token> Enumerator)
    {
        var node = Assign(in Enumerator);
        
        if (Enumerator.EqualToConsume(","))
        {
            var rhs = Expression(in Enumerator);
            return new CommaExpression(node.Location.ToEnd(rhs.Location), node, rhs);             
        }

        return node;
    }

    // assign = equality ("=" assign)?
    private Expression Assign(in DuplicableEnumerator<Token> Enumerator)
    {
        Location loc = Enumerator.Current.Location;
        Expression node = Equality(in Enumerator);
        if (Enumerator.EqualToConsume("="))
        {
            var rhs = Assign(in Enumerator); 
            loc = loc.ToEnd(rhs.Location);
            if (node is not ILValue ilv)
            {
                Utilities.ErrorAt(node.Location, "not an lvalue");
                throw new InvalidOperationException();
            }
            if (!rhs.Type(tres).CanBeAssignedTo(node.Type(tres)))
            {
                Utilities.ErrorAt(loc, "Invalid types");
            }
            node = new AssignmentExpression(loc, ilv, rhs);
        }
        return node;
    }

    // equality = relational ("==" relational | "!=" relational)*
    private Expression Equality(in DuplicableEnumerator<Token> Enumerator)
    {
        Expression node = Relational(in Enumerator);
        Location loc = node.Location;

        while (true)
        {
            if (Enumerator.EqualToConsume("=="))
            {
                Expression rhs = Relational(in Enumerator);
                loc = loc.ToEnd(rhs.Location);
                node = new BinaryExpression(loc, BinaryKind.Equal, node, rhs);
                continue;
            }

            if (Enumerator.EqualToConsume("!="))
            {
                Expression rhs = Relational(in Enumerator);
                loc = loc.ToEnd(rhs.Location);
                node = new BinaryExpression(loc, BinaryKind.NotEqual, node, rhs);
                continue;
            }

            return node;
        }
    }

    // relational = add ("<" add | "<=" add | ">" add | ">=" add)*
    private Expression Relational(in DuplicableEnumerator<Token> Enumerator)
    {
        Expression node = Add(in Enumerator);

        while (true)
        {
            if (Enumerator.EqualToConsume("<"))
            {
                Expression rhs = Add(in Enumerator);
                node = BinaryOperator(node, rhs, BinaryKind.LessThan);
                continue;
            }

            if (Enumerator.EqualToConsume("<="))
            {
                Expression rhs = Add(in Enumerator);
                node = BinaryOperator(node, rhs, BinaryKind.LessThanOrEqual);
                continue;
            }

            if (Enumerator.EqualToConsume(">"))
            {
                Expression rhs = Add(in Enumerator);
                node = BinaryOperator(rhs, node, BinaryKind.LessThan);
                continue;
            }

            if (Enumerator.EqualToConsume(">="))
            {
                Expression rhs = Add(in Enumerator);
                node = BinaryOperator(rhs, node, BinaryKind.LessThanOrEqual);
                continue;
            }

            return node;
        }
    }

    // add = mul ("+" mul | "-" mul)*
    private Expression Add(in DuplicableEnumerator<Token> Enumerator)
    {
        Expression node = Multiply(in Enumerator);

        while (true)
        {
            if (Enumerator.EqualToConsume("+"))
            {
                Expression rhs = Multiply(in Enumerator);
                node = BinaryOperator(node, rhs, BinaryKind.Add);
                continue;
            }

            if (Enumerator.EqualToConsume("-"))
            {
                Expression rhs = Multiply(in Enumerator);
                node = BinaryOperator(node, rhs, BinaryKind.Sub);
                continue;
            }

            return node;
        }

    }

    private Expression BinaryOperator(Expression lhs, Expression rhs, BinaryKind kind)
    {
        var loc = lhs.Location.ToEnd(rhs.Location);

        var ltype = lhs.Type(tres);
        var rtype = rhs.Type(tres);

        bool check = ltype is not IPtrType && rtype is IPtrType;
        check = check || (ltype is IntegralType lt && rtype is IntegralType rt && lt.IsNumeric && rt.IsFloatingPoint);
        if (check)
        {
            if (kind.HasFlag(BinaryKind.Symmetric))
            {
                (ltype, rtype) = (rtype, ltype);
                (lhs, rhs) = (rhs, lhs);
            }
            else
            {
                Utilities.ErrorAt(loc, "Invalid operands");
                throw new InvalidOperationException();
            }
        }

        var types = BinaryExpression.NumericOperandResult(
            tres,
            ltype,
            kind,
            rtype
        );
        if (types is null)
        {
            Utilities.ErrorAt(loc, "Invalid operands");
            throw new InvalidOperationException();
        }
        return new BinaryExpression(loc, kind, lhs, rhs);
    }

    // mul = cast ("*" cast | "/" cast)*
    private Expression Multiply(in DuplicableEnumerator<Token> Enumerator)
    {
        Expression node = Cast(in Enumerator);
        Location loc = node.Location;

        while (true)
        {
            if (Enumerator.EqualToConsume("*"))
            {
                Expression rhs = Cast(in Enumerator);
                loc = loc.ToEnd(rhs.Location);
                node = new BinaryExpression(loc, BinaryKind.Mul, node, rhs);
                continue;
            }

            if (Enumerator.EqualToConsume("/"))
            {
                Expression rhs = Cast(in Enumerator);
                loc = loc.ToEnd(rhs.Location);
                node = new BinaryExpression(loc, BinaryKind.Div, node, rhs);
                continue;
            }

            return node;
        }
    }

    // unary = ("+" | "-" | "*" | "&") cast
    //       | postfix
    private Expression Unary(in DuplicableEnumerator<Token> Enumerator)
    {
        Location loc = Enumerator.Current.Location;
        if (Enumerator.EqualToConsume("+"))
        {
            var unary = Cast(in Enumerator);
            return unary with { Location = loc.ToEnd(unary.Location) };
        }

        if (Enumerator.EqualToConsume("-"))
        {
            var unary = Cast(in Enumerator);
            loc = loc.ToEnd(unary.Location);
            return new NegationExpression(loc, unary);
        }

        if (Enumerator.EqualToConsume("&"))
        {
            var unary = Cast(in Enumerator);
            loc = loc.ToEnd(unary.Location);
            if (unary is not ILValue ilv)
            {
                Utilities.ErrorAt(unary.Location, "not an lvalue");
                throw new InvalidOperationException();
            }
            return new AddressOfExpression(loc, ilv);
        }

        if (Enumerator.EqualToConsume("*"))
        {
            var unary = Cast(in Enumerator);
            loc = loc.ToEnd(unary.Location);
            if (unary.Type(tres) is PointerToType { Internal: VoidType })
            {
                Utilities.ErrorAt(loc, "Dereferencing a void pointer");
            }
            return new DereferenceExpression(loc, unary);
        }

        return Postfix(in Enumerator);
    }

    // postfix = primary ("[" expr "]" | "." ident | "->" ident)*
    private Expression Postfix(in DuplicableEnumerator<Token> Enumerator)
    {
        Expression node = Primary(in Enumerator);

        while (true)
        {
            Location loc = Enumerator.Current.Location;
            if (Enumerator.EqualToConsume("["))
            {
                Expression idx = Expression(in Enumerator);
                loc = loc.ToEnd(Enumerator.Current.Location);
                Enumerator.Skip("]");
                node = new DereferenceExpression(loc, BinaryOperator(node, idx, BinaryKind.Add));
                continue;
            }

            if (Enumerator.EqualToConsume("."))
            {
                if (node.Type(tres) is not StructType st)
                {
                    Utilities.ErrorAt(node.Location, "not a struct");
                    throw new InvalidOperationException();
                }
                if (Enumerator.Current.Kind is not TokenKind.Identifier)
                {
                    Utilities.ErrorToken(Enumerator.Current, "identifier expected");
                    throw new InvalidOperationException();
                }
                var identifier = Enumerator.Current;
                Enumerator.Consume();
                var member = st.Members.SingleOrDefault(m => m.Identifier == identifier.Identifier);
                if (member is null)
                {
                    Utilities.ErrorToken(Enumerator.Current, $"no such member in {st.Name ?? "struct"}");
                    throw new InvalidOperationException();
                }
                if (node is not ILValue ilv)
                {
                    node = new StructMemberRValue(identifier.Location, node, member);
                }
                else
                {
                    node = new StructMemberLValue(identifier.Location, ilv, member);
                }
                continue;
            }

            if (Enumerator.Current.EqualTo("->"))
            {
                var tokloc = Enumerator.Current.Location;
                Enumerator.Consume();
                if (node.Type(tres) is not PointerToType { Internal: StructType st })
                {
                    Utilities.ErrorAt(node.Location, "not a ptr to struct");
                    throw new InvalidOperationException();
                }
                if (Enumerator.Current.Kind is not TokenKind.Identifier)
                {
                    Utilities.ErrorToken(Enumerator.Current, "identifier expected");
                    throw new InvalidOperationException();
                }
                var identifier = Enumerator.Current;
                Enumerator.Consume();
                var member = st.Members.SingleOrDefault(m => m.Identifier == identifier.Identifier);
                if (member is null)
                {
                    Utilities.ErrorToken(Enumerator.Current, $"no such member in {st.Name ?? "struct"}");
                    throw new InvalidOperationException();
                }
                node = new StructMemberLValue(identifier.Location, new DereferenceExpression(tokloc, node), member);
                continue;
            }

            return node;
        }
    }

    // primary = "(" "{" stmt stmt* "}" ")"
    //         | "(" expr ")"
    //         | "sizeof" "(" type-name ")"
    //         | "sizeof" unary
    //         | ident func-args?
    //         | str
    //         | num
    private Expression Primary(in DuplicableEnumerator<Token> Enumerator)
    {
        Location loc = Enumerator.Current.Location;
        if (Enumerator.EqualToConsume("("))
        {
            if (Enumerator.EqualToConsume("{"))
            {
                var body = CompoundStatement(loc, in Enumerator);
                var last = body.Nodes.Last();
                if (last is not UnaryStatement { Kind: UnaryStmtKind.Expression})
                    Utilities.ErrorAt(last.Location, "Void return not supported");
                loc = loc.ToEnd(Enumerator.Current.Location);
                Enumerator.Skip(")");
                return new StatementExpression(loc, body);
            }
            else
            {
                Expression node = Expression(in Enumerator);
                loc = loc.ToEnd(Enumerator.Current.Location);
                Enumerator.Skip(")");
                return node with { Location = loc };
            }
        }

        if (Enumerator.EqualToConsume("sizeof"))
        {
            using var dup = Enumerator.Duplicate();
            if (dup.EqualToConsume("(") && IsTypename(dup.Current))
            {
                Enumerator.Skip("(");
                var type = Typename(in Enumerator);
                loc = loc.ToEnd(Enumerator.Current.Location);
                Enumerator.Skip(")");
                return new SizeofExpression(loc, null, type);
            }
            else
            {
                var expr = Unary(in Enumerator);
                return new SizeofExpression(expr.Location, expr, expr.Type(tres));
            }
        }

        if (Enumerator.Current.Kind is TokenKind.Number)
        {
            long val = Enumerator.Current.NumericValue;
            Enumerator.Consume();
            return new LiteralIntegerExpression(loc, val);
        }

        if (Enumerator.Current.Kind is TokenKind.Identifier)
        {
            var identtok = Enumerator.Current;
            Enumerator.Consume();
            if (Enumerator.EqualToConsume("("))
                return Funccall(loc, identtok.Identifier, in Enumerator);

            var variable = FindDefinition(identtok.Identifier, DefinitionClass.Parameter | DefinitionClass.LocalVariable | DefinitionClass.Global);
            if (variable is null)
                Utilities.ErrorToken(identtok, "Undefined variable");
            var node = new VariableExpression(loc, variable);
            return node;
        }

        if (Enumerator.Current.Kind is TokenKind.String)
        {
            byte[] value = Enumerator.Current.StringLiteral;
            Enumerator.Consume();
            return new LiteralStringExpression(loc, value);
        }

        Utilities.ErrorToken(Enumerator.Current, "Expected an expression");
        return null;
    }

    private FunctionCall Funccall(Location loc, string Identifier, in DuplicableEnumerator<Token> Enumerator)
    {
        List<Expression> args = [];
        while (!Enumerator.Current.EqualTo(")"))
        {
            if (args.Count != 0)
                Enumerator.Skip(",");
            args.Add(Assign(in Enumerator));
        }
        loc = loc.ToEnd(Enumerator.Current.Location);
        Enumerator.Skip(")");
        var ftype = new FunctionType(
            tres.GetBaseType(BaseType.Int32),
            args.Select(a => a.Type(tres)).ToImmutableArray()
        );
        var method = tres.GetMethod(Identifier, ftype);
        method ??= Functions
            .Where(f => f.Identifier == Identifier)
            .Where(f => ftype.CanBeAssignedTo(f.Type))
            .Select(f => f.MakeRef)
            .SingleOrDefault();
        method ??= ftype.CanBeAssignedTo(ctx.Definition.Type) 
            ? new FunctionReference(Identifier, (FunctionType)ctx.Definition.Type, null)
            : null;
        if (method is null)
        {
            Utilities.Log(ftype.ToString());
            Utilities.ErrorAt(loc, "Could not resolve method");
        }
        return new FunctionCall(
            loc,
            Identifier,
            method,
            [.. args]
        );
    }

    private CType AbstractDeclarator(in DuplicableEnumerator<Token> Enumerator, CType type)
    {
        while (Enumerator.EqualToConsume("*"))
            type = type?.PointerTo!;

        if (Enumerator.EqualToConsume("("))
        {
            using var duplicated = Enumerator.Duplicate();
            _ = AbstractDeclarator(in duplicated, null!);
            duplicated.Skip(")");
            Debug.Assert(type is not null);
            type = TypeSuffix(in duplicated, type);
            var def = AbstractDeclarator(in Enumerator, type);
            Enumerator.SkipTo(duplicated);
            return def;
        }

        return TypeSuffix(in Enumerator, type!);
    }

    private CType Typename(in DuplicableEnumerator<Token> Enumerator)
    {
        var (type, typedef) = Typespec(in Enumerator, disallowStorageClass: true);
        Debug.Assert(!typedef);
        return AbstractDeclarator(in Enumerator, type);
    }

    private Expression Cast(in DuplicableEnumerator<Token> Enumerator)
    {
        using var dup = Enumerator.Duplicate();
        if (dup.EqualToConsume("(") && IsTypename(dup.Current))
        {
            Location loc = Enumerator.Current.Location;
            Enumerator.Skip("(");
            var type = Typename(in Enumerator);
            loc = loc.ToEnd(Enumerator.Current.Location);
            Enumerator.Skip(")");
            return new CastExpression(loc, Cast(in Enumerator), type);
        }

        return Unary(in Enumerator);
    }
}
