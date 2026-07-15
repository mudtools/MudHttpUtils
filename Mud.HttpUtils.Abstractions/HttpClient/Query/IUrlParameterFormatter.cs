// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任。
// -----------------------------------------------------------------------

using System;
using System.Reflection;

namespace Mud.HttpUtils;

/// <summary>
/// URL 参数值格式化器抽象。允许运行时自定义参数值的字符串格式化逻辑。
/// </summary>
/// <remarks>
/// <para>
/// 此抽象用于将参数值（枚举、日期时间、数字等）格式化为 URL 查询参数字符串。
/// 替代 <c>QueryMapHelper</c> 的反射路径，提供可注入的格式化策略。
/// </para>
/// <para>
/// <b>Native AOT 注意</b>：默认实现 <c>DefaultUrlParameterFormatter</c> 使用反射读取枚举特性和
/// <c>[Query]</c> 特性，在 AOT 下不兼容。AOT 场景应通过源生成器在编译期生成格式化代码。
/// </para>
/// </remarks>
public interface IUrlParameterFormatter
{
    /// <summary>
    /// 将参数值格式化为字符串。
    /// </summary>
    /// <param name="value">参数值。</param>
    /// <param name="attributeProvider">特性提供者（如 <see cref="ParameterInfo"/>），用于读取 <c>[Query]</c> 特性。可为 null。</param>
    /// <param name="type">参数值的类型。</param>
    /// <returns>格式化后的字符串；如果值为 null 则返回 null。</returns>
    string? Format(object? value, ICustomAttributeProvider? attributeProvider, Type type);
}
