using System.CodeDom;
using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace EmbeddedResourceGenerator.BetterCSharpGenerator;

[SuppressMessage("ReSharper", "UnusedType.Global")]
public static class CSharpCodeGeneratorPatches
{
    private static readonly Type GeneratorType = AccessTools.TypeByName("Microsoft.CSharp.CSharpCodeGenerator");

    private static readonly MethodInfo GenerateExpressionMethod =
        AccessTools.Method(
            GeneratorType,
            "GenerateExpression",
            [typeof(CodeExpression)]
        );

    private static readonly MethodInfo OutputStartingBraceMethod =
        AccessTools.Method(
            GeneratorType,
            "OutputStartingBrace"
        );

    private static readonly MethodInfo GenerateStatementsMethod =
        AccessTools.Method(
            GeneratorType,
            "GenerateStatements",
            [typeof(CodeStatementCollection)]
        );

    private static readonly MethodInfo GenerateStatementMethod =
        AccessTools.Method(
            GeneratorType,
            "GenerateStatement",
            [typeof(CodeStatement)]
        );

    private static readonly MethodInfo GenerateMethodReturnStatementMethod =
        AccessTools.Method(
            GeneratorType,
            "GenerateMethodReturnStatement",
            [typeof(CodeMethodReturnStatement)]
        );

    private static readonly MethodInfo OutputParametersMethod =
        AccessTools.Method(
            GeneratorType,
            "OutputParameters",
            [typeof(CodeParameterDeclarationExpressionCollection)]
        );

    private static readonly MethodInfo OutputTypeMethod =
        AccessTools.Method(
            GeneratorType,
            "OutputType",
            [typeof(CodeTypeReference)]
        );

    private static readonly MethodInfo OutputTypeNamePairMethod =
        AccessTools.Method(
            GeneratorType,
            "OutputTypeNamePair",
            [typeof(CodeTypeReference), typeof(string)]
        );

    private static readonly FieldInfo OutputField =
        AccessTools.Field(GeneratorType, "output");

    private static readonly FieldInfo GeneratingForLoopField =
        AccessTools.Field(GeneratorType, "generatingForLoop");

    [HarmonyPatch]
    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    public static class GenerateStatementPatch
    {
        private static MethodBase TargetMethod() => GenerateStatementMethod;

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            var newInstructions = instructions.ToList();

            var commentCheckIndex = newInstructions
                .FindIndex(static code =>
                    code.Is(
                        OpCodes.Isinst,
                        typeof(CodeCommentStatement)
                    )
                );

            if (commentCheckIndex <= 0)
            {
                Console.Error.WriteLine(
                    $"""
                     Couldn't find a
                     {OpCodes.Isinst}    {typeof(CodeCommentStatement)}
                     instruction or it's the first instruction (broken IL)
                     """
                );
                return newInstructions;
            }

            var branchToIfEnd = newInstructions
                .Skip(commentCheckIndex)
                .FirstOrDefault(static code =>
                    code.opcode == OpCodes.Br
                    || code.opcode == OpCodes.Br_S
                );

            if (branchToIfEnd?.operand is not Label ifEndLabel)
            {
                Console.Error.WriteLine(
                    $"""
                     Instruction
                     {branchToIfEnd?.ToString() ?? "<null>"}
                     doesn't have a {nameof(Label)} operand
                     """
                );
                return newInstructions;
            }

            var continuingLabel = generator.DefineLabel();
            var joiningLabel = generator.DefineLabel();

            var firstCheckInstruction = newInstructions[commentCheckIndex - 1];
            var firstCheckLabels = firstCheckInstruction.labels.ToArray();

            firstCheckInstruction.labels = [continuingLabel];

            newInstructions.InsertRange(
                commentCheckIndex - 1,
                [
                    new CodeInstruction(OpCodes.Ldarg_1)
                        .WithLabels(firstCheckLabels),
                    new CodeInstruction(OpCodes.Isinst, typeof(CodeCustomBlockStatement)),
                    new CodeInstruction(OpCodes.Brfalse_S, joiningLabel),

                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Ldfld, OutputField),
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Castclass, typeof(CodeCustomBlockStatement)),
                    new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(
                            typeof(GenerateStatementPatch),
                            nameof(WriteCustomBlockStatement)
                        )
                    ),

                    new CodeInstruction(OpCodes.Br, ifEndLabel),


                    new CodeInstruction(OpCodes.Ldarg_1)
                        .WithLabels(joiningLabel),
                    new CodeInstruction(OpCodes.Isinst, typeof(CodeEnumerableIterationStatement)),
                    new CodeInstruction(OpCodes.Brfalse_S, continuingLabel),

                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Ldfld, OutputField),
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Castclass, typeof(CodeEnumerableIterationStatement)),
                    new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(
                            typeof(GenerateStatementPatch),
                            nameof(WriteEnumerableIterationStatement)
                        )
                    ),

                    new CodeInstruction(OpCodes.Br, ifEndLabel)
                ]
            );

            return newInstructions;
        }

        private static void WriteCustomBlockStatement(
            // CSharpCodeGenerator
            object generator,
            IndentedTextWriter output,
            CodeCustomBlockStatement e)
        {
            if (!GeneratorType.IsInstanceOfType(generator))
            {
                Console.Error.WriteLine(
                    $"""
                     [{nameof(WriteCustomBlockStatement)}]
                     {nameof(generator)} is of type
                     {generator.GetType()}
                     instead of
                     {GeneratorType}
                     """
                );
                return;
            }

            output.Write(e.Name);
            if (e.Expression is not null)
            {
                output.Write(" (");
                GenerateExpressionMethod.Invoke(generator, [e.Expression]);
                output.Write(")");
            }

            OutputStartingBraceMethod.Invoke(generator, null);
            output.Indent++;
            GenerateStatementsMethod.Invoke(generator, [e.Statements]);
            output.Indent--;
            output.WriteLine("}");
        }

        private static void WriteEnumerableIterationStatement(
            object generator,
            IndentedTextWriter output,
            CodeEnumerableIterationStatement e)
        {
            if (!GeneratorType.IsInstanceOfType(generator))
            {
                Console.Error.WriteLine(
                    $"""
                     [{nameof(WriteEnumerableIterationStatement)}]
                     {nameof(generator)} is of type
                     {generator.GetType()}
                     instead of
                     {GeneratorType}
                     """
                );
                return;
            }

            GeneratingForLoopField.SetValue(generator, true);
            output.Write("foreach (");
            OutputTypeNamePairMethod.Invoke(generator, [e.VariableInfo.Type, e.VariableInfo.Name]);
            output.Write(" in ");
            GenerateExpressionMethod.Invoke(generator, [e.EnumerableExpression]);
            output.Write(")");
            OutputStartingBraceMethod.Invoke(generator, null);
            GeneratingForLoopField.SetValue(generator, false);
            output.Indent++;
            GenerateStatementsMethod.Invoke(generator, [e.Statements]);
            output.Indent--;
            output.WriteLine("}");
        }
    }

    [HarmonyPatch]
    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    public static class GenerateMethodReturnStatementPatch
    {
        private static MethodBase TargetMethod() => GenerateMethodReturnStatementMethod;


        // ReSharper disable once InconsistentNaming
        private static void Prefix(CodeMethodReturnStatement e, IndentedTextWriter ___output)
        {
            if (e is not CodeMethodYieldReturnStatement)
                return;

            ___output.Write("yield ");
        }
    }

    [HarmonyPatch]
    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    public static class GenerateExpressionPatch
    {
        private static MethodBase TargetMethod() => GenerateExpressionMethod;

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            var newInstructions = instructions.ToList();

            var arrayCreateCheckIndex = newInstructions
                .FindIndex(static code =>
                    code.Is(
                        OpCodes.Isinst,
                        typeof(CodeArrayCreateExpression)
                    )
                );

            if (arrayCreateCheckIndex <= 0)
            {
                Console.Error.WriteLine(
                    $"""
                     Couldn't find a
                     {OpCodes.Isinst}    {typeof(CodeArrayCreateExpression)}
                     instruction or it's the first instruction (broken IL)
                     """
                );
                return newInstructions;
            }

            var continuingLabel = generator.DefineLabel();
            var joiningLabel1 = generator.DefineLabel();
            var joiningLabel2 = generator.DefineLabel();

            var firstCheckInstruction = newInstructions[arrayCreateCheckIndex - 1];
            var firstCheckLabels = firstCheckInstruction.labels.ToArray();

            firstCheckInstruction.labels = [continuingLabel];

            newInstructions.InsertRange(
                arrayCreateCheckIndex - 1,
                [
                    new CodeInstruction(OpCodes.Ldarg_1)
                        .WithLabels(firstCheckLabels),
                    new CodeInstruction(OpCodes.Isinst, typeof(CodeLambdaExpression)),
                    new CodeInstruction(OpCodes.Brfalse_S, joiningLabel1),

                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Ldfld, OutputField),
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Castclass, typeof(CodeLambdaExpression)),
                    new CodeInstruction(
                        OpCodes.Call,
                        AccessTools.Method(
                            typeof(GenerateExpressionPatch),
                            nameof(WriteLambdaExpression)
                        )
                    ),

                    new CodeInstruction(OpCodes.Ret),


                    new CodeInstruction(OpCodes.Ldarg_1)
                        .WithLabels(joiningLabel1),
                    new CodeInstruction(OpCodes.Isinst, typeof(CodeTernaryExpression)),
                    new CodeInstruction(OpCodes.Brfalse_S, joiningLabel2),

                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Ldfld, OutputField),
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Castclass, typeof(CodeTernaryExpression)),
                    new CodeInstruction(
                        OpCodes.Call,
                        AccessTools.Method(
                            typeof(GenerateExpressionPatch),
                            nameof(WriteTernaryExpression)
                        )
                    ),

                    new CodeInstruction(OpCodes.Ret),


                    new CodeInstruction(OpCodes.Ldarg_1)
                        .WithLabels(joiningLabel2),
                    new CodeInstruction(OpCodes.Isinst, typeof(CodeNegateExpression)),
                    new CodeInstruction(OpCodes.Brfalse_S, continuingLabel),

                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Dup),
                    new CodeInstruction(OpCodes.Ldfld, OutputField),
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Castclass, typeof(CodeNegateExpression)),
                    new CodeInstruction(
                        OpCodes.Call,
                        AccessTools.Method(
                            typeof(GenerateExpressionPatch),
                            nameof(WriteNegateExpression)
                        )
                    ),

                    new CodeInstruction(OpCodes.Ret)
                ]
            );

            return newInstructions;
        }

        private static void WriteLambdaExpression(
            object generator,
            IndentedTextWriter output,
            CodeLambdaExpression e)
        {
            if (!GeneratorType.IsInstanceOfType(generator))
            {
                Console.Error.WriteLine(
                    $"""
                     [{nameof(WriteLambdaExpression)}]
                     {nameof(generator)} is of type
                     {generator.GetType()}
                     instead of
                     {GeneratorType}
                     """
                );
                return;
            }

            if (e.IsStatic)
                output.Write("static ");

            if (e.ReturnType is not null)
                OutputTypeMethod.Invoke(generator, [e.ReturnType]);

            output.Write("(");
            OutputParametersMethod.Invoke(generator, [e.Parameters]);
            output.Write(") =>");
            if (!e.IsExpressionLambda)
            {
                OutputStartingBraceMethod.Invoke(generator, null);
                output.Indent++;
                GenerateStatementsMethod.Invoke(generator, [e.Statements]);
                output.Indent--;
                output.Write("}");
            }
            else if (e.Expression is not null)
            {
                output.Write(" (");
                GenerateExpressionMethod.Invoke(generator, [e.Expression]);
                output.Write(")");
            }
            else
            {
                throw new InvalidOperationException(
                    $"{nameof(CodeLambdaExpression)} was provided to {nameof(WriteLambdaExpression)}" +
                    $"with {nameof(CodeLambdaExpression.IsExpressionLambda)} being true and " +
                    $"{nameof(CodeLambdaExpression.Expression)} being null."
                );
            }
        }

        private static void WriteTernaryExpression(
            object generator,
            IndentedTextWriter output,
            CodeTernaryExpression e)
        {
            if (!GeneratorType.IsInstanceOfType(generator))
            {
                Console.Error.WriteLine(
                    $"""
                     [{nameof(WriteTernaryExpression)}]
                     {nameof(generator)} is of type
                     {generator.GetType()}
                     instead of
                     {GeneratorType}
                     """
                );
                return;
            }

            output.Write("(");
            GenerateExpressionMethod.Invoke(generator, [e.Condition]);
            output.Write(") ? (");
            GenerateExpressionMethod.Invoke(generator, [e.TrueExpression]);
            output.Write(") : (");
            GenerateExpressionMethod.Invoke(generator, [e.FalseExpression]);
            output.Write(")");
        }

        private static void WriteNegateExpression(
            object generator,
            IndentedTextWriter output,
            CodeNegateExpression e)
        {
            if (!GeneratorType.IsInstanceOfType(generator))
            {
                Console.Error.WriteLine(
                    $"""
                     [{nameof(WriteNegateExpression)}]
                     {nameof(generator)} is of type
                     {generator.GetType()}
                     instead of
                     {GeneratorType}
                     """
                );
                return;
            }

            output.Write("!(");
            GenerateExpressionMethod.Invoke(generator, [e.Expression]);
            output.Write(")");
        }
    }
}
