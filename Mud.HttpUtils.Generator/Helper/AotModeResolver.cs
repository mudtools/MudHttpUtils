// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// [F10 修复] 运行期 AOT 模式。
/// <para>语义：<c>IsAotCompatible</c> 是「启用 AOT/裁剪分析器」的编译期开关（类库为获得 IL 警告而设置），
/// 不表示产物以 Native AOT 发布。原实现以 <c>IsAotCompatible || PublishAot</c> 判定，导致 JIT 部署的库项目在
/// <c>IsAotCompatible=true</c> 时被误判为 AOT——XML 方法直接 Error 阻断构建。</para>
/// </summary>
internal enum AotRuntimeMode
{
    /// <summary>运行期 JIT（默认）。</summary>
    Jit,
    /// <summary>运行期 Native AOT。</summary>
    Aot,
}

/// <summary>
/// [F10] Native AOT 运行期模式的集中判定。
/// <para>优先级：<c>MudAotRuntimeMode</c> 显式配置 &gt; <c>PublishAot</c> &gt; <c>IsAotCompatible</c>（仅降级提示）。</para>
/// <para>
/// [P2-5] 语义漂移防线：本类的三态判定语义在以下位置被复刻，修改时必须同步：
/// <list type="bullet">
/// <item><c>Mud.HttpUtils.Attributes/build/Mud.HttpUtils.JsonContextScaffolder.targets</c> — MSBuild DSL 复刻 <c>MudEnableJsonContextScaffolder</c> 判定</item>
/// <item><c>.github/workflows/ci.yml</c> — aot-publish / aot-packageref 两个作业的探针用例</item>
/// </list>
/// 改动判定语义时，必须同步 targets 的 <c>MudEnableJsonContextScaffolder</c> 条件与 ci.yml 探针用例。
/// </para>
/// </summary>
internal static class AotModeResolver
{
    private const string BuildPropertyMudAotRuntimeMode = "build_property.MudAotRuntimeMode";
    private const string BuildPropertyPublishAot = "build_property.PublishAot";
    private const string BuildPropertyIsAotCompatible = "build_property.IsAotCompatible";

    /// <summary>
    /// 判定「运行期是否为 Native AOT」。
    /// </summary>
    public static AotRuntimeMode Resolve(AnalyzerConfigOptions globalOptions)
    {
        var explicitMode = ProjectConfigHelper.ReadConfigValue(globalOptions, BuildPropertyMudAotRuntimeMode);
        if (string.Equals(explicitMode, "aot", StringComparison.OrdinalIgnoreCase)) return AotRuntimeMode.Aot;
        if (string.Equals(explicitMode, "jit", StringComparison.OrdinalIgnoreCase)) return AotRuntimeMode.Jit;

        return ProjectConfigHelper.ReadConfigValueAsBool(globalOptions, BuildPropertyPublishAot, false)
            ? AotRuntimeMode.Aot
            : AotRuntimeMode.Jit;
    }

    /// <summary>
    /// 是否处于「AOT 分析器已启用但运行期未声明」的模糊态（用于降级提示，不阻断）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// [F11 修复] 判定为真的条件：<c>IsAotCompatible=true</c>（启用 AOT/裁剪分析器）且运行期未确认为 AOT，
    /// <b>且</b>用户未显式声明运行期模式。
    /// </para>
    /// <para>
    /// 历史缺陷（本轮核验发现）：原实现仅判 <c>IsAotCompatible &amp;&amp; Resolve()==Jit</c>，
    /// 而显式 <c>MudAotRuntimeMode=jit</c> 也满足该条件（显式 jit 同样解析为 Jit）——
    /// 即「用户已显式声明以 JIT 发布」仍会持续收到降级提示，缺少正大光明的关闭方式
    /// （只能关掉 <c>IsAotCompatible</c> 或抑制诊断）。现把显式 <c>jit</c> 视为用户已承担运行期语义，
    /// 与 <see cref="Resolve"/> 中「显式 jit 否决 PublishAot」的既有口径对齐。
    /// </para>
    /// </remarks>
    public static bool IsAotAnalyzerOnly(AnalyzerConfigOptions globalOptions)
    {
        if (!ProjectConfigHelper.ReadConfigValueAsBool(globalOptions, BuildPropertyIsAotCompatible, false))
            return false;

        var explicitMode = ProjectConfigHelper.ReadConfigValue(globalOptions, BuildPropertyMudAotRuntimeMode);
        if (string.Equals(explicitMode, "jit", StringComparison.OrdinalIgnoreCase))
            return false;

        return Resolve(globalOptions) == AotRuntimeMode.Jit;
    }
}