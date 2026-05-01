namespace Mud.HttpUtils.Generator.Tests;

public class FormContentGeneratorTests
{
    private static GeneratorDriver RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = BasicReferenceAssemblies.GetReferences();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generatorType = TestHelper.GetType("Mud.HttpUtils.FormContentGenerator");
        var generator = (IIncrementalGenerator)Activator.CreateInstance(generatorType)!;
        var driver = CSharpGeneratorDriver.Create(generator);
        return driver.RunGenerators(compilation);
    }

    #region Basic Generation Tests

    [Fact]
    public void FormContentGenerator_WithNoFormContentClass_GeneratesNothing()
    {
        var source = @"
public class MyClass
{
    public string Name { get; set; }
}";

        var driver = RunGenerator(source);
        var results = driver.GetRunResult();

        results.GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public void FormContentGenerator_WithFormContentClassNoFilePath_GeneratesDiagnostic_FORM002()
    {
        var source = @"
using System.Text.Json.Serialization;
using Mud.HttpUtils.Attributes;

[FormContent]
public class UploadRequest
{
    [JsonPropertyName(""name"")]
    public string Name { get; set; }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().Contain(d => d.Id == "FORM002");
    }

    [Fact]
    public void FormContentGenerator_WithMultipleFilePathAttributes_GeneratesDiagnostic_FORM003()
    {
        var source = @"
using System.Text.Json.Serialization;
using Mud.HttpUtils.Attributes;

[FormContent]
public class UploadRequest
{
    [JsonPropertyName(""file1"")]
    [FilePath]
    public string File1 { get; set; }

    [JsonPropertyName(""file2"")]
    [FilePath]
    public string File2 { get; set; }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().Contain(d => d.Id == "FORM003");
    }

    #endregion
}
