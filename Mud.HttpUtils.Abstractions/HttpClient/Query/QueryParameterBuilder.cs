// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 查询参数构建器，用于安全地构建 URL 查询字符串。
/// </summary>
/// <remarks>
/// 初始化一个新的 <see cref="QueryParameterBuilder"/> 实例，并设置基础 URL。
/// </remarks>
/// <param name="baseUrl">基础 URL</param>
public sealed class QueryParameterBuilder(string baseUrl)
{
    private readonly List<KeyValuePair<string, string>> _params = new();
    private readonly string _baseUrl = baseUrl ?? string.Empty;
    private string? _cachedQueryString;

    /// <summary>
    /// 获取当前查询参数的数量。
    /// </summary>
    public int Count => _params.Count;

    /// <summary>
    /// 初始化一个新的 <see cref="QueryParameterBuilder"/> 实例，基础 URL 为空。
    /// </summary>
    public QueryParameterBuilder() : this(string.Empty) { }

    /// <summary>
    /// 创建一个新的 <see cref="QueryParameterBuilder"/> 实例。
    /// </summary>
    /// <param name="baseUrl">基础 URL</param>
    /// <returns></returns>
    public static QueryParameterBuilder Create(string baseUrl = "") => new(baseUrl);

    /// <summary>
    /// 添加查询参数（空值将被忽略）。
    /// </summary>
    public void Add(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        if (string.IsNullOrWhiteSpace(value))
            return;

        _params.Add(new KeyValuePair<string, string>(name, value!));
        _cachedQueryString = null; // 清除缓存
    }

    /// <summary>
    /// 获取指定名称的最后一个参数值。
    /// </summary>
    public string? this[string name]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            for (int i = _params.Count - 1; i >= 0; i--)
            {
                if (_params[i].Key == name)
                    return _params[i].Value;
            }
            return null;
        }
    }

    /// <summary>
    /// 获取指定名称的所有参数值。
    /// </summary>
    public IEnumerable<string> GetValues(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        return _params.Where(p => p.Key == name).Select(p => p.Value);
    }

    /// <summary>
    /// 构建完整的 URL（包含基础 URL 和查询字符串）。
    /// </summary>
    public string Build()
    {
        if (_cachedQueryString != null)
            return _cachedQueryString;

        var query = BuildQueryString();
        if (string.IsNullOrEmpty(_baseUrl))
        {
            _cachedQueryString = query;
            return _cachedQueryString;
        }

        // 使用 UriBuilder 安全拼接
        var uriBuilder = new UriBuilder(_baseUrl);
        if (!string.IsNullOrEmpty(query))
        {
            uriBuilder.Query = string.IsNullOrEmpty(uriBuilder.Query)
                ? query
                : uriBuilder.Query.TrimStart('?') + "&" + query;
        }

        _cachedQueryString = uriBuilder.Uri.ToString();
        return _cachedQueryString;
    }

    /// <summary>
    /// 仅生成查询字符串（key1=value1&amp;key2=value2）。
    /// </summary>
    private string BuildQueryString()
    {
        if (_params.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        for (int i = 0; i < _params.Count; i++)
        {
            if (i > 0)
                sb.Append('&');
            sb.Append(Uri.EscapeDataString(_params[i].Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(_params[i].Value));
        }
        return sb.ToString();
    }

    /// <summary>
    /// 调试用（不建议用于业务逻辑）。
    /// </summary>
    public override string ToString() => Build();
}
