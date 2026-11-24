using System.CodeDom;

namespace EmbeddedResourceGenerator.BetterCSharpGenerator;

public class CodeTernaryExpression(
    CodeExpression condition,
    CodeExpression trueExpression,
    CodeExpression falseExpression)
    : CodeExpression
{
    public CodeExpression Condition { get; } = condition;
    public CodeExpression TrueExpression { get; } = trueExpression;
    public CodeExpression FalseExpression { get; } = falseExpression;
}
