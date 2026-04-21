// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 查询参数接口，用于将复杂对象转换为 URL 查询参数
/// </summary>
/// <remarks>
/// <para>
/// 实现此接口可以使您的查询参数类型在 AOT (Ahead-of-Time) 编译环境下正常工作，
/// 避免使用反射导致的 AOT 兼容性问题。
/// </para>
/// <para>
/// 使用示例：
/// </para>
/// <code>
/// public class SearchCriteria : IQueryParameter
/// {
///     public string? Keyword { get; set; }
///     public int PageIndex { get; set; }
///     public int PageSize { get; set; }
///     
///     public IEnumerable&lt;KeyValuePair&lt;string, string?&gt;&gt; ToQueryParameters()
///     {
///         yield return new KeyValuePair&lt;string, string?&gt;("keyword", Keyword);
///         yield return new KeyValuePair&lt;string, string?&gt;("pageIndex", PageIndex.ToString());
///         yield return new KeyValuePair&lt;string, string?&gt;("pageSize", PageSize.ToString());
///     }
/// }
/// </code>
/// </remarks>
public interface IQueryParameter
{
    /// <summary>
    /// 将当前对象转换为 URL 查询参数键值对集合
    /// </summary>
    /// <returns>包含查询参数名称和值的键值对集合</returns>
    IEnumerable<KeyValuePair<string, string?>> ToQueryParameters();
}
