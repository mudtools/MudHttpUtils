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
namespace Mud.HttpUtils.Attributes;

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
