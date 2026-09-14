// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

// 连坐抑制回归探针（方案 §1.2 / §3.3 / 附录 A）：
//
// 本接口同时包含
//   - 指针参数方法 → 触发生成器诊断 HTTPCLIENT004（Error，**已按 §3.3 去 NotConfigurable 标签**）；
//   - 缺少 HTTP 方法特性的方法 → 触发分析器诊断 MUD001（Error）。
//
// 历史现象（实测）：HTTPCLIENT004 带 NotConfigurable 标签时，csc 在声明阶段闸门
//   if (HasUnsuppressableErrors(diagnostics)) return;
// 提前返回，**分析器驱动永不执行** → MUD001 完全不可见（"连坐抑制"）。
// 去标签后 MUD001 与 HTTPCLIENT004 应同时呈现（级别仍为 Error，构建仍失败）。
//
// CI 以「生成器开启」构建本工程并断言：HTTPCLIENT004 与 MUD001 同时出现。
// 若有人把 NotConfigurable 标签加回 HTTPCLIENT004，MUD001 消失 → CI 步骤变红。
//
// 注意：探针构建必须开启 AllowUnsafeBlocks（见 csproj）。

using Mud.HttpUtils.Attributes;

namespace Mud.HttpUtils.AnalyzerOnlyProbe;

/// <summary>生成器诊断（Error）与分析器诊断（Error）共存时的可见性探针。</summary>
[HttpClientApi]
public interface ISuppressionProbeApi
{
    /// <summary>触发 <c>HTTPCLIENT004</c>（不支持的参数修饰符：指针）。</summary>
    [Get("/probe/pointer")]
    unsafe Task<string> PointerAsync(int* p);

    /// <summary>触发 <c>MUD001</c>（缺少 HTTP 方法特性）—— 修复前被 <c>HTTPCLIENT004</c> 连坐抑制。</summary>
    Task<string> MissingHttpMethodAsync();
}
