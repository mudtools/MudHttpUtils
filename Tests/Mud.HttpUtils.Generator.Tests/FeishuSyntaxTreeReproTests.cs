// -----------------------------------------------------------------------
// 临时复现测试：HTTPCLIENT004「编译中不包含 SyntaxTree」误报
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

using Mud.HttpUtils.Analyzers;

/// <summary>
/// 复现 IFeishuV1DriveFiles 触发的 HTTPCLIENT004「编译中不包含 SyntaxTree (Parameter 'syntaxTree')」误报。
/// 结构对齐真实场景：基接口与派生接口位于不同源文件（不同 SyntaxTree），派生接口带 [Token] 与 [HttpClientApi(IsAbstract = true)]。
/// </summary>
public class FeishuSyntaxTreeReproTests
{
    private const string BaseTypesSource = """
        using System.Threading.Tasks;
        using Mud.HttpUtils;

        namespace TestNS
        {
            public static class FeishuTokenTypes
            {
                public const string TenantAccessToken = "TenantAccessToken";
            }

            public static class Consts
            {
                public const string Authorization = "Authorization";
                public const string User_Id_Type = "open_id";
                public const int PageSize_10 = 10;
            }

            public interface IFeishuAppManager
            {
                Mud.HttpUtils.IMudAppContext GetDefaultApp();
                Mud.HttpUtils.IMudAppContext GetApp(string appKey);
            }

            public interface IFeishuAppContextSwitcher
            {
                // 模拟基接口中带 HTTP 特性的方法（触发跨语法树方法定位）
                [Mud.HttpUtils.Attributes.Get("/base/ping")]
                Task<string?> PingAsync(System.Threading.CancellationToken cancellationToken = default);
            }

            public record FeishuApiResult<T>(int Code, T? Data);
            public record FeishuApiPageListResult<T>(int Code, T? Data);
            public record FeishuNullDataApiResult(int Code);
            public record MetasBatchQueryRequest;
            public record MetasBatchQueryResult;
            public record FileStatisticsReuslt;
            public record FileViewRecord;
            public record CopyFileRequest;
            public record FileTaskResult;
            public record MoveFileRequest;
            public record CreateShortcutRequest;
            public record UploadAllFileRequest;
            public record FilesUploadPrepareRequest;
            public record FilesUploadPartRequest;
            public record FilesUploadFinishRequest;
            public record FilesUploadAllResult;
            public record FilesUploadPrepareResult;
            public record FilesUploadFinishResult;
            public record ImportTasksRequest;
            public record ImportTaskResult;
            public record TasksResult;
            public record ExportTasksRequest;
            public record ExportTasksResult;
            public record FileLikeInfo;
            public record FileContentResult;
        }
        """;

    private const string DriveFilesSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Mud.HttpUtils.Attributes;
        using TestNS;

        namespace TestNS
        {
            [HttpClientApi(TokenManage = nameof(IFeishuAppManager), IsAbstract = true)]
            [Token(FeishuTokenTypes.TenantAccessToken, Name = Consts.Authorization)]
            public interface IFeishuV1DriveFiles : IFeishuAppContextSwitcher
            {
                [Post("/open-apis/drive/v1/metas/batch_query")]
                Task<FeishuApiResult<MetasBatchQueryResult>?> BatchQueryMetasAsync(
                    [Body] MetasBatchQueryRequest metasBatchQueryRequest,
                    [Query("user_id_type")] string? user_id_type = Consts.User_Id_Type,
                    CancellationToken cancellationToken = default);

                [Get("/open-apis/drive/v1/files/{file_token}/statistics")]
                Task<FeishuApiResult<FileStatisticsReuslt>?> GetFileStatisticsByFileTokenAsync(
                  [Path] string file_token,
                  [Query("file_type")] string file_type,
                  CancellationToken cancellationToken = default);

                [Get("/open-apis/drive/v1/files/{file_token}/view_records")]
                Task<FeishuApiPageListResult<FileViewRecord>?> GetFileViewRecordPageListByFileTokenAsync(
                   [Path] string file_token,
                   [Query("file_type")] string file_type,
                   [Query("page_size")] int page_size = Consts.PageSize_10,
                   [Query("page_token")] string? page_token = null,
                   [Query("viewer_id_type")] string? viewer_id_type = Consts.User_Id_Type,
                   CancellationToken cancellationToken = default);

                [Post("/open-apis/drive/v1/files/{file_token}/copy")]
                Task<FeishuApiResult<CopyFileResult>?> CopyFileByFileTokenAsync(
                  [Body] CopyFileRequest copyFileRequest,
                  [Path] string file_token,
                  [Query("user_id_type")] string? user_id_type = Consts.User_Id_Type,
                  CancellationToken cancellationToken = default);

                [Delete("/open-apis/drive/v1/files/{file_token}")]
                Task<FeishuApiResult<FileTaskResult>?> DeleteFileByFileTokenAsync(
                    [Path] string file_token,
                    [Query("type")] string file_type,
                    CancellationToken cancellationToken = default);

                [Post("/open-apis/drive/v1/files/upload_all")]
                Task<FeishuApiResult<FilesUploadAllResult>?> UploadAllFileAsync(
                  [FormContent] UploadAllFileRequest uploadAllFileRequest,
                  CancellationToken cancellationToken = default);

                [Get("/open-apis/drive/v1/files/{file_token}/download")]
                Task<byte[]?> DownloadFileAsync(
                    [Path] string file_token,
                    [Header("Range")] string? range = null,
                    CancellationToken cancellationToken = default);

                [Get("/open-apis/drive/v1/export_tasks/file/{file_token}/download")]
                Task<byte[]?> DownloadExportFileAsync([Path] string file_token, CancellationToken cancellationToken = default);

                [Get("/open-apis/drive/v1/export_tasks/file/{file_token}/download")]
                Task DownloadExportLargeFileAsync([Path] string file_token, [FilePath] string localFile, CancellationToken cancellationToken = default);
            }
        }
        """;

    private static GeneratorDriver RunGenerator(string[] sources)
    {
        var trees = sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, path: $"File{i}.cs")).ToArray();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            trees,
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generatorType = TestHelper.GetType("Mud.HttpUtils.HttpInvokeClassSourceGenerator");
        var generator = (IIncrementalGenerator)Activator.CreateInstance(generatorType)!;
        return CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
    }

    [Fact]
    public void FeishuStyleMultiTreeInterface_DoesNotReportSyntaxTreeNotInCompilation()
    {
        var diagnostics = RunGenerator([BaseTypesSource, DriveFilesSource])
            .GetRunResult().Diagnostics;

        var paramErrors = diagnostics.Where(d => d.Id == "HTTPCLIENT004").ToList();
        paramErrors.Should().BeEmpty(
            "不应误报 HTTPCLIENT004；实际：{0}",
            string.Join("\n", paramErrors.Select(d => d.ToString())));
    }

    private static (GeneratorDriver driver, CSharpCompilation compilation) CreateCompilation(string[] sources)
    {
        var trees = sources.Select((s, i) => CSharpSyntaxTree.ParseText(s, path: $"File{i}.cs")).ToArray();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            trees,
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var generatorType = TestHelper.GetType("Mud.HttpUtils.HttpInvokeClassSourceGenerator");
        var generator = (IIncrementalGenerator)Activator.CreateInstance(generatorType)!;
        return (CSharpGeneratorDriver.Create(generator), compilation);
    }

    private static string[] GetDiagnostics(GeneratorDriver driver, CSharpCompilation compilation)
    {
        var run = driver.RunGenerators(compilation);
        return run.GetRunResult().Diagnostics
            .Where(d => d.Id == "HTTPCLIENT004")
            .Select(d => d.ToString())
            .ToArray();
    }

    [Fact]
    public void IncrementalReplay_WhenOtherFileChanges_DoesNotReportSyntaxTreeNotInCompilation()
    {
        // 第 1 轮：基线编译
        var (driver, c1) = CreateCompilation([BaseTypesSource, DriveFilesSource]);
        var first = GetDiagnostics(driver, c1);
        first.Should().BeEmpty(string.Join("\n", first));

        // 第 2 轮：修改"基类型文件"（新增一个无关类型），DriveFiles 树实例保持不变（模拟 IDE），
        // IFeishuV1DriveFiles 指纹不变 → 缓存的 InterfaceModel（旧 SemanticModel/旧树）被重放。
        var changedBaseSource = BaseTypesSource + "\npublic static class Unrelated { }\n";
        var newBaseTree = CSharpSyntaxTree.ParseText(changedBaseSource, path: "File0.cs");
        var c2 = c1.RemoveAllSyntaxTrees().AddSyntaxTrees(newBaseTree, c1.SyntaxTrees.Last());

        var second = GetDiagnostics(driver, c2);
        second.Should().BeEmpty(string.Join("\n", second));

        // 第 3 轮：对 DriveFiles 文件做纯注释（trivia）修改 → 指纹不变但树实例更换。
        var triviaEdited = DriveFilesSource.Replace("IFeishuV1DriveFiles : IFeishuAppContextSwitcher", "IFeishuV1DriveFiles : IFeishuAppContextSwitcher // 注释");
        var driveTree1 = c2.SyntaxTrees.Last();
        var c3 = c2.RemoveAllSyntaxTrees().AddSyntaxTrees(c2.SyntaxTrees.First(), CSharpSyntaxTree.ParseText(triviaEdited, path: driveTree1.FilePath));

        var third = GetDiagnostics(driver, c3);
        third.Should().BeEmpty(string.Join("\n", third));
    }

    [Fact]
    public void SemanticModelCache_TryGet_TreeInCompilation_ReturnsModel()
    {
        var tree = CSharpSyntaxTree.ParseText("public class InTree { }", path: "InTree.cs");
        var compilation = CSharpCompilation.Create("GuardC1", [tree]);

        var result = SemanticModelCache.TryGet(compilation, tree, out var model);

        result.Should().BeTrue();
        model.Should().NotBeNull();
    }

    [Fact]
    public void SemanticModelCache_TryGet_ForeignTree_ReturnsFalseWithoutThrowing()
    {
        // 复现原缺陷触发条件：把不属于当前编译的语法树交给 GetSemanticModel
        // —— 原实现会抛出 ArgumentException("编译中不包含 SyntaxTree")，
        // 沿生成管道上抛后伪装为 HTTPCLIENT004「接口参数配置错误」误报。
        var treeInA = CSharpSyntaxTree.ParseText("public class A { }", path: "A.cs");
        var treeInB = CSharpSyntaxTree.ParseText("public class B { }", path: "B.cs");
        var compilationA = CSharpCompilation.Create("GuardC_A", [treeInA]);
        var compilationB = CSharpCompilation.Create("GuardC_B", [treeInB]);

        var actForeign = () => SemanticModelCache.TryGet(compilationA, treeInB, out _);
        actForeign.Should().NotThrow("外部语法树应降级返回 false，而不是抛出异常");
        SemanticModelCache.TryGet(compilationA, treeInB, out var foreignModel).Should().BeFalse();
        foreignModel.Should().BeNull();

        // GetOrCreate 保留原契约：显式抛出（仅限确认包含的调用方使用，如遍历 compilation.SyntaxTrees 的场景）
        var actGetOrCreate = () => SemanticModelCache.GetOrCreate(compilationA, treeInB);
        actGetOrCreate.Should().Throw<ArgumentException>().WithParameterName("syntaxTree");

        // 同一编译内的语法树不受影响
        SemanticModelCache.TryGet(compilationB, treeInB, out _).Should().BeTrue();
    }

    [Fact]
    public void AnalyzeInterfaceProperties_ForeignTree_ReturnsEmptyWithoutThrowing()
    {
        var treeInA = CSharpSyntaxTree.ParseText("namespace N { public interface IInA { } }", path: "A.cs");
        var treeInB = CSharpSyntaxTree.ParseText(
            "namespace N { public interface IInB { [Mud.HttpUtils.Attributes.Query(\"q\")] string Q { get; } } }",
            path: "B.cs");
        var compilationA = CSharpCompilation.Create("PropC_A", [treeInA], BasicReferenceAssemblies.GetReferences());
        var foreignDecl = treeInB.GetRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>().Single();

        var properties = MethodAnalyzer.AnalyzeInterfaceProperties(foreignDecl, compilationA, semanticModel: null);

        properties.Should().BeEmpty("外部语法树应降级为空属性集，而不是抛出 ArgumentException");
    }

    [Fact]
    public void GetAllBaseInterfaceSyntaxNodes_ForeignTree_ReturnsEmptyWithoutThrowing()
    {
        var treeInA = CSharpSyntaxTree.ParseText("namespace N { public interface IInA { } }", path: "A.cs");
        var treeInB = CSharpSyntaxTree.ParseText("namespace N { public interface IInB { } }", path: "B.cs");
        var compilationA = CSharpCompilation.Create("BaseC_A", [treeInA], BasicReferenceAssemblies.GetReferences());
        var foreignDecl = treeInB.GetRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>().Single();

        var act = () => MethodAnalyzer.GetAllBaseInterfaceSyntaxNodes(compilationA, foreignDecl, semanticModel: null);

        act.Should().NotThrow("外部语法树应终止该分支遍历（yield break），而不是抛出 ArgumentException");
        act().Should().BeEmpty();
    }
}
