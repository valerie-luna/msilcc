using System.Numerics;

namespace Msilcc.Parser;

public abstract class NodeVisitor<T>
{
    public virtual T VisitLiteralInteger(LiteralIntegerExpression node) => throw new NotImplementedException();
    public virtual T VisitBinary(BinaryExpression node) => throw new NotImplementedException();
    public virtual T VisitBlock(BlockStatement node) => throw new NotImplementedException();
    public virtual T VisitVariable(VariableExpression node) => throw new NotImplementedException();
    public virtual T VisitUnary(UnaryStatement node) => throw new NotImplementedException();
    public virtual T VisitIf(IfStatement node) => throw new NotImplementedException();
    public virtual T VisitFor(ForStatement node) => throw new NotImplementedException();
    public virtual T VisitEmpty(EmptyStatement node) => throw new NotImplementedException();
    public virtual T VisitAssignment(AssignmentExpression node) => throw new NotImplementedException();
    public virtual T VisitAddressOf(AddressOfExpression node) => throw new NotImplementedException();
    public virtual T VisitNegation(NegationExpression node) => throw new NotImplementedException();
    public virtual T VisitDereference(DereferenceExpression node) => throw new NotImplementedException();
    public virtual T VisitSizeof(SizeofExpression node) => throw new NotImplementedException();
    public virtual T VisitFunction(Function node) => throw new NotImplementedException();
    public virtual T VisitFunctionCall(FunctionCall node) => throw new NotImplementedException();
    public virtual T VisitProgram(ProgramNode node) => throw new NotImplementedException();
    public virtual T VisitDefinition(CDefinition node) => throw new NotImplementedException();
    public virtual T VisitLiteralString(LiteralStringExpression node) => throw new NotImplementedException();
    public virtual T VisitStatementExpression(StatementExpression node) => throw new NotImplementedException();
    public virtual T VisitComma(CommaExpression node) => throw new NotImplementedException();
    public virtual T VisitMemberAccessL(StructMemberLValue node) => throw new NotImplementedException();
    public virtual T VisitMemberAccessR(StructMemberRValue node) => throw new NotImplementedException();
    public virtual T VisitFunctionDeclaration(FunctionDeclaration functionDeclaration) => throw new NotImplementedException();
    public virtual T VisitCast(CastExpression castExpression) => throw new NotImplementedException();
}

public interface INodeVisitor
{
    void VisitLiteralInteger(LiteralIntegerExpression node);
    void VisitBinary(BinaryExpression node);
    void VisitBlock(BlockStatement node);
    void VisitVariable(VariableExpression node);
    void VisitUnary(UnaryStatement node);
    void VisitIf(IfStatement node);
    void VisitFor(ForStatement node);
    void VisitEmpty(EmptyStatement node);
    void VisitAssignment(AssignmentExpression node);
    void VisitAddressOf(AddressOfExpression node);
    void VisitNegation(NegationExpression node);
    void VisitDereference(DereferenceExpression node);
    void VisitSizeof(SizeofExpression node);
    void VisitFunction(Function node);
    void VisitFunctionCall(FunctionCall node);
    void VisitProgram(ProgramNode node);
    void VisitDefinition(CDefinition node);
    void VisitLiteralString(LiteralStringExpression node);
    void VisitStatementExpression(StatementExpression node);
    void VisitComma(CommaExpression node);
    void VisitMemberAccessL(StructMemberLValue node);
    void VisitMemberAccessR(StructMemberRValue node);
    void VisitFunctionDeclaration(FunctionDeclaration node);
    void VisitCast(CastExpression castExpression);
}
