// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记接口、方法、属性或字段忽略源代码生成器的处理。
/// </summary>
/// <remarks>
/// <para>
/// 应用于接口、方法、属性或字段上，指示源代码生成器在生成代码时应忽略该元素。
/// 通常用于在接口中保留某些成员不被自动生成实现，允许手动实现。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HttpClientApi]
/// public interface IUserApi
/// {
///     // 这个方法会被自动生成实现
///     [Get("/api/users/{id}")]
///     Task&lt;User&gt; GetUserAsync(int id);
///     
///     // 这个方法会被忽略，需要手动实现
///     [IgnoreGenerator]
///     Task&lt;User&gt; GetSpecialUserAsync(int id);
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class IgnoreGeneratorAttribute : Attribute
{
}
