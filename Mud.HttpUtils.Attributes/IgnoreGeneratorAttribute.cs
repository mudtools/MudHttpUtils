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
namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class IgnoreGeneratorAttribute : Attribute
{
}
