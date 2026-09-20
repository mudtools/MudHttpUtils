// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Mud.HttpUtils;

/// <summary>
/// G7-06：生成器的「配置值快照」。
/// </summary>
/// <remarks>
/// <para>
/// 原管道把引用型 <see cref="AnalyzerConfigOptionsProvider"/> 直接 <c>Combine</c> 进增量图：
/// IDE 每次编译即使配置值不变，只要 Provider 实例被重建就会使下游全部 <c>Modified</c>（缓存全量失效）。
/// 本快照把影响生成内容的全部关键配置值抽成 <b>值相等</b> 输入：值不变即 <c>Cached</c>，
/// 配置值真正变化时才 <c>Modified</c>——与 05/06 册的 salt 设计（值化失效信号）一致。
/// </para>
/// <para>
/// 类不可变（全部字段 <c>init</c>）并实现值相等；不采用 <c>record</c> 以免依赖
/// <c>System.Runtime.CompilerServices.IsExternalInit</c>（netstandard2.0 目标未注入该类型）。
/// </para>
/// </remarks>
internal sealed class GeneratorConfigSnapshot : IEquatable<GeneratorConfigSnapshot>
{
    /// <summary>初始化快照。</summary>
    public GeneratorConfigSnapshot(
        bool disable,
        string optionsName,
        bool nullableEnable,
        bool emitMarkers,
        AotRuntimeMode aotMode,
        bool force,
        string version)
    {
        Disable = disable;
        OptionsName = optionsName;
        NullableEnable = nullableEnable;
        EmitMarkers = emitMarkers;
        AotMode = aotMode;
        Force = force;
        Version = version;
    }

    /// <summary>T5.3：全局禁用开关。</summary>
    public bool Disable { get; }

    /// <summary>HttpClient 选项类型名（build_property.HttpClientOptionsName）。</summary>
    public string OptionsName { get; }

    /// <summary>消费项目 nullable 是否启用（build_property.Nullable == "enable"）。</summary>
    public bool NullableEnable { get; }

    /// <summary>是否发射 [GeneratedCode] 标注（build_property.MudEmitGeneratedCodeMarkers）。</summary>
    public bool EmitMarkers { get; }

    /// <summary>运行期 AOT 模式（三态判定）。</summary>
    public AotRuntimeMode AotMode { get; }

    /// <summary>逃生舱开关（build_property.ForceHttpGenerator）。</summary>
    public bool Force { get; }

    /// <summary>生成器版本号（与 salt 的 E-3 失效语义一致）。</summary>
    public string Version { get; }

    /// <summary>值相等比较器（与 <c>Combine</c> 的 <c>WithComparer</c> 配套）。</summary>
    public static IEqualityComparer<GeneratorConfigSnapshot> Comparer { get; } =
        new GeneratorConfigSnapshotEqualityComparer();

    /// <inheritdoc />
    public bool Equals(GeneratorConfigSnapshot? other) =>
        other != null
        && Disable == other.Disable
        && string.Equals(OptionsName, other.OptionsName, StringComparison.Ordinal)
        && NullableEnable == other.NullableEnable
        && EmitMarkers == other.EmitMarkers
        && AotMode == other.AotMode
        && Force == other.Force
        && string.Equals(Version, other.Version, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GeneratorConfigSnapshot other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + Disable.GetHashCode();
            hash = (hash * 31) + (OptionsName?.GetHashCode() ?? 0);
            hash = (hash * 31) + NullableEnable.GetHashCode();
            hash = (hash * 31) + EmitMarkers.GetHashCode();
            hash = (hash * 31) + AotMode.GetHashCode();
            hash = (hash * 31) + Force.GetHashCode();
            hash = (hash * 31) + (Version?.GetHashCode() ?? 0);
            return hash;
        }
    }

    /// <summary>
    /// 从分析器配置提供器构建快照（<c>Select</c> 阶段执行，读取全部影响生成内容的 GlobalOptions）。
    /// </summary>
    public static GeneratorConfigSnapshot Create(AnalyzerConfigOptionsProvider provider)
    {
        var g = provider.GlobalOptions;
        return new GeneratorConfigSnapshot(
            ProjectConfigHelper.ReadConfigValueAsBool(g, "build_property.DisableMudSourceGenerator", false),
            ProjectConfigHelper.ReadConfigValue(g, "build_property.HttpClientOptionsName", "HttpClientOptions")
                ?? "HttpClientOptions",
            ProjectConfigHelper.ReadConfigValue(g, "build_property.Nullable", "enable") == "enable",
            ProjectConfigHelper.ReadConfigValueAsBool(g, "build_property.MudEmitGeneratedCodeMarkers", true),
            AotModeResolver.Resolve(g),
            ProjectConfigHelper.ReadConfigValueAsBool(g, "build_property.ForceHttpGenerator", false),
            GeneratedCodeConsts.GeneratorVersion);
    }

    /// <summary>快照值相等比较器（委托给 <see cref="IEquatable{T}.Equals"/>）。</summary>
    private sealed class GeneratorConfigSnapshotEqualityComparer : IEqualityComparer<GeneratorConfigSnapshot>
    {
        public bool Equals(GeneratorConfigSnapshot? x, GeneratorConfigSnapshot? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null)
                return false;
            return x.Equals(y);
        }

        public int GetHashCode(GeneratorConfigSnapshot obj) => obj.GetHashCode();
    }
}