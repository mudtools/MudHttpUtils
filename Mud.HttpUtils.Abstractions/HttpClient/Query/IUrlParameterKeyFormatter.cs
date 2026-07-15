// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// URL 参数键格式化器抽象（命名约定转换）。
/// </summary>
/// <remarks>
/// 用于将参数键（属性名/参数名）转换为最终的 URL 参数名，如 PascalCase → camelCase。
/// 与 <see cref="IUrlParameterFormatter"/>（值格式化）配合使用。
/// </remarks>
public interface IUrlParameterKeyFormatter
{
    /// <summary>
    /// 将参数键格式化为最终名称。
    /// </summary>
    /// <param name="key">原始键名。</param>
    /// <returns>格式化后的键名。</returns>
    string Format(string key);
}
