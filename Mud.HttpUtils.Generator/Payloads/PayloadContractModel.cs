// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Mud.HttpUtils.Models.Payloads;

/// <summary>
/// 单个字段的映射模型（已解析出可直接渲染的右值表达式）。
/// </summary>
internal sealed class PayloadFieldModel
{
    public PayloadFieldModel(string element, string propertyName, string propertyTypeDisplay, string resolvedCall, Location location)
    {
        Element = element;
        PropertyName = propertyName;
        PropertyTypeDisplay = propertyTypeDisplay;
        ResolvedCall = resolvedCall;
        Location = location;
    }

    /// <summary>源端元素名。</summary>
    public string Element { get; }

    /// <summary>目标属性名。</summary>
    public string PropertyName { get; }

    /// <summary>目标属性类型（<c>global::</c> 全限定显示，仅用于指纹与诊断文案）。</summary>
    public string PropertyTypeDisplay { get; }

    /// <summary>预渲染的右值表达式（含 <c>global::</c> 全限定的转换器调用）。</summary>
    public string ResolvedCall { get; }

    /// <summary>声明位置（用于诊断定位）。</summary>
    public Location Location { get; }
}

/// <summary>
/// 模型期已确定的诊断（<c>Execute</c> 阶段原样报告）。
/// </summary>
/// <remarks>
/// 诊断在 <c>Transform</c> 期生成：该阶段持有语义模型（可解析转换器方法），
/// 但<b>不能</b>报告诊断（无 <see cref="SourceProductionContext"/>）；故随模型携带至执行期。
/// </remarks>
internal sealed class PayloadDiagnostic
{
    public PayloadDiagnostic(DiagnosticDescriptor descriptor, Location location, string className, string message)
    {
        Descriptor = descriptor;
        Location = location;
        ClassName = className;
        Message = message;
    }

    /// <summary>诊断描述符。</summary>
    public DiagnosticDescriptor Descriptor { get; }

    /// <summary>定位（优先属性语法节点，其次类型节点）。</summary>
    public Location Location { get; }

    /// <summary>消息实参 0（类名）。</summary>
    public string ClassName { get; }

    /// <summary>消息实参 1（细节与修复建议）。</summary>
    public string Message { get; }
}

/// <summary>
/// 载荷契约模型（增量管线的等价单元）。
/// </summary>
/// <remarks>
/// <para>
/// 等价性以 <see cref="Fingerprint"/> 为准（照 <c>Models.InterfaceModel</c> 的既有范式）：
/// 指纹覆盖渲染产物所依赖的<b>全部</b>输入（类型、契约参数、逐字段元素名/属性名/预渲染右值/诊断），
/// 因此「指纹相同 ⇒ 渲染结果相同」，可安全地以指纹做增量比较。
/// </para>
/// <para>
/// 目的：改 A 类不触发 B 类重生成（<c>WithComparer</c> + 逐项注册点）；
/// 改字段顺序、元素名、转换方法名则必然触发<b>本类</b>重生成。
/// </para>
/// </remarks>
internal sealed class PayloadContractModel : IEquatable<PayloadContractModel>
{
    public PayloadContractModel(
        string? ns,
        string typeName,
        string typeDisplay,
        string hintName,
        string? contractId,
        string? scopeFallback,
        string? converterDisplay,
        ImmutableArray<PayloadFieldModel> fields,
        ImmutableArray<PayloadDiagnostic> diagnostics,
        Location location,
        string fingerprint)
    {
        Namespace = ns;
        TypeName = typeName;
        TypeDisplay = typeDisplay;
        HintName = hintName;
        ContractId = contractId;
        ScopeFallback = scopeFallback;
        ConverterDisplay = converterDisplay;
        Fields = fields;
        Diagnostics = diagnostics;
        Location = location;
        Fingerprint = fingerprint;
    }

    /// <summary>命名空间（<see langword="null"/> = 全局命名空间）。</summary>
    public string? Namespace { get; }

    /// <summary>类简单名（不支持泛型与嵌套，故无 arity 与容器链）。</summary>
    public string TypeName { get; }

    /// <summary>类全限定显示名（<c>global::</c> 前缀，用于渲染泛型实参与属性类型）。</summary>
    public string TypeDisplay { get; }

    /// <summary>生成文件的 hintName。</summary>
    public string HintName { get; }

    /// <summary>契约标识（<see langword="null"/> = 渲染期回退为类名）。</summary>
    public string? ContractId { get; }

    /// <summary>作用域回退节点名。</summary>
    public string? ScopeFallback { get; }

    /// <summary>转换器全限定显示名（<c>global::</c> 前缀；<see langword="null"/> = 未指定）。</summary>
    public string? ConverterDisplay { get; }

    /// <summary>字段（声明序）。</summary>
    public ImmutableArray<PayloadFieldModel> Fields { get; }

    /// <summary>模型期已定的诊断。</summary>
    public ImmutableArray<PayloadDiagnostic> Diagnostics { get; }

    /// <summary>类声明位置（兜底异常诊断的定位）。</summary>
    public Location Location { get; }

    /// <summary>值相等依据。</summary>
    public string Fingerprint { get; }

    /// <summary>是否存在 Error 级诊断（有则不产出生成文件，避免「半成品」误导）。</summary>
    public bool HasErrors
    {
        get
        {
            for (var i = 0; i < Diagnostics.Length; i++)
            {
                if (Diagnostics[i].Descriptor.DefaultSeverity == DiagnosticSeverity.Error)
                    return true;
            }

            return false;
        }
    }

    /// <inheritdoc/>
    public bool Equals(PayloadContractModel? other) =>
        other != null && string.Equals(Fingerprint, other.Fingerprint, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PayloadContractModel other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        Fingerprint.Length > 0 ? StringComparer.Ordinal.GetHashCode(Fingerprint) : 0;
}
