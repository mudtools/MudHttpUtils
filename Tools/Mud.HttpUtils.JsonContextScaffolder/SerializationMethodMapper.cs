// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Attributes;

namespace Mud.HttpUtils.JsonContextScaffolder;

/// <summary>
/// <see cref="SerializationMethod"/> 枚举 → 契约名称映射（脚手架工具内部辅助）。
/// </summary>
/// <remarks>
/// [A-2] 历史上脚手架硬编码 <c>int → 名称</c> 映射表，与枚举声明<b>顺序耦合</b>——
/// 成员重排/新增即静默分叉。本映射按<b>枚举成员</b>锚定（成员改名/删除即编译失败），
/// 消除顺序耦合。归属脚手架工具内部而非 Attributes 包：Attributes 为纯元数据程序集
///（仅 Attribute 类与枚举，零运行时行为），且唯一消费方是脚手架本身
///（生成器侧为 <c>MethodAnalyzer.ReadSerializationMethodName</c> 内部入口，语义同为名称锚定）。
/// </remarks>
internal static class SerializationMethodMapper
{
    /// <summary>
    /// 返回枚举成员的契约名称（<c>Json</c> / <c>Xml</c> / <c>FormUrlEncoded</c>）；
    /// 未定义成员返回 <c>null</c>（与脚手架"未知值跳过注册"语义一致）。
    /// </summary>
    public static string? ToSerializationMethodName(this SerializationMethod method) => method switch
    {
        SerializationMethod.Json => "Json",
        SerializationMethod.Xml => "Xml",
        SerializationMethod.FormUrlEncoded => "FormUrlEncoded",
        _ => null
    };
}
