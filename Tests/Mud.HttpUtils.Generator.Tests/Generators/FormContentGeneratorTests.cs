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

    #region Code Generation Tests

    [Fact]
    public void FormContentGenerator_WithValidFormContent_GeneratesCode()
    {
        var source = @"
using System.Text.Json.Serialization;
using Mud.HttpUtils.Attributes;

[FormContent]
public class UploadRequest
{
    [JsonPropertyName(""user_name"")]
    public string UserName { get; set; }

    [JsonPropertyName(""file"")]
    [FilePath]
    public string FilePath { get; set; }
}";

        var driver = RunGenerator(source);
        var results = driver.GetRunResult();

        results.GeneratedTrees.Should().HaveCount(1);
        var generatedCode = results.GeneratedTrees[0].ToString();
        generatedCode.Should().Contain("GetFormDataContentAsync");
        generatedCode.Should().Contain("MultipartFormDataContent");
        generatedCode.Should().Contain("\"user_name\"");
        generatedCode.Should().Contain("GetByteArrayContentAsync");
    }

    [Fact]
    public void FormContentGenerator_WithSpecialCharsInJsonPropertyName_EscapesCorrectly()
    {
        var source = @"
using System.Text.Json.Serialization;
using Mud.HttpUtils.Attributes;

[FormContent]
public class UploadRequest
{
    [JsonPropertyName(""test\""name"")]
    public string Name { get; set; }

    [JsonPropertyName(""file"")]
    [FilePath]
    public string FilePath { get; set; }
}";

        var driver = RunGenerator(source);
        var results = driver.GetRunResult();

        results.GeneratedTrees.Should().HaveCount(1);
        var generatedCode = results.GeneratedTrees[0].ToString();
        // The double quote in the JSON property name should be escaped as \"
        generatedCode.Should().Contain("\"test\\\"name\"");
    }

    [Fact]
    public void FormContentGenerator_WithBackslashInJsonPropertyName_EscapesCorrectly()
    {
        var source = @"
using System.Text.Json.Serialization;
using Mud.HttpUtils.Attributes;

[FormContent]
public class UploadRequest
{
    [JsonPropertyName(""path\\name"")]
    public string Name { get; set; }

    [JsonPropertyName(""file"")]
    [FilePath]
    public string FilePath { get; set; }
}";

        var driver = RunGenerator(source);
        var results = driver.GetRunResult();

        results.GeneratedTrees.Should().HaveCount(1);
        var generatedCode = results.GeneratedTrees[0].ToString();
        // The backslash in the JSON property name should be escaped as \\
        generatedCode.Should().Contain("path\\\\name");
    }

    /// <summary>
    /// P1-2 补充：<c>[FormContent]</c> 类名 / 属性名为 C# 保留字时，partial 类声明位与
    /// 属性引用位必须成对补 <c>@</c> 前缀（否则生成文件报 CS1001）。
    /// </summary>
    [Fact]
    public void FormContentGenerator_WithKeywordNamedClassAndProperties_GeneratesCompilableCode()
    {
        const string source = """
            using System;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [FormContent]
                public partial class @class
                {
                    public string @event { get; set; } = "";

                    [FilePath]
                    public string @string { get; set; } = "";

                    public byte[] @default { get; set; } = Array.Empty<byte>();
                }
            }
            """;

        var generatorType = TestHelper.GetType("Mud.HttpUtils.FormContentGenerator");
        var generator = (IIncrementalGenerator)Activator.CreateInstance(generatorType)!;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            generator,
            source,
            description: "关键字命名的 [FormContent] 类/属性必须经 @ 转义且生成代码可编译");

        var generatedCode = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));
        generatedCode.Should().Contain("public partial class @class",
            "partial 类声明位必须补 @ 前缀");
        generatedCode.Should().Contain("IsNullOrWhiteSpace(@event)",
            "字符串属性引用位必须补 @ 前缀");
        generatedCode.Should().Contain("CreateFileContent(@string!, @default)",
            "文件属性与 byte[] 属性的引用位必须补 @ 前缀");
    }

    #endregion
}
