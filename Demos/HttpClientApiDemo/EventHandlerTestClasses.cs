// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace CodeGeneratorTest.EventHandlers;

/// <summary>
/// 修改人员类型名称事件处理器
/// <para>当应用订阅该事件后，若果更新了人员类型的选项内容...</para>
/// <para>事件类型:contact.employee_type_enum.updated_v3</para>
/// <para>文档地址：<see href="https://open.feishu.cn/document/..."/> </para>
/// </summary>
[GenerateEventHandler(EventType = EmployeeTypeEnum.UserCreated,
             InheritedFrom = "DefaultFeishuEventHandler")]
public class EmployeeTypeEnumUpdateResult : IEventResult
{
    [JsonPropertyName("old_enum")]
    public EmployeeTypeEnum? OldEnum { get; set; }

    [JsonPropertyName("new_enum")]
    public EmployeeTypeEnum? NewEnum { get; set; }
}

/// <summary>
/// 员工类型枚举
/// </summary>
public class EmployeeTypeEnum
{
    public const string UserCreated = "contact.user.created_v3";

    /// <summary>
    /// 全职员工
    /// </summary>
    public const string FullTime = "FullTime";

    /// <summary>
    /// 兼职员工
    /// </summary>
    public const string PartTime = "PartTime";

    /// <summary>
    /// 实习生
    /// </summary>
    public const string Intern = "Intern";
}

/// <summary>
/// 事件结果接口
/// </summary>
public interface IEventResult
{
}

/// <summary>
/// 默认事件处理器基类（用于测试）
/// </summary>
/// <typeparam name="TEventResult">事件结果类型</typeparam>
public abstract class DefaultFeishuEventHandler<TEventResult> where TEventResult : IEventResult
{
    protected readonly ILogger _logger;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="logger">日志记录器</param>
    protected DefaultFeishuEventHandler(IFeishuEventDeduplicator businessDeduplicator, ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 支持的事件类型
    /// </summary>
    public abstract string SupportedEventType { get; }
}

/// <summary>
/// 简单的事件结果类，用于测试默认命名规则
/// </summary>
[GenerateEventHandler(EventType = "SimpleEvent.Test", InheritedFrom = "DefaultFeishuEventHandler")]
public class SimpleEventResult : IEventResult
{
    public string? Message { get; set; }
    public DateTime Timestamp { get; set; }
}