using System.CodeDom;

namespace EmbeddedResourceGenerator.BetterCSharpGenerator;

public class CodeMethodYieldReturnStatement : CodeMethodReturnStatement
{
    public CodeMethodYieldReturnStatement()
    {
    }

    public CodeMethodYieldReturnStatement(CodeExpression expression) : base(expression)
    {
    }
}
