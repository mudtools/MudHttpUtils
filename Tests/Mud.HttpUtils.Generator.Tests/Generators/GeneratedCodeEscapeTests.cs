namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// [Phase2 修复 1.8 / §3.7] 生成源码转义回归守卫（动态口径）。
/// </summary>
/// <remarks>
/// <para>
/// 生成器把 <c>[Token]</c> 配置（TokenManagerKey / Scopes / Name）以裸插值写入字符串字面量。
/// 历史缺陷：<c>MethodGenerator</c> 的 Scopes / ApiKey / TokenManagerKey 三处未调用
/// <c>StringEscapeHelper.EscapeString</c>，配置含 <c>"</c> 或 <c>\</c> 时会产出不可编译的 C#。
/// </para>
/// <para>
/// 采用<strong>动态</strong>口径（方案 A.5 修订 4）：直接编译含特殊字符配置的接口，
/// 断言「输入 + 生成产物」整体无 Error 级诊断。相比静态扫描生成器源码，
/// 动态口径不会误伤 <c>string.Join("{{ … }}")</c> 这类合法花括号转义，且对未来的新插值点同样有效。
/// </para>
/// </remarks>
public class GeneratedCodeEscapeTests
{
    /// <summary>
    /// TokenManagerKey 含双引号 → 生成代码必须转义为 <c>\"</c> 而不是提前闭合字符串字面量。
    /// </summary>
    [Fact]
    public void TokenManagerKeyWithQuotes_GeneratesCompilableCode()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public interface ITestTokenManager
                {
                    IMudAppContext GetDefaultApp();
                    IMudAppContext GetApp(string appKey);
                }

                [HttpClientApi(TokenManage = "ITestTokenManager")]
                [Token(TokenManagerKey = "key\"with\"quotes", Scopes = "read\"only,write")]
                public interface ITokenApi
                {
                    [Get("/data")]
                    Task<string> GetAsync();
                }
            }
            """;

        GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "TokenManagerKey/Scopes 含双引号时生成代码必须可编译（Phase2 1.8）");
    }

    /// <summary>
    /// TokenManagerKey 含反斜杠 → 生成代码必须转义为 <c>\\</c>。
    /// </summary>
    [Fact]
    public void TokenManagerKeyWithBackslash_GeneratesCompilableCode()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public interface ITestTokenManager
                {
                    IMudAppContext GetDefaultApp();
                    IMudAppContext GetApp(string appKey);
                }

                [HttpClientApi(TokenManage = "ITestTokenManager")]
                [Token(TokenManagerKey = "domain\\key", Scopes = "a\\b")]
                public interface ITokenApi
                {
                    [Get("/data")]
                    Task<string> GetAsync();
                }
            }
            """;

        GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "TokenManagerKey/Scopes 含反斜杠时生成代码必须可编译（Phase2 1.8）");
    }

    /// <summary>
    /// ApiKey 注入模式下的令牌名含双引号（<c>InterfaceTokenName</c> → <c>GetApiKeyAsync("…")</c>）。
    /// </summary>
    [Fact]
    public void ApiKeyTokenNameWithQuotes_GeneratesCompilableCode()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public interface ITestTokenManager
                {
                    IMudAppContext GetDefaultApp();
                    IMudAppContext GetApp(string appKey);
                }

                [HttpClientApi(TokenManage = "ITestTokenManager")]
                [Token(Name = "X-Api\"Key", InjectionMode = TokenInjectionMode.ApiKey)]
                public interface IApiKeyApi
                {
                    [Get("/data")]
                    Task<string> GetAsync();
                }
            }
            """;

        GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "ApiKey 模式令牌名含双引号时生成代码必须可编译（Phase2 1.8）");
    }

    /// <summary>
    /// [GEN-09] 方法级 <c>[Token(Name = "…")]</c> 的 Name 含双引号 → 生成代码必须转义为
    /// <c>\"</c>，并作为 ApiKey 名在两处（注入 + 恢复上下文）正确落地。
    /// </summary>
    [Fact]
    public void MethodLevelTokenNameWithQuotes_GeneratesCompilableCode()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public interface ITestTokenManager
                {
                    IMudAppContext GetDefaultApp();
                    IMudAppContext GetApp(string appKey);
                }

                [HttpClientApi(TokenManage = "ITestTokenManager")]
                [Token(Name = "X-Interface-Token", InjectionMode = TokenInjectionMode.ApiKey)]
                public interface IApiKeyApi
                {
                    [Get("/data")]
                    [Token(Name = "X-Method\"Key", InjectionMode = TokenInjectionMode.ApiKey)]
                    Task<string> GetAsync();
                }
            }
            """;

        GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "方法级 ApiKey 令牌名含双引号时生成代码必须可编译（GEN-09）");
    }

    /// <summary>
    /// [GEN-18][§8.6] 含 [Header] 参数时，生成代码必须发射运行期 CR/LF 校验守卫
    /// （net4x/netstandard2.0 编译的 HttpClient 不校验头值，存在头部注入风险）。
    /// </summary>
    [Fact]
    public void HeaderValue_WithCrLf_Throws()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IHeaderApi
                {
                    [Get("/data")]
                    Task<string> GetAsync([Header("X-Token")] string token);
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "含 [Header] 参数时生成代码必须可编译（GEN-18）");

        var generated = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));
        generated.Should().Contain("HttpHeaderValueValidator.IsValid(token)",
            "生成代码必须发射运行期 CR/LF 校验守卫（GEN-18）");
        generated.Should().Contain("ArgumentException",
            "守卫失败时应抛出带参数名提示的 ArgumentException（GEN-18）");
    }

    /// <summary>
    /// [GEN-01] CacheKeyTemplate 含花括号（含未配对的 <c>{bad</c>）→ 必须转义为 <c>{{…}}</c>，
    /// 否则 <c>{bad</c> 会成为插值孔导致生成的 CacheKey 插值串不可编译（CS1733/CS1006）。
    /// </summary>
    [Fact]
    public void CacheKeyTemplate_WithBraces_Compiles()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ICacheApi
                {
                    [Get("/products")]
                    [Cache(60, CacheKeyTemplate = "/u/{id}?{bad", UseSlidingExpiration = true)]
                    Task<string> GetAsync([Path] int id);
                }
            }
            """;

        GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "CacheKeyTemplate 含花括号（含未配对 {bad）时必须转义，生成的 CacheKey 插值串才可编译（GEN-01）");
    }

    /// <summary>
    /// G8-05：multipart 的「直接构造 + Add」站点必须发射<b>异常路径释放</b>守卫
    /// （局部变量 → try → Add → catch → Dispose → throw）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>MultipartFormDataContent.Add</c> 会对 name/fileName 做头部校验；实测（.NET 10）：
    /// 含 CR/LF → <c>FormatException</c>、含 <c>"</c> 或空白 → <c>ArgumentException</c>，
    /// 且抛出点在 <c>base.Add(content)</c> 之前 ⇒ 刚构造的 <c>HttpContent</c> 未移交 multipart。
    /// 带 <c>ContentType</c> 的分支（G7-19）早已做「仅异常路径 Dispose」，其余 8 个站点此前未保护 ⇒
    /// 分支间行为不一致（异常路径有时释放调用方流、有时不释放）。
    /// </para>
    /// <para>
    /// <b>副作用（有意为之）</b>：<c>StreamContent.Dispose()</c> 会关闭调用方传入的流（实测）。
    /// 此时请求构造已失败、不会再发送，故统一选择释放以消除不确定性。
    /// </para>
    /// </remarks>
    [Fact]
    public void MultipartAdd_Sites_EmitExceptionPathDisposeGuard()
    {
        const string source = """
            using System.IO;
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public class UserInfo
                {
                    public int Id { get; set; }
                }

                [HttpClientApi]
                public interface IMultipartApi
                {
                    [Post("/upload")]
                    Task<string> UploadAsync(
                        [Form(FieldName = "field")] string title,
                        [Form(FieldName = "count")] int count,
                        [Form(FieldName = "note")] UserInfo? note,
                        [MultipartForm] string payload,
                        [Upload(FieldName = "file", FileName = "a.txt")] Stream file,
                        [Upload(FieldName = "file2", FileName = "b.txt", ContentType = "text/plain")] Stream file2);
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "multipart（[Form]/[MultipartForm]/[Upload]）生成代码必须可编译（G8-05）");

        var generated = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));

        // 本输入共 6 个 multipart 内容站点：
        //   [Form] ×3（string / 非可空值类型 / 引用类型）+ [MultipartForm] ×1 + [Upload] ×2（无 ContentType / 带 ContentType）。
        // 第一个 [Upload] 走 G8-05 新增的守卫；带 ContentType 的走 G7-19 既有守卫。
        // 两者注释前缀一致（仅 G7-19 多一个「已」字），故用共同前缀匹配。
        var disposeGuards = System.Text.RegularExpressions.Regex
            .Matches(generated, "仅异常路径释放（成功路径所有权")
            .Count;

        disposeGuards.Should().BeGreaterThanOrEqualTo(6,
            $"每个「直接构造 + Add」站点都必须有异常路径 Dispose；实际 {disposeGuards} 处");
        generated.Should().Contain("catch", "守卫必须位于 catch 块中（仅在 Add 抛异常时释放）");
        generated.Should().Contain("throw;", "释放后必须原样重抛，不得吞掉异常");
    }
}
