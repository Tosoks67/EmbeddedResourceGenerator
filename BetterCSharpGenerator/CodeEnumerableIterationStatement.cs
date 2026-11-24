using System.CodeDom;

namespace EmbeddedResourceGenerator.BetterCSharpGenerator;

public class CodeEnumerableIterationStatement(
    (CodeTypeReference Type, string Name) variableInfo,
    CodeExpression enumerableExpression)
    : CodeStatement
{
    public (CodeTypeReference Type, string Name) VariableInfo { get; set; } = variableInfo;
    public CodeExpression EnumerableExpression { get; set; } = enumerableExpression;

    public CodeStatementCollection Statements { get; } = [];
}
