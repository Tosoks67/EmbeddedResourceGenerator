using System.CodeDom;

namespace EmbeddedResourceGenerator.BetterCSharpGenerator;

public class CodeLambdaExpression : CodeExpression
{
    public CodeLambdaExpression(params CodeStatement[] statements)
    {
        Statements = new CodeStatementCollection(statements);
        IsExpressionLambda = false;
    }

    public CodeLambdaExpression(CodeExpression expression)
    {
        Expression = expression;
        IsExpressionLambda = true;
    }

    /// <summary>
    ///     Set this and set <see cref="IsExpressionLambda" />
    ///     to <see langword="true" />
    ///     if you wish for this lambda
    ///     to be an expression one
    /// </summary>
    public CodeExpression? Expression { get; set; }

    /// <summary>
    ///     Change this and set <see cref="IsExpressionLambda" />
    ///     to <see langword="false" />
    ///     if you wish for this lambda
    ///     to be a statement one
    /// </summary>
    public CodeStatementCollection Statements { get; } = [];

    public CodeParameterDeclarationExpressionCollection Parameters { get; } = [];
    public CodeTypeReference? ReturnType { get; set; } = null;
    public bool IsStatic { get; set; } = false;
    public bool IsExpressionLambda { get; set; }
}
