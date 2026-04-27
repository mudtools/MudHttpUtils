// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记类以生成事件处理器（Event Handler）代码。
/// </summary>
/// <remarks>
/// <para>
/// 应用于类上，指示源代码生成器为此类生成事件处理器实现。
/// 通常用于处理 Webhook 或事件驱动的场景，如飞书事件订阅等。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [GenerateEventHandler(EventType = "message")]
/// public class MessageEventHandler : IEventHandler
/// {
///     public Task HandleAsync(EventContext context)
///     {
///         // 处理消息事件
///         return Task.CompletedTask;
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class GenerateEventHandlerAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="GenerateEventHandlerAttribute"/> 类的新实例。
    /// </summary>
    public GenerateEventHandlerAttribute()
    {
    }

    /// <summary>
    /// 初始化 <see cref="GenerateEventHandlerAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="eventType">事件类型标识符。</param>
    public GenerateEventHandlerAttribute(string? eventType)
    {
        EventType = eventType;
    }

    /// <summary>
    /// 获取或设置生成的处理器类名称。
    /// </summary>
    public string? HandlerClassName { get; set; }

    /// <summary>
    /// 获取或设置生成的处理器类所在的命名空间。
    /// </summary>
    public string? HandlerNamespace { get; set; }

    /// <summary>
    /// 获取或设置继承来源，用于标识此处理器继承自哪个基类。
    /// </summary>
    public string? InheritedFrom { get; set; }

    /// <summary>
    /// 获取或设置事件类型标识符。
    /// </summary>
    public string? EventType { get; set; }

    /// <summary>
    /// 获取或设置构造函数参数字符串，用于生成构造函数签名。
    /// </summary>
    public string? ConstructorParameters { get; set; }

    /// <summary>
    /// 获取或设置构造函数基类调用字符串，用于生成 base() 调用。
    /// </summary>
    public string? ConstructorBaseCall { get; set; }

    /// <summary>
    /// 获取或设置请求头类型，用于反序列化事件请求头。
    /// </summary>
    public string? HeaderType { get; set; }
}
