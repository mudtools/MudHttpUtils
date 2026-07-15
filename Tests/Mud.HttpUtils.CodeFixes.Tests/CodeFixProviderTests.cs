// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
// -----------------------------------------------------------------------

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using Mud.HttpUtils.CodeFixes;

namespace Mud.HttpUtils.CodeFixes.Tests;

/// <summary>
/// CodeFixProvider 单元测试。
/// 验证 AOT007 和 HTTPCLIENT007 诊断的自动修复功能。
/// </summary>
public class CodeFixProviderTests
{
    #region AOT007 CodeFix 测试

    [Fact]
    public async Task Aot007CodeFix_ReplacesXmlWithJson()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(BaseAddress = "https://api.example.com")]
    public interface ITestApi
    {
        [Get("/data")]
        [SerializationMethod(SerializationMethod.Xml)]
        Task<string> GetDataAsync();
    }
}
""";

        var (document, diagnostic) = await CreateDocumentWithDiagnosticAsync(
            source,
            "AOT007",
            FindSerializationMethodAttribute);

        var codeFix = new AotXmlCodeFixProvider();
        var fixedDocument = await ApplyCodeFixAsync(codeFix, document, diagnostic);

        var fixedSource = (await fixedDocument.GetSyntaxRootAsync())!.ToFullString();
        fixedSource.Should().Contain("SerializationMethod.Json");
        fixedSource.Should().NotContain("SerializationMethod.Xml");
    }

    [Fact]
    public async Task Aot007CodeFix_FixableDiagnosticIds_ContainsAot007()
    {
        var provider = new AotXmlCodeFixProvider();
        provider.FixableDiagnosticIds.Should().Contain("AOT007");
    }

    #endregion

    #region HTTPCLIENT007 CodeFix 测试

    [Fact]
    public async Task HttpClient007CodeFix_RemovesHttpClientProperty()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public interface ITestTokenManager
    {
        IMudAppContext GetDefaultApp();
        IMudAppContext GetApp(string appKey);
    }

    [HttpClientApi(HttpClient = "IEnhancedHttpClient", TokenManage = "ITestTokenManager")]
    public interface ITestApi
    {
        [Get("/data")]
        Task<string> GetDataAsync();
    }
}
""";

        var (document, diagnostic) = await CreateDocumentWithDiagnosticAsync(
            source,
            "HTTPCLIENT007",
            FindHttpClientApiAttribute);

        var codeFix = new HttpClientMutuallyExclusiveCodeFixProvider();
        var fixedDocument = await ApplyCodeFixAsync(
            codeFix, document, diagnostic,
            titleFilter: "移除 HttpClient 属性");

        var fixedSource = (await fixedDocument.GetSyntaxRootAsync())!.ToFullString();
        fixedSource.Should().NotContain("HttpClient = ");
        fixedSource.Should().Contain("TokenManage = ");
    }

    [Fact]
    public async Task HttpClient007CodeFix_RemovesTokenManageProperty()
    {
        var source = """
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public interface ITestTokenManager
    {
        IMudAppContext GetDefaultApp();
        IMudAppContext GetApp(string appKey);
    }

    [HttpClientApi(HttpClient = "IEnhancedHttpClient", TokenManage = "ITestTokenManager")]
    public interface ITestApi
    {
        [Get("/data")]
        Task<string> GetDataAsync();
    }
}
""";

        var (document, diagnostic) = await CreateDocumentWithDiagnosticAsync(
            source,
            "HTTPCLIENT007",
            FindHttpClientApiAttribute);

        var codeFix = new HttpClientMutuallyExclusiveCodeFixProvider();
        var fixedDocument = await ApplyCodeFixAsync(
            codeFix, document, diagnostic,
            titleFilter: "移除 TokenManage 属性");

        var fixedSource = (await fixedDocument.GetSyntaxRootAsync())!.ToFullString();
        fixedSource.Should().Contain("HttpClient = ");
        fixedSource.Should().NotContain("TokenManage = ");
    }

    [Fact]
    public void HttpClient007CodeFix_FixableDiagnosticIds_ContainsHttpClient007()
    {
        var provider = new HttpClientMutuallyExclusiveCodeFixProvider();
        provider.FixableDiagnosticIds.Should().Contain("HTTPCLIENT007");
    }

    #endregion

    #region AOT004 / AOT005 / AOT006 CodeFix 测试

    [Fact]
    public async Task AotJsonContextCodeFix_AddsJsonSerializableToExistingContext_Aot004()
    {
        var source = """
using System.Text.Json.Serialization;

namespace TestNamespace
{
    public class UserDto { public int Id { get; set; } }

    [JsonSerializable(typeof(OtherDto))]
    internal partial class AppJsonContext : JsonSerializerContext { }

    public class OtherDto { public int Id { get; set; } }

    public interface ITestApi
    {
        Task<UserDto> GetUserAsync();
    }
}
""";

        var properties = ImmutableDictionary<string, string>.Empty
            .Add("TypeFullName", "global::TestNamespace.UserDto");

        var (document, diagnostic) = await CreateDocumentWithDiagnosticAsync(
            source,
            "AOT004",
            root => root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == "GetUserAsync"),
            properties);

        var codeFix = new AotJsonContextCodeFixProvider();
        var fixedDocument = await ApplyCodeFixAsync(codeFix, document, diagnostic);

        var fixedSource = (await fixedDocument.GetSyntaxRootAsync())!.ToFullString();
        fixedSource.Should().Contain("System.Text.Json.Serialization.JsonSerializableAttribute(typeof(global::TestNamespace.UserDto))");
        fixedSource.Should().Contain("class AppJsonContext");
    }

    [Fact]
    public async Task AotJsonContextCodeFix_AddsJsonSerializableToExistingContext_Aot005()
    {
        var source = """
using System.Text.Json.Serialization;

namespace TestNamespace
{
    public class QueryDto { public int Id { get; set; } }

    [JsonSerializable(typeof(OtherDto))]
    internal partial class AppJsonContext : JsonSerializerContext { }

    public class OtherDto { public int Id { get; set; } }

    public interface ITestApi
    {
        Task<QueryDto> SearchAsync();
    }
}
""";

        var properties = ImmutableDictionary<string, string>.Empty
            .Add("TypeFullName", "global::TestNamespace.QueryDto");

        var (document, diagnostic) = await CreateDocumentWithDiagnosticAsync(
            source,
            "AOT005",
            root => root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == "SearchAsync"),
            properties);

        var codeFix = new AotJsonContextCodeFixProvider();
        var fixedDocument = await ApplyCodeFixAsync(codeFix, document, diagnostic);

        var fixedSource = (await fixedDocument.GetSyntaxRootAsync())!.ToFullString();
        fixedSource.Should().Contain("System.Text.Json.Serialization.JsonSerializableAttribute(typeof(global::TestNamespace.QueryDto))");
    }

    [Fact]
    public async Task AotJsonContextCodeFix_NoTypeFullName_RegistersNoFix()
    {
        var source = """
using System.Text.Json.Serialization;

namespace TestNamespace
{
    public class UserDto { public int Id { get; set; } }

    [JsonSerializable(typeof(OtherDto))]
    internal partial class AppJsonContext : JsonSerializerContext { }

    public class OtherDto { public int Id { get; set; } }

    public interface ITestApi
    {
        Task<UserDto> GetUserAsync();
    }
}
""";

        // 不附带 TypeFullName 属性
        var (document, diagnostic) = await CreateDocumentWithDiagnosticAsync(
            source,
            "AOT004",
            root => root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == "GetUserAsync"));

        var codeFix = new AotJsonContextCodeFixProvider();
        var actions = await GetRegisteredActionsAsync(codeFix, document, diagnostic);

        actions.Should().BeEmpty("缺少 TypeFullName 属性时不应注册任何修复");
    }

    [Fact]
    public void AotJsonContextCodeFix_FixableDiagnosticIds_ContainsAotIds()
    {
        var provider = new AotJsonContextCodeFixProvider();
        provider.FixableDiagnosticIds.Should().Contain("AOT004");
        provider.FixableDiagnosticIds.Should().Contain("AOT005");
        provider.FixableDiagnosticIds.Should().Contain("AOT006");
    }

    #endregion

    #region 辅助方法

    private static readonly MetadataReference[] TestReferences =
    {
        // 核心库（object / Task 等）与 System.Text.Json（JsonSerializerContext 基类）。
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Text.Json.Serialization.JsonSerializerContext).Assembly.Location),
    };

    private static async Task<(Document Document, Diagnostic Diagnostic)> CreateDocumentWithDiagnosticAsync(
        string source,
        string diagnosticId,
        Func<SyntaxNode, SyntaxNode?> attributeFinder,
        ImmutableDictionary<string, string>? properties = null)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);

        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "TestProject",
            "TestAssembly",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: TestReferences);

        var project = workspace.AddProject(projectInfo);
        var document = workspace.AddDocument(project.Id, "TestFile.cs", SourceText.From(source));

        // 模拟一个诊断，定位到语法节点
        var root = await document.GetSyntaxRootAsync();
        var attribute = attributeFinder(root!);

        var location = attribute?.GetLocation() ?? Location.None;
        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptorHelper.GetDescriptor(diagnosticId),
            location,
            properties);

        return (document, diagnostic);
    }

    private static SyntaxNode? FindSerializationMethodAttribute(SyntaxNode root)
    {
        return root.DescendantNodes()
            .OfType<AttributeSyntax>()
            .FirstOrDefault(a => a.Name.ToString().Contains("SerializationMethod"));
    }

    private static SyntaxNode? FindHttpClientApiAttribute(SyntaxNode root)
    {
        return root.DescendantNodes()
            .OfType<AttributeSyntax>()
            .FirstOrDefault(a => a.Name.ToString().Contains("HttpClientApi"));
    }

    private static async Task<List<CodeAction>> GetRegisteredActionsAsync(
        CodeFixProvider provider,
        Document document,
        Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        var captureContext = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await provider.RegisterCodeFixesAsync(captureContext);
        return actions;
    }

    private static async Task<Document> ApplyCodeFixAsync(
        CodeFixProvider provider,
        Document document,
        Diagnostic diagnostic,
        string? titleFilter = null)
    {
        var actions = await GetRegisteredActionsAsync(provider, document, diagnostic);

        actions.Should().NotBeEmpty($"CodeFixProvider should register at least one fix for {diagnostic.Id}");

        var actionToApply = titleFilter != null
            ? actions.FirstOrDefault(a => a.Title.Contains(titleFilter))
            : actions.First();

        actionToApply.Should().NotBeNull(
            titleFilter != null
                ? $"CodeFixProvider should register a fix matching '{titleFilter}'"
                : "CodeFixProvider should register at least one fix");

        var operations = await actionToApply!.GetOperationsAsync(CancellationToken.None);
        var changedDocOp = operations.OfType<ApplyChangesOperation>().First();
        return changedDocOp.ChangedSolution.GetDocument(document.Id)!;
    }

    #endregion
}

/// <summary>
/// 测试用诊断描述符辅助类。
/// </summary>
internal static class DiagnosticDescriptorHelper
{
    private static readonly Dictionary<string, DiagnosticDescriptor> _descriptors = new()
    {
        ["AOT004"] = new DiagnosticDescriptor(
            "AOT004",
            "HttpClient API 方法的 DTO 未被任何 JsonSerializerContext 覆盖",
            "{0}",
            "AOT",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true),
        ["AOT005"] = new DiagnosticDescriptor(
            "AOT005",
            "查询参数类型使用 JSON 序列化但未被 Context 覆盖",
            "{0}",
            "AOT",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true),
        ["AOT006"] = new DiagnosticDescriptor(
            "AOT006",
            "[HttpJsonSerializable] 类型未被任何 JsonSerializerContext 覆盖",
            "{0}",
            "AOT",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true),
        ["AOT007"] = new DiagnosticDescriptor(
            "AOT007",
            "XML 序列化在 Native AOT 下不支持",
            "{0}",
            "AOT",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true),
        ["HTTPCLIENT007"] = new DiagnosticDescriptor(
            "HTTPCLIENT007",
            "HttpClient 与 TokenManage 互斥",
            "{0}",
            "代码生成",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true),
    };

    public static DiagnosticDescriptor GetDescriptor(string id)
    {
        return _descriptors.TryGetValue(id, out var descriptor)
            ? descriptor
            : new DiagnosticDescriptor(id, "Test Diagnostic", "{0}", "Test", DiagnosticSeverity.Error, true);
    }
}
