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
    /// 是否处于「AOT 分析器已启用但运行期未必 AOT」的模糊态（仅用于降级提示，不阻断）。
    /// </summary>
    public static bool IsAotAnalyzerOnly(AnalyzerConfigOptions globalOptions)
        => ProjectConfigHelper.ReadConfigValueAsBool(globalOptions, BuildPropertyIsAotCompatible, false)
           && Resolve(globalOptions) == AotRuntimeMode.Jit;
}