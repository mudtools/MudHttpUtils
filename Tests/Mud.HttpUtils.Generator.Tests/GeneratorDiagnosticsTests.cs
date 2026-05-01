namespace Mud.HttpUtils.Generator.Tests;

public class GeneratorDiagnosticsTests
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

        var generatorType = TestHelper.GetType("Mud.HttpUtils.HttpInvokeClassSourceGenerator");
        var generator = (IIncrementalGenerator)Activator.CreateInstance(generatorType)!;
        var driver = CSharpGeneratorDriver.Create(generator);
        return driver.RunGenerators(compilation);
    }

    #region HTTPCLIENT012 - Generic Interface Not Supported

    [Fact]
    public void Generator_WithGenericInterface_GeneratesHTTPCLIENT012()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi<T>
    {
        [Get(""/items"")]
        Task<T> GetItemsAsync();
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT012");
    }

    #endregion

    #region HTTPCLIENT007 - HttpClient and TokenManager Mutually Exclusive

    [Fact]
    public void Generator_WithBothHttpClientAndTokenManager_GeneratesHTTPCLIENT007()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(HttpClient = ""myClient"", TokenManage = ""myManager"")]
    public interface ITestApi
    {
        [Get(""/data"")]
        Task<string> GetDataAsync();
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT007");
    }

    #endregion

    #region HTTPCLIENT005 - Invalid URL Template

    [Fact]
    public void Generator_WithInvalidUrlTemplate_GeneratesHTTPCLIENT005()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/users/{userId/invalid"")]
        Task<string> GetUserAsync(string userId);
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT005");
    }

    #endregion

    #region No Diagnostics for Valid Interface

    [Fact]
    public void Generator_WithValidInterface_NoDiagnostics()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/data"")]
        Task<string> GetDataAsync();
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().BeEmpty();
    }

    #endregion
}
