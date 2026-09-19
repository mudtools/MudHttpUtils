// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Globalization;

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
    /// 添加查询参数（null 将被忽略；空字符串是合法值，序列化为 <c>key=</c>）。
    /// </summary>
    /// <remarks>
    /// M2-#16A：<c>null</c> 与"空字符串"是两种语义 —— 前者表示"无值，跳过"，
    /// 后者是调用方显式提供的空值（生成代码在 <c>QueryMap(IncludeNullValues = true)</c>
    /// 时产出 <c>key=</c> 空串对），必须保留，否则该配置完全失效。
    /// </remarks>
    public void Add(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        if (value is null)
            return;

        _params.Add(new KeyValuePair<string, string>(name, value));
        _cachedQueryString = null; // 清除缓存
    }

    /// <summary>
    /// 添加查询参数；<paramref name="value"/> 为 <c>null</c> 时仍以空值落键（<c>name=</c>）。
    /// </summary>
    /// <remarks>
    /// CFG-04：供 <c>[Query(SerializeNull = true)]</c> 使用。与 <see cref="Add(string, string?)"/>
    /// 的「null 跳过」语义区分：本方法保留「显式空值」表达力。
    /// </remarks>
    /// <param name="name">参数名称。</param>
    /// <param name="value">参数值；为 <c>null</c> 时序列化为空串。</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> 为 null 或空白。</exception>
    public void AddAllowNull(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        _params.Add(new KeyValuePair<string, string>(name, value ?? string.Empty));
        _cachedQueryString = null; // 清除缓存
    }

    /// <summary>
    /// 添加查询参数（空值将被忽略），并使用指定的格式化字符串。
    /// </summary>
    /// <param name="name">参数名称。</param>
    /// <param name="value">参数值。</param>
    /// <param name="formatString">格式化字符串，为 null 或空时使用默认格式。</param>
    public void Add(string name, int? value, string? formatString)
        => AddNullableWithValueToString(name, value, formatString);

    /// <summary>
    /// 添加查询参数（空值将被忽略），并使用指定的格式化字符串。
    /// </summary>
    /// <param name="name">参数名称。</param>
    /// <param name="value">参数值。</param>
    /// <param name="formatString">格式化字符串，为 null 或空时使用默认格式。</param>
    public void Add(string name, short? value, string? formatString)
        => AddNullableWithValueToString(name, value, formatString);

    /// <summary>
    /// 添加查询参数（空值将被忽略），并使用指定的格式化字符串。
    /// </summary>
    /// <param name="name">参数名称。</param>
    /// <param name="value">参数值。</param>
    /// <param name="formatString">格式化字符串，为 null 或空时使用默认格式。</param>
    public void Add(string name, long? value, string? formatString)
        => AddNullableWithValueToString(name, value, formatString);

    /// <summary>
    /// 添加查询参数（空值将被忽略），并使用指定的格式化字符串。
    /// </summary>
    /// <param name="name">参数名称。</param>
    /// <param name="value">参数值。</param>
    /// <param name="formatString">格式化字符串，为 null 或空时使用默认格式。</param>
    public void Add(string name, float? value, string? formatString)
        => AddNullableWithValueToString(name, value, formatString);

    /// <summary>
    /// 添加查询参数（空值将被忽略），并使用指定的格式化字符串。
    /// </summary>
    /// <param name="name">参数名称。</param>
    /// <param name="value">参数值。</param>
    /// <param name="formatString">格式化字符串，为 null 或空时使用默认格式。</param>
    public void Add(string name, decimal? value, string? formatString)
        => AddNullableWithValueToString(name, value, formatString);

    /// <summary>
    /// 添加查询参数（空值将被忽略），并使用指定的格式化字符串。
    /// </summary>
    /// <param name="name">参数名称。</param>
    /// <param name="value">参数值。</param>
    /// <param name="formatString">格式化字符串，为 null 或空时使用默认格式。</param>
    public void Add(string name, double? value, string? formatString)
        => AddNullableWithValueToString(name, value, formatString);

    /// <summary>
    /// 添加查询参数（空值将被忽略），并使用指定的格式化字符串。
    /// </summary>
    /// <param name="name">参数名称。</param>
    /// <param name="value">参数值。</param>
    /// <param name="formatString">格式化字符串，为 null 或空时使用默认格式。</param>
    public void Add(string name, Guid? value, string? formatString)
        => AddNullableWithValueToString(name, value, formatString);

    /// <summary>
    /// 添加查询参数（空值将被忽略），并使用指定的格式化字符串。
    /// </summary>
    /// <param name="name">参数名称。</param>
    /// <param name="value">参数值。</param>
    /// <param name="formatString">格式化字符串，为 null 或空时使用默认格式（yyyy/MM/dd）。</param>
    public void Add(string name, DateTime? value, string? formatString)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));
        if (!value.HasValue)
            return;
        // DateTime 默认使用 yyyy/MM/dd 格式，与其他类型的 ToString() 默认行为不同
        var valueString = !string.IsNullOrWhiteSpace(formatString)
            ? value!.Value.ToString(formatString, CultureInfo.InvariantCulture)
            : value!.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        Add(name, valueString);
    }

    /// <summary>
    /// 添加查询参数（空值将被忽略），并使用指定的格式化字符串。
    /// </summary>
    /// <param name="name">参数名称。</param>
    /// <param name="value">参数值。</param>
    /// <param name="formatString">格式化字符串（未使用，仅为 API 一致性保留）。</param>
    public void Add(string name, bool? value, string? formatString)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));
        if (!value.HasValue)
            return;
        // bool 转换为小写 "true"/"false"，符合 URL 查询参数惯例
        var valueString = value.Value ? "true" : "false";
        Add(name, valueString);
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
    /// <remarks>
    /// M3-#25：相对路径（如 <c>/api/users</c>）降级为字符串拼接
    /// （返回 <c>/api/users?page=1</c>），不再要求绝对 URI；绝对 URL 维持 <see cref="UriBuilder"/> 拼接语义。
    /// </remarks>
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

        // M3-#25（方案 B）：相对 baseUrl 不适用 UriBuilder（要求绝对 URI），降级为字符串拼接
        // （合法且常见："/api/users" + "?page=1"）
        // [跨平台] Unix 上 .NET 6 会把前导 '/' 的字符串（如 "/relative"）解析为 file:// 绝对 URI
        // （.NET 8+ 已移除该行为），故除 UriKind.Absolute 外还须要求 http/https scheme，
        // 否则 Linux + net6 会误入 UriBuilder 分支抛 UriFormatException。
        if (!Uri.TryCreate(_baseUrl, UriKind.Absolute, out var absUri)
            || (absUri.Scheme != Uri.UriSchemeHttp && absUri.Scheme != Uri.UriSchemeHttps)
            || absUri.Host.Length == 0)
        {
            _cachedQueryString = _baseUrl + (query.Length > 0 ? "?" + query : string.Empty);
            return _cachedQueryString;
        }

        // 使用 UriBuilder 安全拼接（绝对 URL 维持现状）
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

        // 预估容量：每个参数平均约 30 字符（key=value + &）
        var sb = new StringBuilder(_params.Count * 30);
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

    /// <summary>
    /// 通用的可空值类型参数添加方法，消除重复代码。
    /// </summary>
    /// <typeparam name="T">值类型，必须实现 <see cref="IFormattable"/>。</typeparam>
    /// <param name="name">参数名称。</param>
    /// <param name="value">可空参数值。</param>
    /// <param name="formatString">格式化字符串，为 null 或空时使用默认 ToString()。</param>
    private void AddNullableWithValueToString<T>(string name, T? value, string? formatString) where T : struct, IFormattable
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));
        if (!value.HasValue)
            return;
        // 统一使用 InvariantCulture，避免不同区域设置下小数分隔符差异（如逗号）破坏 URL
        var valueString = value.Value.ToString(formatString, CultureInfo.InvariantCulture);
        Add(name, valueString);
    }
}
