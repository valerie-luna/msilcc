using System.Diagnostics;
using Msilcc.Metadata;
using Msilcc.Types;

namespace Msilcc.Parser;

public record StatementExpression(Location Location, BlockStatement Statements) : Expression(Location)
{
    public override CType Type(IMetadataResolver resolver)
    {
        var statement = Statements.Nodes.Last() as UnaryStatement;
        Debug.Assert(statement is not null && statement.Kind is UnaryStmtKind.Expression);
        return statement.Node.Type(resolver);
    }

    public override void Visit(INodeVisitor visitor) => visitor.VisitStatementExpression(this);
    public override T Visit<T>(NodeVisitor<T> visitor) => visitor.VisitStatementExpression(this);
}