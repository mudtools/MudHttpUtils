namespace Mud.HttpUtils;

/// <summary>
/// 查询参数接口，用于将对象转换为 HTTP 查询参数键值对。
/// </summary>
public interface IQueryParameter
{
    /// <summary>
    /// 将对象转换为查询参数键值对集合。
    /// </summary>
    /// <returns>包含查询参数的键值对 enumerable 集合。</returns>
    IEnumerable<KeyValuePair<string, string?>> ToQueryParameters();
}
