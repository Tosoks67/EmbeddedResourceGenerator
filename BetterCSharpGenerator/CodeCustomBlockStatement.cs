using System.CodeDom;

namespace EmbeddedResourceGenerator.BetterCSharpGenerator;

public class CodeCustomBlockStatement : CodeStatement
{
    public string Name { get; set; } = string.Empty;
    public CodeExpression? Expression { get; set; }
    public CodeStatementCollection Statements { get; } = [];
}
