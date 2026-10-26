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

    /// <summary>
    /// P1-2 回归：C# 保留字命名的接口成员/参数 —— 声明位与引用位必须成对补 <c>@</c> 前缀，
    /// 否则生成文件报 CS1001/CS0825/CS0102 等（探针实证 12 个错误）。载荷引擎已修同一类缺陷
    /// （PayloadFieldMapRenderer 的 EscapeIdentifier），本用例守护 HTTP 引擎同步覆盖。
    /// </summary>
    [Fact]
    public void KeywordNamedMembers_ShouldEmitEscapedIdentifiers()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IKeywordApi
                {
                    [Get("/data/{int}")]
                    Task<string> @class([Path] int @int);

                    [Get("/ids/{int}")]
                    [Cache(60)]
                    Task<string> CachedAsync([Path] int @int, [Query] string @event, [Query] int[] @default);

                    [Get("/fmt/{int}")]
                    Task<string> FormattedAsync([Path(FormatString = "D4")] int @int);

                    [Get("/text/{string}")]
                    Task<string> NullableTextAsync([Path] string? @string);

                    [Get("/q")]
                    Task<string> QueryAsync([Query] string @event);

                    [Query("kw")]
                    string @string { get; set; }
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "关键字命名的接口成员/参数必须经 @ 转义且生成代码可编译（P1-2 修复）");

        var generated = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));

        generated.Should().Contain("@class(", "方法声明位必须补 @ 前缀（CS1001 回归）");
        generated.Should().Contain("@int", "参数声明位必须补 @ 前缀");
        generated.Should().Contain("@event", "[Query] 参数引用位必须补 @ 前缀（与声明位成对）");
        generated.Should().Contain("public string @string", "接口属性声明位必须补 @ 前缀");
        generated.Should().Contain("__queryParams.Add(\"kw\", @string)",
            "接口 [Query] 属性的引用位必须与声明位成对转义（CS1001 回归）");
        generated.Should().Contain("__queryParams.Add(\"event\", @event)",
            "参数引用位必须与声明位成对转义（CS1001/CS0103 回归）");

        // [Path] 引用位（URL 插值洞 / string.Format / nameof）——F3 原清单遗漏的发射点
        generated.Should().Contain("Uri.EscapeDataString((@int)",
            "[Path] 关键字参数的 URL 插值洞必须补 @ 前缀");
        generated.Should().Contain("\"{0:D4}\", @int)",
            "[Path(FormatString)] 的 string.Format 实参必须补 @ 前缀");
        generated.Should().Contain("nameof(@string)",
            "可空 string 路径参数的 nameof 必须补 @ 前缀（CS1001 回归）");

        // 缓存键表达式（标量段）——F3 原清单遗漏的发射点
        generated.Should().Contain("Convert.ToString(@int, CultureInfo.InvariantCulture)",
            "缓存键标量段引用必须补 @ 前缀（CacheKeySafety）");
        generated.Should().Contain("Convert.ToString(@event, CultureInfo.InvariantCulture)",
            "缓存键标量段引用必须补 @ 前缀（CacheKeySafety）");
        generated.Should().Contain("string.Join(\",\", @default.Select(x =>",
            "缓存键集合段引用必须补 @ 前缀（CacheKeySafety）");
    }

    /// <summary>
    /// P1-2 补充：保留字命名的<b>类型</b>出现在方法签名中时（返回类型 / 参数类型），
    /// 类型显示串同样必须保留 <c>@</c> 前缀。
    /// </summary>
    /// <remarks>
    /// <c>SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(...)</c> 会<b>整体替换</b>
    /// 选项集合，历史实现在返回类型/参数类型/属性类型格式中只写 <c>UseSpecialTypes</c>，
    /// 丢失 <c>EscapeKeywordIdentifiers</c> ⇒ <c>class @class</c> 渲染为 <c>class</c>（CS1001）。
    /// </remarks>
    [Fact]
    public void KeywordNamedType_InSignature_ShouldEmitEscapedTypeReferences()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public class @class
                {
                    public string Value { get; set; } = string.Empty;
                }

                [HttpClientApi]
                public interface IKeywordTypeApi
                {
                    [Get("/data")]
                    Task<@class> GetAsync();

                    [Post("/data")]
                    Task SendAsync([Body] @class @event);
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "保留字命名类型出现在签名中时，生成代码必须可编译（类型显示串须含 @）");

        var generated = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));
        generated.Should().Contain("TestNamespace.@class",
            "返回类型/参数类型显示串必须保留 @ 前缀（EscapeKeywordIdentifiers 不得被 WithMiscellaneousOptions 覆盖）");
        generated.Should().Contain("@event",
            "保留字参数名在声明位必须补 @ 前缀");
    }

    /// <summary>
    /// P1-2 回归（负向守护口径）：关键字方法名进入占位实现路径（无 HTTP 方法特性）时同样须转义。
    /// </summary>
    [Fact]
    public void KeywordNamedMethod_WithoutHttpAttribute_PlaceholderCompiles()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IKeywordApi
                {
                    Task<string> @default();
                }
            }
            """;

        GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "关键字方法名的占位实现必须可编译（P1-2 修复；HTTPCLIENT024 占位路径）");
    }
}
