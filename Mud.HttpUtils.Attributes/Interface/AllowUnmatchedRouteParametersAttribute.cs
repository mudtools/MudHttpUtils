// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 标记接口允许路由模板中存在未匹配的 <c>{token}</c> 占位符（编译期决策）。
/// </summary>
/// <remarks>
/// <para>
/// 在 Mud.HttpUtils 中，此特性作为<strong>编译期决策</strong>：源生成器读取此特性后，
/// 在生成的 URL 构建代码中<strong>跳过未匹配占位符的替换</strong>（保留 <c>{token}</c> 字面量），
/// 允许后续通过 <c>DelegatingHandler</c> 或拦截器重写。
/// </para>
/// <para>
/// 未标记此特性时，未匹配的占位符将触发编译器警告或异常（默认行为不变）。
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = true)]
public sealed class AllowUnmatchedRouteParametersAttribute : Attribute
{
}
