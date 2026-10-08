// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// SDK 工具生成剖面标记接口。
/// </summary>
/// <remarks>
/// <para>
/// 实现本接口 = 声明「当前类型是一个工具 Schema 生成剖面」，且<b>必须</b>同时标注
/// <c>[SdkToolProfile]</c>（位于 <c>Mud.HttpUtils.Attributes</c>）。成对性由编译期守卫诊断
/// <c>SDKT001</c>（<c>Mud.HttpUtils.Generator</c> 的 <c>ToolSurface/ProfileContractGuard</c>）强制。
/// </para>
/// <para>
/// <b>为什么是「空标记接口 + 数据特性」而非「接口带 get-only 属性」</b>：源生成器只能读
/// <c>IFieldSymbol.ConstantValue</c> 或 <c>AttributeData</c>，<b>不能执行属性 getter</b>。
/// 用特性承载常量 = 编译器可读 + 消费方可写 + 无运行时语义泄漏（设计文档 §4.1）。
/// 本接口因此没有任何成员，仅承担「类型即剖面」的编译期声明职责与守卫锚点。
/// </para>
/// <para>
/// 剖面槽位清单（全部由 <c>[SdkToolProfile]</c> 承载）见设计文档 §4.2，共 17 组命名事实槽，
/// 覆盖上游 <c>Mud.Feishu.AI.Tools</c> 引擎源码中的全部 SDK 命名字面量。
/// </para>
/// </remarks>
public interface ISdkToolProfile
{
}
