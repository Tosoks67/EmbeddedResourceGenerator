using System.CodeDom;
using System.CodeDom.Compiler;
using System.Globalization;
using System.Resources.Tools;
using HarmonyLib;
using Microsoft.CSharp;

namespace EmbeddedResourceGenerator;

internal static class Program
{
    internal static readonly CodeGeneratorOptions GeneratorOptions = new()
    {
        BracingStyle = "C"
    };

    internal static string ResourceSuffix { get; private set; } = ".resources";

    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine(
                "Usage: EmbeddedResourceGenerator " +
                "<input.resx> " +
                "<namespace> " +
                "<project folder> " +
                "[is internal? = true]" +
                "[resource files suffix = .resources]"
            );

            return 1;
        }

        Console.WriteLine($"Started embedding resources via {nameof(EmbeddedResourceGenerator)}");

        var harmony = new Harmony($"T67.{nameof(EmbeddedResourceGenerator)}");
        harmony.PatchAll();

        var input = args[0];
        var ns = args[1];
        var projectFolder = args[2];

        var firstNs = ns.Split('.').First();

        if (args.Length < 4 || !bool.TryParse(args[3], out var isInternal))
            isInternal = true;

        if (args.Length >= 5)
            ResourceSuffix = args[4];

        var className = Path.GetFileNameWithoutExtension(input);

        using var provider = new CSharpCodeProvider();

        var originalCulture = CultureInfo.CurrentUICulture;

        string[] unmatchable;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            var unit = StronglyTypedResourceBuilder.Create(
                input,
                className,
                ns,
                provider,
                isInternal,
                out unmatchable
            );

            ResourceCacheManagerCreator.CreateAndWrite(projectFolder, firstNs);


            harmony.UnpatchAll(harmony.Id);

            var resourceClassTuple = unit.Namespaces
                .Cast<CodeNamespace>()
                .SelectMany(
                    static n => n.Types.Cast<CodeTypeDeclaration>(),
                    static (n, t) => (Namespace: n, Type: t)
                )
                .Single(t => t.Type.Name == className);

            resourceClassTuple.Namespace.Imports.Add(
                new CodeNamespaceImport(firstNs)
            );

            var resourceManagerProperty = resourceClassTuple.Type.Members
                .OfType<CodeMemberProperty>()
                .Single(static property => property.Name is "ResourceManager");

            var field = new CodeFieldReferenceExpression(null, "resourceMan");

            resourceManagerProperty.GetStatements.Clear();
            resourceManagerProperty.GetStatements.AddRange(
                [
                    new CodeConditionStatement(
                        new CodeMethodInvokeExpression(
                            new CodeMethodReferenceExpression(
                                new CodeTypeReferenceExpression(typeof(object)),
                                nameof(ReferenceEquals)
                            ),
                            field,
                            new CodePrimitiveExpression(null)
                        ),
                        new CodeAssignStatement(
                            field,
                            new CodeObjectCreateExpression(
                                new CodeTypeReference(ResourceCacheManagerCreator.ClassName),
                                new CodePrimitiveExpression($"{ns}.{className}"),
                                new CodePropertyReferenceExpression(
                                    new CodeTypeOfExpression(className),
                                    nameof(Type.Assembly)
                                )
                            )
                        )
                    ),
                    new CodeMethodReturnStatement(field)
                ]
            );

            using var writer = new StreamWriter(Path.ChangeExtension(input, ".Designer.cs"));

            provider.GenerateCodeFromCompileUnit(
                unit,
                writer,
                GeneratorOptions
            );
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }

        // ReSharper disable once InvertIf
        if (unmatchable.Length > 0)
        {
            Console.Error.WriteLine("Some resource names cannot be represented as identifiers:");

            foreach (var name in unmatchable)
                Console.Error.WriteLine(name);
        }

        return 0;
    }
}
