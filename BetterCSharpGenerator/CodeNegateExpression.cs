using System.CodeDom;

namespace EmbeddedResourceGenerator.BetterCSharpGenerator;

public class CodeNegateExpression(CodeExpression expression) : CodeExpression
{
    public CodeExpression Expression { get; } = expression;
}
