using System.CodeDom;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Resources;
using EmbeddedResourceGenerator.BetterCSharpGenerator;
using Microsoft.CSharp;
using ResourceAssembliesFieldType =
    System.Collections.Concurrent.ConcurrentDictionary<System.Globalization.CultureInfo, System.Resources.ResourceSet>;

namespace EmbeddedResourceGenerator;

[SuppressMessage("ReSharper", "BitwiseOperatorOnEnumWithoutFlags")]
public static class ResourceCacheManagerCreator
{
    internal const string ClassName = "ResourceCacheManager";

    private const string ResourceAssembliesFieldName = "_resourceAssemblies";
    private const string ResourceLockFieldName = "_resourceLock";

    private const string InternalGetResourceSetMethod = "InternalGetResourceSet";
    private const string FallbackCulturesMethod = "FallbackCultures";
    private const string ReleaseAllResourcesMethod = nameof(ResourceManager.ReleaseAllResources);

    private static readonly CodeFieldReferenceExpression ResourceAssembliesField =
        new(null, ResourceAssembliesFieldName);

    private static readonly CodeFieldReferenceExpression ResourceLockField =
        new(null, ResourceLockFieldName);

    public static void CreateAndWrite(string folder, string ns)
    {
        using var provider = new CSharpCodeProvider();

        var unit = new CodeCompileUnit
        {
            Namespaces =
            {
                new CodeNamespace(ns)
                {
                    Imports =
                    {
                        new CodeNamespaceImport(nameof(System))
                    },
                    Types =
                    {
                        new CodeTypeDeclaration(ClassName)
                        {
                            BaseTypes =
                            {
                                new CodeTypeReference(typeof(ResourceManager))
                            },
                            Members =
                            {
                                new CodeMemberField(
                                    typeof(ResourceAssembliesFieldType),
                                    ResourceAssembliesFieldName
                                )
                                {
                                    Attributes = MemberAttributes.Private | MemberAttributes.Final
                                },
                                new CodeMemberField(
                                    typeof(object),
                                    ResourceLockFieldName
                                )
                                {
                                    Attributes = MemberAttributes.Private | MemberAttributes.Final,
                                    InitExpression = new CodeObjectCreateExpression(typeof(object))
                                },
                                GetConstructor(),
                                GetInternalGetResourceSetMethod(),
                                GetFallbackCulturesMethod(),
                                GetReleaseAllResourcesMethod()
                            }
                        }
                    }
                }
            }
        };

        using var writer = new StreamWriter(Path.Combine(folder, $"{ClassName}.Designer.cs"));

        provider.GenerateCodeFromCompileUnit(
            unit,
            writer,
            Program.GeneratorOptions
        );
    }

    private static CodeConstructor GetConstructor()
    {
        const string baseNameArgName = "baseName";
        const string assemblyArgName = "assembly";
        var baseNameArg = new CodeArgumentReferenceExpression(baseNameArgName);
        var assemblyArg = new CodeArgumentReferenceExpression(assemblyArgName);

        const string prefixVarName = "prefix";
        const string invariantNameVarName = "invariantName";
        var prefixVar = new CodeVariableReferenceExpression(prefixVarName);
        var invariantNameVar = new CodeVariableReferenceExpression(invariantNameVarName);

        return new CodeConstructor
        {
            Attributes = MemberAttributes.Public,
            Parameters =
            {
                new CodeParameterDeclarationExpression(typeof(string), baseNameArgName),
                new CodeParameterDeclarationExpression(typeof(Assembly), assemblyArgName)
            },
            BaseConstructorArgs =
            {
                baseNameArg,
                assemblyArg
            },
            Statements =
            {
                new CodeVariableDeclarationStatement(
                    typeof(string),
                    prefixVarName,
                    new CodeBinaryOperatorExpression(
                        baseNameArg,
                        CodeBinaryOperatorType.Add,
                        new CodePrimitiveExpression('.')
                    )
                ),
                new CodeVariableDeclarationStatement(
                    typeof(string),
                    invariantNameVarName,
                    new CodeBinaryOperatorExpression(
                        baseNameArg,
                        CodeBinaryOperatorType.Add,
                        new CodePrimitiveExpression(Program.ResourceSuffix)
                    )
                ),
                new CodeAssignStatement(
                    ResourceAssembliesField,
                    new CodeObjectCreateExpression(
                        typeof(ResourceAssembliesFieldType),
                        new CodeMethodInvokeExpression(
                            new CodeMethodInvokeExpression(
                                new CodeMethodInvokeExpression(
                                    new CodeMethodInvokeExpression(
                                        new CodeMethodInvokeExpression(
                                            assemblyArg,
                                            nameof(Assembly.GetManifestResourceNames)
                                        ),
                                        nameof(Enumerable.Where),
                                        GetResourceNameCheckLambda()
                                    ),
                                    nameof(Enumerable.Select),
                                    GetResourceLoadLambda()
                                ),
                                nameof(Enumerable.Where),
                                GetTupleCheckLambda()
                            ),
                            nameof(Enumerable.Select),
                            GetTupleToPairLambda()
                        )
                    )
                )
            }
        };

        CodeLambdaExpression GetResourceNameCheckLambda()
        {
            const string nameArgName = "name";
            var nameArg = new CodeArgumentReferenceExpression(nameArgName);

            var ordinalStringComparison = new CodeFieldReferenceExpression(
                new CodeTypeReferenceExpression(typeof(StringComparison)),
                nameof(StringComparison.Ordinal)
            );

            return new CodeLambdaExpression(
                new CodeBinaryOperatorExpression(
                    new CodeMethodInvokeExpression(
                        nameArg,
                        nameof(string.StartsWith),
                        prefixVar,
                        ordinalStringComparison
                    ),
                    CodeBinaryOperatorType.BooleanAnd,
                    new CodeMethodInvokeExpression(
                        nameArg,
                        nameof(string.EndsWith),
                        new CodePrimitiveExpression(Program.ResourceSuffix),
                        ordinalStringComparison
                    )
                )
            )
            {
                Parameters =
                {
                    new CodeParameterDeclarationExpression(typeof(string), nameArgName)
                }
            };
        }

        CodeLambdaExpression GetResourceLoadLambda()
        {
            const string nameArgName = "name";
            var nameArg = new CodeArgumentReferenceExpression(nameArgName);

            const string cultureVarName = "culture";
            var cultureVar = new CodeVariableReferenceExpression(cultureVarName);

            var tupleCreateMethod = new CodeMethodReferenceExpression(
                new CodeTypeReferenceExpression(typeof(ValueTuple)),
                nameof(ValueTuple.Create),
                new CodeTypeReference(typeof(CultureInfo)),
                new CodeTypeReference(typeof(Stream))
            );

            return new CodeLambdaExpression(
                new CodeVariableDeclarationStatement(typeof(CultureInfo), cultureVarName),
                new CodeTryCatchFinallyStatement(
                    [
                        new CodeAssignStatement(
                            cultureVar,
                            new CodeTernaryExpression(
                                new CodeBinaryOperatorExpression(
                                    nameArg,
                                    CodeBinaryOperatorType.ValueEquality,
                                    invariantNameVar
                                ),
                                new CodePropertyReferenceExpression(
                                    new CodeTypeReferenceExpression(typeof(CultureInfo)),
                                    nameof(CultureInfo.InvariantCulture)
                                ),
                                new CodeMethodInvokeExpression(
                                    new CodeTypeReferenceExpression(typeof(CultureInfo)),
                                    nameof(CultureInfo.GetCultureInfo),
                                    new CodeMethodInvokeExpression(
                                        nameArg,
                                        nameof(string.Substring),
                                        new CodePropertyReferenceExpression(
                                            prefixVar,
                                            nameof(string.Length)
                                        ),
                                        new CodeBinaryOperatorExpression(
                                            new CodePropertyReferenceExpression(
                                                nameArg,
                                                nameof(string.Length)
                                            ),
                                            CodeBinaryOperatorType.Subtract,
                                            new CodePrimitiveExpression(Program.ResourceSuffix.Length + 2)
                                        )
                                    )
                                )
                            )
                        )
                    ],
                    [
                        new CodeCatchClause
                        {
                            CatchExceptionType = new CodeTypeReference(typeof(CultureNotFoundException)),
                            Statements =
                            {
                                new CodeMethodReturnStatement(
                                    new CodeMethodInvokeExpression(
                                        tupleCreateMethod,
                                        new CodePrimitiveExpression(null),
                                        new CodePrimitiveExpression(null)
                                    )
                                )
                            }
                        }
                    ]
                ),
                new CodeMethodReturnStatement(
                    new CodeMethodInvokeExpression(
                        tupleCreateMethod,
                        cultureVar,
                        new CodeMethodInvokeExpression(
                            assemblyArg,
                            nameof(Assembly.GetManifestResourceStream),
                            nameArg
                        )
                    )
                )
            )
            {
                Parameters =
                {
                    new CodeParameterDeclarationExpression(typeof(string), nameArgName)
                }
            };
        }

        static CodeLambdaExpression GetTupleCheckLambda()
        {
            const string tupleArgName = "tuple";
            var tupleArg = new CodeArgumentReferenceExpression(tupleArgName);

            return new CodeLambdaExpression(
                new CodeNegateExpression(
                    new CodeBinaryOperatorExpression(
                        new CodeBinaryOperatorExpression(
                            new CodeFieldReferenceExpression(
                                tupleArg,
                                nameof(ValueTuple<,>.Item1)
                            ),
                            CodeBinaryOperatorType.ValueEquality,
                            new CodePrimitiveExpression(null)
                        ),
                        CodeBinaryOperatorType.BooleanOr,
                        new CodeBinaryOperatorExpression(
                            new CodeFieldReferenceExpression(
                                tupleArg,
                                nameof(ValueTuple<,>.Item2)
                            ),
                            CodeBinaryOperatorType.ValueEquality,
                            new CodePrimitiveExpression(null)
                        )
                    )
                )
            )
            {
                IsStatic = true,
                Parameters =
                {
                    new CodeParameterDeclarationExpression(typeof(ValueTuple<CultureInfo, Stream>), tupleArgName)
                }
            };
        }

        static CodeLambdaExpression GetTupleToPairLambda()
        {
            const string tupleArgName = "tuple";
            var tupleArg = new CodeArgumentReferenceExpression(tupleArgName);

            return new CodeLambdaExpression(
                new CodeObjectCreateExpression(
                    typeof(KeyValuePair<CultureInfo, ResourceSet>),
                    new CodeFieldReferenceExpression(
                        tupleArg,
                        nameof(ValueTuple<,>.Item1)
                    ),
                    new CodeObjectCreateExpression(
                        typeof(ResourceSet),
                        new CodeFieldReferenceExpression(
                            tupleArg,
                            nameof(ValueTuple<,>.Item2)
                        )
                    )
                )
            )
            {
                IsStatic = true,
                Parameters =
                {
                    new CodeParameterDeclarationExpression(typeof(ValueTuple<CultureInfo, Stream>), tupleArgName)
                }
            };
        }
    }

    private static CodeMemberMethod GetInternalGetResourceSetMethod()
    {
        const string cultureArgName = "culture";
        const string createIfNotExistsArgName = "createIfNotExists";
        const string tryParentsArgName = "tryParents";
        var cultureArg = new CodeArgumentReferenceExpression(cultureArgName);
        var createIfNotExistsArg = new CodeArgumentReferenceExpression(createIfNotExistsArgName);
        var tryParentsArg = new CodeArgumentReferenceExpression(tryParentsArgName);

        const string culturesVarName = "cultures";
        var culturesVar = new CodeVariableReferenceExpression(culturesVarName);

        const string fallbackIterationVarName = "fallback";
        const string resourceSetIterationVarName = "resourceSet";
        var fallbackIterationVar = new CodeVariableReferenceExpression(fallbackIterationVarName);
        var resourceSetIterationVar = new CodeVariableReferenceExpression(resourceSetIterationVarName);

        return new CodeMemberMethod
        {
            Attributes = MemberAttributes.Family | MemberAttributes.Override,
            ReturnType = new CodeTypeReference(typeof(ResourceSet)),
            Name = InternalGetResourceSetMethod,
            Parameters =
            {
                new CodeParameterDeclarationExpression(typeof(CultureInfo), cultureArgName),
                new CodeParameterDeclarationExpression(typeof(bool), createIfNotExistsArgName),
                new CodeParameterDeclarationExpression(typeof(bool), tryParentsArgName)
            },
            Statements =
            {
                new CodeVariableDeclarationStatement(
                    typeof(IEnumerable<CultureInfo>),
                    culturesVarName,
                    new CodeTernaryExpression(
                        tryParentsArg,
                        new CodeMethodInvokeExpression(
                            null,
                            FallbackCulturesMethod,
                            cultureArg
                        ),
                        new CodeArrayCreateExpression(typeof(CultureInfo), [cultureArg])
                    )
                ),
                new CodeCustomBlockStatement
                {
                    Name = "lock",
                    Expression = ResourceLockField,
                    Statements =
                    {
                        new CodeEnumerableIterationStatement(
                            (new CodeTypeReference(typeof(CultureInfo)), fallbackIterationVarName),
                            culturesVar
                        )
                        {
                            Statements =
                            {
                                new CodeVariableDeclarationStatement(
                                    typeof(ResourceSet),
                                    resourceSetIterationVarName
                                ),
                                new CodeConditionStatement(
                                    new CodeMethodInvokeExpression(
                                        ResourceAssembliesField,
                                        nameof(ConcurrentDictionary<,>.TryGetValue),
                                        fallbackIterationVar,
                                        new CodeDirectionExpression(
                                            FieldDirection.Out,
                                            resourceSetIterationVar
                                        )
                                    ),
                                    new CodeMethodReturnStatement(resourceSetIterationVar)
                                )
                            }
                        }
                    }
                },
                new CodeMethodReturnStatement(
                    new CodeMethodInvokeExpression(
                        new CodeBaseReferenceExpression(),
                        InternalGetResourceSetMethod,
                        cultureArg,
                        createIfNotExistsArg,
                        tryParentsArg
                    )
                )
            }
        };
    }

    private static CodeMemberMethod GetFallbackCulturesMethod()
    {
        const string startFromArgName = "startFrom";
        var startFromArg = new CodeArgumentReferenceExpression(startFromArgName);

        const string currentCultureVarName = "currentCulture";
        var currentCultureVar = new CodeVariableReferenceExpression(currentCultureVarName);

        return new CodeMemberMethod
        {
            Attributes = MemberAttributes.Private | MemberAttributes.Static,
            ReturnType = new CodeTypeReference(typeof(IEnumerable<CultureInfo>)),
            Name = FallbackCulturesMethod,
            Parameters =
            {
                new CodeParameterDeclarationExpression(typeof(CultureInfo), startFromArgName)
            },
            Statements =
            {
                new CodeIterationStatement(
                    new CodeVariableDeclarationStatement(
                        typeof(CultureInfo),
                        currentCultureVarName,
                        startFromArg
                    ),
                    new CodeNegateExpression(
                        new CodeMethodInvokeExpression(
                            currentCultureVar,
                            nameof(Equals),
                            new CodePropertyReferenceExpression(
                                new CodeTypeReferenceExpression(typeof(CultureInfo)),
                                nameof(CultureInfo.InvariantCulture)
                            )
                        )
                    ),
                    new CodeAssignStatement(
                        currentCultureVar,
                        new CodePropertyReferenceExpression(
                            currentCultureVar,
                            nameof(CultureInfo.Parent)
                        )
                    )
                )
                {
                    Statements =
                    {
                        new CodeMethodYieldReturnStatement(currentCultureVar)
                    }
                },
                new CodeMethodYieldReturnStatement(
                    new CodePropertyReferenceExpression(
                        new CodeTypeReferenceExpression(typeof(CultureInfo)),
                        nameof(CultureInfo.InvariantCulture)
                    )
                )
            }
        };
    }

    private static CodeMemberMethod GetReleaseAllResourcesMethod()
    {
        const string resourceSetIterationVarName = "resourceSet";
        var resourceSetIterationVar = new CodeVariableReferenceExpression(resourceSetIterationVarName);

        return new CodeMemberMethod
        {
            Attributes = MemberAttributes.Public | MemberAttributes.Override,
            ReturnType = new CodeTypeReference(typeof(void)),
            Name = ReleaseAllResourcesMethod,
            Statements =
            {
                new CodeCustomBlockStatement
                {
                    Name = "lock",
                    Expression = ResourceLockField,
                    Statements =
                    {
                        new CodeEnumerableIterationStatement(
                            (new CodeTypeReference(typeof(ResourceSet)), resourceSetIterationVarName),
                            new CodePropertyReferenceExpression(
                                ResourceAssembliesField,
                                nameof(ConcurrentDictionary<,>.Values)
                            )
                        )
                        {
                            Statements =
                            {
                                new CodeMethodInvokeExpression(
                                    resourceSetIterationVar,
                                    nameof(ResourceSet.Dispose)
                                )
                            }
                        },
                        new CodeMethodInvokeExpression(
                            ResourceAssembliesField,
                            nameof(ConcurrentDictionary<,>.Clear)
                        )
                    }
                },
                new CodeMethodInvokeExpression(
                    new CodeBaseReferenceExpression(),
                    ReleaseAllResourcesMethod
                )
            }
        };
    }
}
