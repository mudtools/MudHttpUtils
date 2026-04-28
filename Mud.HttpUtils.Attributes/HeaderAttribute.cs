// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记参数、方法或接口作为 HTTP 请求头（Header）。
/// </summary>
/// <remarks>
/// <para>
/// 应用于方法参数、接口或方法上，指示将参数添加为 HTTP 请求头。支持自定义请求头名称、值和替换模式。
/// 可以在方法级别应用多次以添加多个固定请求头。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 参数作为请求头
/// [Get("/api/users")]
/// Task&lt;List&lt;User&gt;&gt; GetUsersAsync([Header("X-API-Key")] string apiKey);
/// 
/// // 方法级别添加固定请求头
/// [Get("/api/users")]
/// [Header("Accept", "application/json")]
/// Task&lt;List&lt;User&gt;&gt; GetUsersAsync();
/// 
/// // 使用别名和替换模式
/// [Post("/api/data")]
/// Task SendDataAsync([Header("Authorization", Replace = true)] string auth);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Parameter, AllowMultiple = true)]
public sealed class HeaderAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="HeaderAttribute"/> 类的新实例。
    /// </summary>
    public HeaderAttribute()
    {
    }

    /// <summary>
    /// 初始化 <see cref="HeaderAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="name">请求头的名称。</param>
    public HeaderAttribute(string name)
    {
        Name = name;
    }

    /// <summary>
    /// 初始化 <see cref="HeaderAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="name">请求头的名称。</param>
    /// <param name="value">请求头的值。</param>
    public HeaderAttribute(string name, object? value)
        : this(name) =>
        Value = value;

    /// <summary>
    /// 获取或设置请求头的名称。
    /// </summary>
    public string? Name { get; set; }

    private object? _field;

    /// <summary>
    /// 获取或设置请求头的值。
    /// </summary>
    /// <remarks>
    /// 设置此属性时会自动标记 <see cref="HasSetValue"/> 为 true。
    /// </remarks>
    public object? Value
    {
        get => _field;
        set
        {
            _field = value;
            HasSetValue = true;
        }
    }

    /// <summary>
    /// 获取或设置请求头的别名，用于映射到不同的请求头名称。
    /// </summary>
    public string? AliasAs { get; set; }

    /// <summary>
    /// 获取或设置一个值，该值指示是否替换已有的同名请求头。
    /// </summary>
    /// <value>默认为 false（追加模式）。</value>
    public bool Replace { get; set; }

    /// <summary>
    /// 获取或设置格式化字符串，用于格式化请求头值。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 支持以下格式化方式：
    /// <list type="bullet">
    /// <item>如果格式包含 {0}，则使用 string.Format 格式化</item>
    /// <item>如果参数实现 IFormattable，则调用 ToString(format, CultureInfo.InvariantCulture)</item>
    /// <item>否则调用 ToString()</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // GUID 格式化
    /// [Header("X-Request-Id", FormatString = "N")]
    /// 
    /// // 日期格式化
    /// [Header("X-Timestamp", FormatString = "yyyy-MM-ddTHH:mm:ssZ")]
    /// </code>
    /// </example>
    public string? FormatString { get; set; }

    /// <summary>
    /// 获取一个值，该值指示是否已设置 <see cref="Value"/> 属性。
    /// </summary>
    internal bool HasSetValue { get; private set; }
}
