// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 事件处理器代码生成特性
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class GenerateEventHandlerAttribute : Attribute
{
    /// <summary>
    /// 默认构造函数
    /// </summary>
    public GenerateEventHandlerAttribute()
    {
    }

    /// <summary>
    /// 默认构造函数
    /// </summary>
    /// <param name="eventType">事件处理器类型</param>
    public GenerateEventHandlerAttribute(string? eventType)
    {
        EventType = eventType;
    }

    /// <summary>
    /// 事件处理器类名。
    /// </summary>
    public string? HandlerClassName { get; set; }

    /// <summary>
    /// 事件处理器类所属命名空间。
    /// </summary>
    public string? HandlerNamespace { get; set; }

    /// <summary>
    /// 事件处理器类继承的父类。
    /// </summary>
    public string? InheritedFrom { get; set; }

    /// <summary>
    /// 事件处理器类型。
    /// </summary>
    public string? EventType { get; set; }

    /// <summary>
    /// 构造函数参数定义，格式："Type1 param1,Type2 param2"。
    /// <para>示例: "IFeishuEventDeduplicator businessDeduplicator,ILogger logger"</para>
    /// <para>默认值: "IFeishuEventDeduplicator businessDeduplicator,ILogger logger"</para>
    /// </summary>
    public string? ConstructorParameters { get; set; }

    /// <summary>
    /// 构造函数基类调用参数，格式："param1,param2"。
    /// <para>示例: "businessDeduplicator,logger"</para>
    /// <para>默认值: "businessDeduplicator,logger"</para>
    /// </summary>
    public string? ConstructorBaseCall { get; set; }

    /// <summary>
    /// 事件头类型。
    /// <para>当设置此属性时，生成的类将继承自 IdempotentFeishuEventHandler&lt;TResult, THeader&gt;。</para>
    /// <para>示例: "FeishuEventHeaderV2"</para>
    /// </summary>
    public string? HeaderType { get; set; }
}
