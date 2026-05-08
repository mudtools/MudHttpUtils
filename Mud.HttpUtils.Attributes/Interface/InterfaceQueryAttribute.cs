// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 在接口级别添加固定的查询参数。
/// </summary>
/// <remarks>
/// <para>
/// 应用于接口，为该接口的所有方法自动添加指定的查询参数。
/// 这对于需要统一传递 API 版本号、客户端标识等固定参数的场景非常有用。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HttpClientApi]
/// [InterfaceQuery("api_version", "2.0")]
/// [InterfaceQuery("client_id", "my-app")]
/// public interface IUserApi
/// {
///     [Get("/api/users/{id}")]
///     Task&lt;User&gt; GetUserAsync(int id);
///     // 实际请求: /api/users/1?api_version=2.0&amp;client_id=my-app
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = true)]
public sealed class InterfaceQueryAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="InterfaceQueryAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="name">查询参数的名称。</param>
    /// <param name="value">查询参数的值。</param>
    public InterfaceQueryAttribute(string name, string? value)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Value = value;
    }

    /// <summary>
    /// 获取查询参数的名称。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 获取查询参数的值。
    /// </summary>
    public string? Value { get; }
}
