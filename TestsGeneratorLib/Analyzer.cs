using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TestsGeneratorLib
{
    using static SyntaxFactory;
    
    public class Analyzer
    {
        private readonly AnalyzerConfig _config;

        public Analyzer(AnalyzerConfig config)
        {
            _config = config;

            if (!Directory.Exists(config.SavePath))
            {
                Directory.CreateDirectory(config.SavePath);
            }
        }
        
        public Task Analyze()
        {
            var readFile = new TransformBlock<string, string>
            (
                async path => await ReadFileAsync(path),
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = _config.MaxFilesReadingParallel
                }
            );
            var generateTestsByFile = new TransformManyBlock<string, string>
            (
                async data => await GenerateTestClasses(data),
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = _config.MaxTestClassesGeneratingParallel
                }
            );
            var writeFile = new ActionBlock<string>
            (
                async data => await WriteFileAsync(data),
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = _config.MaxFilesWritingParallel
                }
            );

            readFile.LinkTo(generateTestsByFile, new DataflowLinkOptions { PropagateCompletion = true });
            generateTestsByFile.LinkTo(writeFile, new DataflowLinkOptions { PropagateCompletion = true });

            foreach (var path in _config.FilesPaths)
            {
                readFile.Post(path);
            }
            
            readFile.Complete();

            return writeFile.Completion;
        }

        private async Task<string> ReadFileAsync(string path) => await File.ReadAllTextAsync(path);

        private async Task WriteFileAsync(string data)
        {
            var tree = CSharpSyntaxTree.ParseText(data);
            var fileName = (await tree.GetRootAsync())
                .DescendantNodes().OfType<ClassDeclarationSyntax>()
                .First().Identifier.Text;
            var filePath = Path.Combine(_config.SavePath, $"{fileName}.cs");
            
            await File.WriteAllTextAsync(filePath, data);
        }

        private async Task<string[]> GenerateTestClasses(string fileText)
        {
            var root = CSharpSyntaxTree.ParseText(fileText).GetCompilationUnitRoot();
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
            var result = new List<string>();
            
            foreach (var @class in classes)
            {
                result.Add(await GenerateTestClass(@class, root));
            }

            return result.ToArray();
        }
        
        private IEnumerable<UsingDirectiveSyntax> GenerateTestUsings(CompilationUnitSyntax root)
        {
            var result = new Dictionary<string, UsingDirectiveSyntax>();
            var defaultUsings = GenerateDefaultTestUsings();

            var @namespace = root.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault();

            if (@namespace is not null)
            {
                var selfUsing = UsingDirective(
                    IdentifierName(@namespace.Name.ToString())
                );
                
                result.TryAdd(selfUsing.Name.ToString(), selfUsing);
            }
            
            foreach (var defaultUsing in defaultUsings)
            {
                result.TryAdd(defaultUsing.Name.ToString(), defaultUsing);
            }
            foreach (var rootUsing in root.Usings)
            {
                result.TryAdd(rootUsing.Name.ToString(), rootUsing);
            }
            
            return result.Values;
        }
        
        private IEnumerable<UsingDirectiveSyntax> GenerateDefaultTestUsings()
        {
            return List
            (
                new[]
                {
                    UsingDirective(IdentifierName("System")),
                    UsingDirective
                    (
                        QualifiedName
                        (
                            IdentifierName("System"),
                            IdentifierName("Collections")
                        )
                    ),
                    UsingDirective
                    (
                        QualifiedName
                        (
                            QualifiedName
                            (
                                IdentifierName("System"),
                                IdentifierName("Collections")
                            ),
                            IdentifierName("Generic")
                        )
                    ),
                    UsingDirective(IdentifierName("Xunit")),
                    UsingDirective(IdentifierName("Moq"))
                }
            );
        }

        private (IEnumerable<FieldDeclarationSyntax>, ConstructorDeclarationSyntax) GenerateDependencyInjection(
            ClassDeclarationSyntax classDeclaration)
        {
            var constructors = 
                classDeclaration.Members.OfType<ConstructorDeclarationSyntax>().ToList();
            var variables = new List<FieldDeclarationSyntax>();
            var expressions = new List<ExpressionStatementSyntax>();
            var arguments = new List<ArgumentSyntax>();

            var biggestConstructor = constructors.FirstOrDefault();
            
            if (biggestConstructor is not null) 
            {
                foreach (var constructor in constructors)
                {
                    if (constructor.ParameterList.Parameters.Count > biggestConstructor.ParameterList.Parameters.Count)
                    {
                        biggestConstructor = constructor;
                    }
                }

                var parameters = biggestConstructor.ParameterList.Parameters;

                foreach (var parameter in parameters)
                {
                    ArgumentSyntax argument;
                    var isInterface = Regex.Match(parameter.Type?.ToString() ?? string.Empty, @"I[A-Z]{1}\w*").Success;
                    if (!isInterface)
                    {
                        argument = Argument(
                            IdentifierName("_" + parameter.Identifier.Text)
                        );
                    }
                    else
                    {
                        argument = Argument(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName("_" + parameter.Identifier.Text),
                                IdentifierName("Object")
                            )
                        );
                    }

                    arguments.Add(argument);
                    
                    var variable = FieldDeclaration
                    (
                        VariableDeclaration(
                            isInterface ?
                                GenericName(Identifier("Mock"))
                                .WithTypeArgumentList(
                                    TypeArgumentList(
                                        SingletonSeparatedList<TypeSyntax>(
                                            IdentifierName(parameter.Type?.ToString() ?? string.Empty)
                                        )
                                    )
                                ) :
                                IdentifierName(parameter.Type?.ToString() ?? string.Empty)
                            )
                        .WithVariables(
                            SingletonSeparatedList(
                                VariableDeclarator(Identifier("_" + parameter.Identifier.Text))
                            )
                        )
                    )
                    .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)));

                    variables.Add(variable);

                    var expression = ExpressionStatement
                    (
                        AssignmentExpression
                        (
                            SyntaxKind.SimpleAssignmentExpression,
                            IdentifierName("_" + parameter.Identifier.Text),
                            ObjectCreationExpression
                                (
                                    isInterface ?
                                        GenericName(Identifier("Mock"))
                                        .WithTypeArgumentList
                                        (
                                            TypeArgumentList
                                            (
                                                SingletonSeparatedList<TypeSyntax>
                                                (
                                                    IdentifierName(parameter.Type?.ToString() ?? string.Empty)
                                                )
                                            )
                                        ) :
                                        IdentifierName(parameter.Type?.ToString() ?? string.Empty)

                                )
                                .WithArgumentList(ArgumentList())
                        )
                    );

                    expressions.Add(expression);
                }
            }

            var classVariable = FieldDeclaration
                (
                    VariableDeclaration(IdentifierName(classDeclaration.Identifier.Text))
                    .WithVariables(
                        SingletonSeparatedList(
                            VariableDeclarator(
                                Identifier(                        
                                    "_" + char.ToLowerInvariant(classDeclaration.Identifier.Text[0]) + 
                                    classDeclaration.Identifier.Text.Substring(1)
                                )
                            )
                        )
                    )
                )
                .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)));
            
            variables.Add(classVariable);
            
            var classAssignment = ExpressionStatement
            (
                AssignmentExpression
                (
                    SyntaxKind.SimpleAssignmentExpression,
                    IdentifierName(
                        "_" + char.ToLowerInvariant(classDeclaration.Identifier.Text[0]) + 
                        classDeclaration.Identifier.Text.Substring(1)
                    ),
                    ObjectCreationExpression(IdentifierName(classDeclaration.Identifier.Text))
                        .WithArgumentList(ArgumentList(SeparatedList(arguments)))
                )
            );
            
            expressions.Add(classAssignment);
            
            var constructorDeclaration = ConstructorDeclaration
                (
                    Identifier(classDeclaration.Identifier.Text + "Tests")
                )
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                .WithBody(Block(expressions));

            return (variables, constructorDeclaration);
        }

        private IEnumerable<MemberDeclarationSyntax> GenerateTestMethods(ClassDeclarationSyntax classDeclaration)
        {
            var result = new List<MemberDeclarationSyntax>();

            var methodsDeclarations =
                classDeclaration.Members.OfType<MethodDeclarationSyntax>()
                    .Where(syntax => syntax.Modifiers.Any(SyntaxKind.PublicKeyword));

            var uniqueMethodsNames = new List<string>();
            
            foreach (var methodDeclaration in methodsDeclarations)
            {
                var body = new List<StatementSyntax>();

                var baseUniqueName = methodDeclaration.Identifier.Text + "Test";
                string uniqueName;
                var i = 0;
                do
                {
                    uniqueName = baseUniqueName + i.ToString();
                    i++;
                } while (uniqueMethodsNames.Contains(uniqueName));
                uniqueMethodsNames.Add(uniqueName);

                var isAsync = methodDeclaration.Modifiers.Any(SyntaxKind.AsyncKeyword);
                var isWithReturn = !(methodDeclaration.ReturnType.ToString() == "void" || 
                                     isAsync && (methodDeclaration.ReturnType.ToString() == "void" ||
                                                 methodDeclaration.ReturnType.ToString() == "Task" &&
                                                 methodDeclaration.ReturnType is not GenericNameSyntax));

                body.AddRange(GenerateArrangeStatements(methodDeclaration));

                body.Add(GenerateActStatement(classDeclaration, methodDeclaration, isWithReturn, isAsync));

                body.AddRange(GenerateAssertStatements(methodDeclaration, isWithReturn, isAsync));
                
                var method = 
                    MethodDeclaration(
                        isAsync ? IdentifierName("Task") : PredefinedType(Token(SyntaxKind.VoidKeyword)),
                        Identifier(uniqueName)
                    )
                    .WithAttributeLists(
                        SingletonList
                        (
                            AttributeList(SingletonSeparatedList(Attribute(IdentifierName("Fact"))))
                        )
                    )
                    .WithModifiers(
                        isAsync ?
                            TokenList(
                                Token(SyntaxKind.PublicKeyword),
                                Token(SyntaxKind.AsyncKeyword)
                            ) :
                            TokenList(
                                Token(SyntaxKind.PublicKeyword)
                            )
                    )
                    .WithBody(Block(body));
                
                result.Add(method);
            }

            return result;
        }

        private List<StatementSyntax> GenerateArrangeStatements(MethodDeclarationSyntax methodDeclaration)
        {
            var parameters = methodDeclaration.ParameterList.Parameters;
            var result = new List<StatementSyntax>();

            foreach (var parameter in parameters)
            {
                var assignment =
                    LocalDeclarationStatement(VariableDeclaration(IdentifierName(parameter.Type?.ToString() ?? string.Empty))
                        .WithVariables
                        (
                            SingletonSeparatedList
                            (
                            VariableDeclarator(Identifier(parameter.Identifier.Text))
                                .WithInitializer
                                (
                                    EqualsValueClause
                                    (
                                        DefaultExpression(IdentifierName(parameter.Type?.ToString() ?? string.Empty))
                                    )
                                )
                            )
                        )
                    );
                
                result.Add(assignment);
            }

            return result;
        }


        private StatementSyntax GenerateActStatement(ClassDeclarationSyntax classDeclaration,
            MethodDeclarationSyntax methodDeclaration, bool isWithReturn, bool isAsync)
        {
            var parameters = methodDeclaration.ParameterList.Parameters;

            var invocationExpression =
                InvocationExpression(
                    MemberAccessExpression
                    (
                        SyntaxKind.SimpleMemberAccessExpression,
                        IdentifierName(
                            "_" + char.ToLowerInvariant(classDeclaration.Identifier.Text[0]) + 
                            classDeclaration.Identifier.Text.Substring(1)
                        ),
                        IdentifierName(methodDeclaration.Identifier.Text)
                    )
                )
                .WithArgumentList
                (
                    ArgumentList
                    (
                        SeparatedList
                        (
                            parameters.Select(syntax => Argument(IdentifierName(syntax.Identifier.Text)))
                        )
                    )
                );

            if (!isWithReturn)
            {
                return ExpressionStatement(invocationExpression);
            }
            else
            {
                return
                    LocalDeclarationStatement(
                        VariableDeclaration(
                                IdentifierName(
                                    isAsync
                                        ? ((GenericNameSyntax)methodDeclaration.ReturnType).TypeArgumentList
                                        .Arguments[0].ToString()
                                        : methodDeclaration.ReturnType.ToString()
                                )
                            )
                            .WithVariables
                            (
                                SingletonSeparatedList
                                (
                                    VariableDeclarator(Identifier("actual"))
                                        .WithInitializer
                                        (
                                            EqualsValueClause
                                            (
                                                isAsync ? AwaitExpression(invocationExpression) : invocationExpression
                                            )
                                        )
                                )
                            )
                    );
            }
        }

        private List<StatementSyntax> GenerateAssertStatements(MethodDeclarationSyntax methodDeclaration, 
            bool isWithReturn, bool isAsync)
        {
            if (isWithReturn)
            {
                return GenerateAdditionalAssertStatements(methodDeclaration, isAsync);
            }
            else
            {
                return new List<StatementSyntax> { GenerateDefaultAssertStatements() };
            }
        }
        
        private StatementSyntax GenerateDefaultAssertStatements()
        {
            return
                ExpressionStatement
                (
                    InvocationExpression
                    (
                        MemberAccessExpression
                        (
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName("Assert"),
                            IdentifierName("True")
                        )
                    )
                    .WithArgumentList
                    (
                        ArgumentList
                        (
                            SeparatedList<ArgumentSyntax>
                            (
                                new SyntaxNodeOrToken[]
                                {
                                    Argument(LiteralExpression(SyntaxKind.FalseLiteralExpression)),
                                    Token(SyntaxKind.CommaToken),
                                    Argument
                                    (
                                        LiteralExpression
                                        (
                                            SyntaxKind.StringLiteralExpression,
                                            Literal("error")
                                        )
                                    )
                                }
                            )
                        )
                    )
                );
        }
        
        private List<StatementSyntax> GenerateAdditionalAssertStatements(MethodDeclarationSyntax methodDeclaration, 
            bool isAsync)
        {
            var expected = LocalDeclarationStatement
            (
                VariableDeclaration(
                    IdentifierName(
                        isAsync ? 
                            ((GenericNameSyntax)methodDeclaration.ReturnType).TypeArgumentList.Arguments[0].ToString() : 
                            methodDeclaration.ReturnType.ToString()
                    )
                )
                .WithVariables
                (
                    SingletonSeparatedList
                    (
                        VariableDeclarator(Identifier("expected"))
                            .WithInitializer
                            (
                                EqualsValueClause
                                (
                                    DefaultExpression(
                                        IdentifierName(
                                            isAsync ? 
                                                ((GenericNameSyntax)methodDeclaration.ReturnType).TypeArgumentList.Arguments[0].ToString() : 
                                                methodDeclaration.ReturnType.ToString()
                                        )
                                    )
                                )
                            )
                    )
                )
            );

            var assertEquality = ExpressionStatement
            (
                InvocationExpression
                    (
                        MemberAccessExpression
                        (
                            SyntaxKind.SimpleMemberAccessExpression,
                            IdentifierName("Assert"),
                            IdentifierName("Equal")
                        )
                    )
                    .WithArgumentList
                    (
                        ArgumentList
                        (
                            SeparatedList
                            (
                                new[]
                                {
                                    Argument(IdentifierName("expected")),
                                    Argument(IdentifierName("actual"))
                                }
                            )
                        )
                    )
            );
            return new List<StatementSyntax>() { expected, assertEquality };
        }

        private async Task<string> GenerateTestClass(ClassDeclarationSyntax classDeclaration,
            CompilationUnitSyntax root)
        {
            return await Task.Run(() =>
            {
                var compilationUnit = CompilationUnit();

                compilationUnit = compilationUnit.AddUsings(GenerateTestUsings(root).ToArray());

                var baseNamespace = root.DescendantNodes().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
                var @namespace = NamespaceDeclaration(
                    baseNamespace is null ? IdentifierName("Tests") : IdentifierName($"{baseNamespace.Name}.Tests")
                );

                var @class = ClassDeclaration(classDeclaration.Identifier.Text + "Tests")
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)));

                var depInj = GenerateDependencyInjection(classDeclaration);

                @class = @class.AddMembers(depInj.Item1.ToArray());
                @class = @class.AddMembers(depInj.Item2);
                @class = @class.AddMembers(GenerateTestMethods(classDeclaration).ToArray());

                compilationUnit = compilationUnit.AddMembers(@namespace.AddMembers(@class));

                return compilationUnit.NormalizeWhitespace().ToString();
            });
        }
    }
}