// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记接口或方法忽略源代码生成器的处理。
/// </summary>
/// <remarks>
/// <para>
/// [E-2] 支持面收敛为 <see cref="AttributeTargets.Interface"/> 与 <see cref="AttributeTargets.Method"/>
/// （决策 2026-09-13）。Property/Field 上的标注在当前实现下本就无效（无消费点），收窄后编译器以
/// CS0592 直接提示，比生成器诊断更准确。
/// </para>
/// <para>方法级：不生成该方法实现（同接口其他方法照常生成）。</para>
/// <para>接口级：生成器完全不介入——不生成实现类、不生成 DI 注册/工厂、不报该接口的 AOT/MUD 诊断；
/// 由使用方自行实现并注册。</para>
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
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
public sealed class IgnoreGeneratorAttribute : Attribute
{
}
