// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 查询参数序列化方法
/// </summary>
public enum QuerySerializationMethod
{
    /// <summary>
    /// 使用 ToString() 序列化（默认）
    /// </summary>
    ToString,

    /// <summary>
    /// 使用 JSON 序列化
    /// </summary>
    Json
}

/// <summary>
/// 标记参数作为查询参数映射，将对象的所有属性展开为查询参数。
/// </summary>
/// <remarks>
/// <para>
/// 应用于方法参数，将参数对象的所有公共属性展开为 URL 查询参数。
/// 参数类型应实现 <see cref="IQueryParameter"/> 接口，或为普通 POCO 对象。
/// </para>
/// <para>
/// 对于嵌套属性，默认使用下划线分隔（如 User_Name），可通过 <see cref="PropertySeparator"/> 自定义分隔符。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class SearchCriteria
/// {
///     public string? Keyword { get; set; }
///     public int Page { get; set; }
///     public int PageSize { get; set; }
/// }
/// 
/// [Get("/api/search")]
/// Task&lt;SearchResult&gt; SearchAsync([QueryMap] SearchCriteria criteria);
/// 
/// // 调用: api.SearchAsync(new SearchCriteria { Keyword = "test", Page = 1, PageSize = 10 });
/// // 生成: /api/search?Keyword=test&amp;Page=1&amp;PageSize=10
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false)]
public sealed class QueryMapAttribute : Attribute
{
    /// <summary>
    /// 获取或设置属性名称分隔符，用于嵌套属性。
    /// </summary>
    /// <value>默认为 "_"（下划线）。</value>
    public string PropertySeparator { get; set; } = "_";

    /// <summary>
    /// 获取或设置查询参数的序列化方法。
    /// </summary>
    /// <value>默认为 <see cref="QuerySerializationMethod.ToString"/>。</value>
    public QuerySerializationMethod SerializationMethod { get; set; } = QuerySerializationMethod.ToString;

    /// <summary>
    /// 获取或设置一个值，该值指示是否对查询参数值进行 URL 编码。
    /// </summary>
    /// <value>默认为 true（启用 URL 编码）。</value>
    public bool UrlEncode { get; set; } = true;

    /// <summary>
    /// 获取或设置一个值，该值指示是否包含值为 null 的属性。
    /// </summary>
    /// <value>默认为 false（不包含 null 值属性）。</value>
    public bool IncludeNullValues { get; set; }
}
