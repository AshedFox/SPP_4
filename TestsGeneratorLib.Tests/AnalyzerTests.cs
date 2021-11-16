using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace TestsGeneratorLib.Tests
{
    public class AnalyzerTests
    {
        private readonly string _writePath = "./tests";
        private readonly string _testClass1Path = "../../../TestClass1.cs";
        private readonly string _testClass2Path = "../../../TestClass2.cs";

        [Fact]
        public void Analyze_GeneratingTests_UsingsCorrect()
        {
            var config = new AnalyzerConfig(3,3,3,
                new List<string>() 
                { 
                    _testClass1Path,
                    _testClass2Path
                }, _writePath);
            var analyzer = new Analyzer(config);
            var expected = new List<string>()
            {
                "Xunit",
                "Moq",
                "TestsGeneratorLib.Tests",
                "System.Threading.Tasks",
                "System"
            };

            analyzer.Analyze().Wait();
            var actual = CSharpSyntaxTree
                .ParseText(File.ReadAllText(Path.Combine(_writePath, "TestClass1Tests.cs")))
                .GetCompilationUnitRoot().Usings.Select(syntax => syntax.Name.ToString());

            Assert.Superset(expected.ToHashSet(), actual.ToHashSet());
        }        
        
        [Fact]
        public void Analyze_GeneratingTests_NamespaceCorrect()
        {
            var config = new AnalyzerConfig(3,3,3,
                new List<string>() 
                { 
                    _testClass1Path 
                }, _writePath);
            var analyzer = new Analyzer(config);
            var expected = "TestsGeneratorLib.Tests.Tests";

            analyzer.Analyze().Wait();
            var actual = CSharpSyntaxTree
                .ParseText(File.ReadAllText(Path.Combine(_writePath, "TestClass1Tests.cs")))
                .GetCompilationUnitRoot().DescendantNodes().OfType<NamespaceDeclarationSyntax>().First().Name.ToString();
            
            Assert.Equal(expected, actual);
        }        
        
        [Fact]
        public void Analyze_GeneratingTests_ClassCorrect()
        {
            var config = new AnalyzerConfig(3,3,3,
                new List<string>() 
                { 
                    _testClass1Path 
                }, _writePath);
            var analyzer = new Analyzer(config);
            var expected = "TestClass1Tests";

            analyzer.Analyze().Wait();
            var actual = CSharpSyntaxTree
                .ParseText(File.ReadAllText(Path.Combine(_writePath, "TestClass1Tests.cs")))
                .GetCompilationUnitRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();
            
            Assert.Single(actual);
            
            Assert.Equal(expected, actual.First().Identifier.Text);
        }
        
        [Fact]
        public void Analyze_GeneratingTests_MethodsNamesCorrect()
        {
            var config = new AnalyzerConfig(3,3,3,
                new List<string>() 
                { 
                    _testClass1Path 
                }, _writePath);
            var analyzer = new Analyzer(config);
            var expected = new List<string>()
            {
                "TestMethod1Test0",
                "TestMethod2Test0",
                "TestMethod3Test0",
                "TestMethod4Test0",
            };

            analyzer.Analyze().Wait();
            var actual = CSharpSyntaxTree
                .ParseText(File.ReadAllText(Path.Combine(_writePath, "TestClass1Tests.cs")))
                .GetCompilationUnitRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Select(syntax => syntax.Identifier.Text);
            
            Assert.Superset(expected.ToHashSet(), actual.ToHashSet());
        }
        
        [Fact]
        public void Analyze_GeneratingTests_DepInjVarsCorrect()
        {
            var config = new AnalyzerConfig(3,3,3,
                new List<string>() 
                { 
                    _testClass2Path 
                }, _writePath);
            var analyzer = new Analyzer(config);
            var expected = new List<string>()
            {
                "Mock<IEnumerable> _param1",
                "Mock<ICollection> _param2",
                "TestClass2 _testClass2",
            };

            analyzer.Analyze().Wait();
            var vars = CSharpSyntaxTree
                .ParseText(File.ReadAllText(Path.Combine(_writePath, "TestClass2Tests.cs")))
                .GetCompilationUnitRoot().DescendantNodes().OfType<VariableDeclarationSyntax>();
            
            var actual = string.Join("\n", vars);

            foreach (var expectedStr in expected)
            {
                Assert.Contains(expectedStr, actual);
            }
        }
        
        [Fact]
        public void Analyze_GeneratingTests_DepInjCtorCorrect()
        {
            var config = new AnalyzerConfig(3,3,3,
                new List<string>() 
                { 
                    _testClass2Path 
                }, _writePath);
            var analyzer = new Analyzer(config);
            var expected = new List<string>()
            {
                "_param1 = new Mock<IEnumerable>()",
                "_param2 = new Mock<ICollection>()",
                "_testClass2 = new TestClass2(_param1.Object, _param2.Object)",
            };

            analyzer.Analyze().Wait();
            var constructors = CSharpSyntaxTree
                .ParseText(File.ReadAllText(Path.Combine(_writePath, "TestClass2Tests.cs")))
                .GetCompilationUnitRoot().DescendantNodes().OfType<ConstructorDeclarationSyntax>();
            
            var actual = constructors.OrderByDescending(syntax => syntax.ParameterList.Parameters.Count)
                .First().Body!.ToString();

            foreach (var expectedStr in expected)
            {
                Assert.Contains(expectedStr, actual);
            }
        }
        
        [Fact]
        public void Analyze_GeneratingTests_ArrangeStatementsCorrect()
        {
            var config = new AnalyzerConfig(3,3,3,
                new List<string>() 
                { 
                    _testClass1Path 
                }, _writePath);
            var analyzer = new Analyzer(config);
            var expected = new List<string>()
            {
                "Guid id = default(Guid)",
                "object obj = default(object)"
            };

            analyzer.Analyze().Wait();
            var actual = CSharpSyntaxTree
                .ParseText(File.ReadAllText(Path.Combine(_writePath, "TestClass1Tests.cs")))
                .GetCompilationUnitRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .First(syntax => syntax.Identifier.Text == "TestMethod2Test0").Body!.ToString();

            foreach (var expectedStr in expected)
            {
                Assert.Contains(expectedStr, actual);
            }
        }
        
        [Fact]
        public void Analyze_GeneratingTests_ActStatementsCorrect()
        {
            var config = new AnalyzerConfig(3,3,3,
                new List<string>() 
                { 
                    _testClass1Path 
                }, _writePath);
            var analyzer = new Analyzer(config);
            var expected = new List<string>()
            {
                "Uri actual = await _testClass1.TestMethod4(uri)"
            };

            analyzer.Analyze().Wait();
            var actual = CSharpSyntaxTree
                .ParseText(File.ReadAllText(Path.Combine(_writePath, "TestClass1Tests.cs")))
                .GetCompilationUnitRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .First(syntax => syntax.Identifier.Text == "TestMethod4Test0").Body!.ToString();

            foreach (var expectedStr in expected)
            {
                Assert.Contains(expectedStr, actual);
            }
        }
        
        [Fact]
        public void Analyze_GeneratingTests_AssertStatementsCorrect()
        {
            var config = new AnalyzerConfig(3,3,3,
                new List<string>() 
                { 
                    _testClass1Path 
                }, _writePath);
            var analyzer = new Analyzer(config);
            var expected = new List<string>()
            {
                "Assert.Equal(expected, actual)",
                "Uri expected = default(Uri)"
            };

            analyzer.Analyze().Wait();
            var actual = CSharpSyntaxTree
                .ParseText(File.ReadAllText(Path.Combine(_writePath, "TestClass1Tests.cs")))
                .GetCompilationUnitRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .First(syntax => syntax.Identifier.Text == "TestMethod4Test0").Body!.ToString();

            foreach (var expectedStr in expected)
            {
                Assert.Contains(expectedStr, actual);
            }
        }
    }
}