// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// G8-20 复发守卫：<c>InterfaceToken*</c>（接口级令牌字段）只允许出现在<b>模型</b>与<b>解析器</b>中；
/// 其余任何生成器文件必须走 <c>EffectiveToken*</c> / <c>HasExplicitTokenInjectionMode</c> 访问器。
/// </summary>
/// <remarks>
/// <para>
/// <b>动机（三轮同族缺陷）</b>：同一「口径分裂」缺陷在本仓库被反复发现 ——
/// <list type="number">
///   <item><description>G8-01：<c>ShouldGenerateTokenQuery</c> / <c>BuildUrlWithPlaceholders</c> /
///     <c>GetTokenHeaderName</c> 只认接口级 ⇒ 方法级 <c>Query</c>/<c>Path</c> 令牌被静默丢弃；</description></item>
///   <item><description>G8-19：ApiKey 模式的 <c>Name</c>（注入头名）被丢弃 ⇒ 密钥写错头；</description></item>
///   <item><description>G8-20：<c>HeaderParameterBinder</c> 直读接口级 <c>Name</c> 并把 <c>Name</c> 与
///     Scheme 字面量比对 ⇒ 参数默认头名落回参数名。</description></item>
/// </list>
/// 每次都是「某个新消费点忘了走有效级」。本守卫把该不变式<b>机器化</b>：新增消费点若直读原始字段即失败。
/// </para>
/// <para>
/// <b>白名单与理由</b>：
/// <list type="bullet">
///   <item><description><c>MethodAnalysisResult.cs</c>：字段声明本体 + <c>Effective*</c>/<c>HasExplicit*</c> 访问器实现；</description></item>
///   <item><description><c>MethodAnalyzer.cs</c>：唯一的赋值点（从特性解析出的接口级值写入模型）。</description></item>
/// </list>
/// 白名单**仅此两个文件**。若某处确实需要「原始值」，请先在模型上新增语义化访问器，
/// 而不是放宽本守卫（这正是 G8-20 收敛 <c>ShouldInjectToken</c> 的做法）。
/// </para>
/// <para>
/// <b>判据实现</b>：逐行处理，跳过整行注释（<c>//</c> / <c>///</c>）并剥离行尾注释后再检索字段名，
/// 以免把「解释性注释」当作消费点（假阳性）；代价是极少数把字段写在字符串拼接中的行可能漏检（可接受的假阴性）。
/// </para>
/// </remarks>
public class EffectiveTokenFieldUsageGuardTests
{
    private const string GeneratorRoot = @"../../../../../Mud.HttpUtils.Generator";

    /// <summary>允许直接引用接口级令牌字段的文件（其余文件一律失败）。</summary>
    private static readonly HashSet<string> AllowedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "MethodAnalysisResult.cs",
        "MethodAnalyzer.cs",
    };

    /// <summary>接口级令牌原始字段名（消费点必须改用对应访问器）。</summary>
    private static readonly string[] GuardedFields =
    [
        "InterfaceTokenName",
        "InterfaceTokenInjectionMode",
        "InterfaceTokenScheme",
        "InterfaceTokenScopes",
    ];

    [Fact]
    public void InterfaceLevelTokenFields_AreOnlyUsedInsideModelAndAnalyzer()
    {
        var sourceFiles = EnumerateGeneratorSources().ToList();
        sourceFiles.Should().NotBeEmpty("守卫自身必须能枚举到生成器源码，否则扫描逻辑失效");

        var files = sourceFiles.Select(f => (FileName: Path.GetFileName(f), Text: File.ReadAllText(f))).ToList();
        var offenders = FindOffenders(files);

        offenders.Should().BeEmpty(
            "G8-20：接口级令牌字段只允许出现在 MethodAnalysisResult.cs（声明+访问器）与 MethodAnalyzer.cs（赋值）。" +
            "违反项：\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// 守卫自检（非恒真证明）：构造一个含违规代码的文件，检测逻辑必须命中；
    /// 同时注释中的同名提及必须<b>不</b>命中（避免假阳性）。
    /// </summary>
    [Fact]
    public void SelfCheck_DetectsViolation_AndIgnoresComments()
    {
        (string FileName, string Text)[] synthetic =
        [
            ("SomeGenerator.cs", "var n = methodInfo.InterfaceTokenName;\n"),
            ("AnotherGenerator.cs", "// 说明：此前用 InterfaceTokenName 曾出错\n/// InterfaceTokenInjectionMode 已弃用\n"),
            ("MethodAnalysisResult.cs", "public string? InterfaceTokenName { get; set; }\n"),
        ];

        var offenders = FindOffenders(synthetic);

        offenders.Should().ContainSingle("检测逻辑必须命中真实违规（SomeGenerator.cs）");
        offenders[0].Should().Contain("SomeGenerator.cs").And.Contain("InterfaceTokenName");
    }

    /// <summary>核心检测逻辑（抽出以便自检）：返回违规描述列表。</summary>
    private static List<string> FindOffenders(IEnumerable<(string FileName, string Text)> files)
    {
        var offenders = new List<string>();

        foreach (var (fileName, text) in files)
        {
            if (AllowedFiles.Contains(fileName))
                continue;

            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var code = StripComments(lines[i]);
                if (code.Length == 0)
                    continue;

                foreach (var field in GuardedFields)
                {
                    if (code.Contains(field, StringComparison.Ordinal))
                    {
                        offenders.Add($"{fileName}:{i + 1} 直读 {field} —— 请改用 Effective*/HasExplicit* 访问器；" +
                                      "确有需要时先在 MethodAnalysisResult 上补语义化访问器");
                    }
                }
            }
        }

        return offenders;
    }

    /// <summary>
    /// 守卫自身的健全性检查：白名单文件里**确实**存在这些字段（否则守卫可能因重命名而恒真）。
    /// </summary>
    [Fact]
    public void GuardedFields_StillExistInModel()
    {
        var modelPath = EnumerateGeneratorSources()
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), "MethodAnalysisResult.cs", StringComparison.OrdinalIgnoreCase));
        modelPath.Should().NotBeNull("模型文件必须存在");

        var modelText = File.ReadAllText(modelPath!);
        foreach (var field in GuardedFields)
        {
            modelText.Should().Contain(field,
                $"守卫监控的字段 {field} 必须仍然存在于模型中；若已重命名，请同步更新本守卫");
        }
    }

    /// <summary>剥离整行注释与行尾注释（本仓库注释均以 <c>//</c> 起始）。</summary>
    private static string StripComments(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
            return string.Empty;

        var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
        return commentIndex >= 0 ? line.Substring(0, commentIndex) : line;
    }

    /// <summary>生成器源码全部 *.cs 文件路径（排除 obj/bin）。</summary>
    private static IEnumerable<string> EnumerateGeneratorSources()
    {
        var root = Path.GetFullPath(GeneratorRoot);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;
            yield return file;
        }
    }
}
