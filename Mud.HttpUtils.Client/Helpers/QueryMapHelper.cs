// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Reflection;

namespace Mud.HttpUtils;

/// <summary>
/// 查询参数映射辅助类
/// </summary>
public static class QueryMapHelper
{
    private const int MaxFlattenRecursionDepth = 10;

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

    /// <summary>
    /// 未注入序列化器时的默认实例，确保 JSON 序列化至少使用库默认选项（含 MudHttpJsonContext.Default）。
    /// </summary>
    private static readonly IHttpContentSerializer s_defaultSerializer = HttpContentSerializerFactory.CreateDefault();


    /// <summary>
    /// 递归解释查询参数对象，将其属性展平为键值对，并添加到 QueryParameterBuilder 中。支持基本类型、字符串、枚举、日期时间、GUID，以及实现了 IQueryParameter 接口的对象。对于复杂对象，会继续递归展平其属性。可以选择是否包含 null 值，是否使用 JSON 序列化，以及是否对键和值进行 URL 编码。
    /// </summary>
    /// <remarks>
    /// <paramref name="urlEncode"/> 为 false 时会将结果写入 <paramref name="rawPairs"/>，此时 <b>键与值均不做 URL 编码</b>，
    /// 由调用方负责最终的编码/拼接；该模式适用于需要传入已编码或含特殊分隔符的原始查询串的场景。
    /// </remarks>
#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "该方法已标注 RequiresUnreferencedCode，属显式非 AOT 回退路径；IL2072 来自 GetProperties 返回值赋给参数。")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("QueryMap uses reflection to flatten POCO objects and is not compatible with Native AOT. Consider using IQueryParameter or individual [Query] parameters instead.")]
#endif
#if NET7_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("QueryMap 通过 IHttpContentSerializer.Serialize(object, Type) 使用运行时类型分派，Native AOT 不支持。请改用 IQueryParameter 或独立的 [Query] 参数。")]
#endif
    public static void FlattenObjectToQueryParams(
        object obj,
        string prefix,
        string separator,
        QueryParameterBuilder queryParams,
        bool includeNullValues,
        bool useJsonSerialization,
        bool urlEncode = true,
        List<string>? rawPairs = null,
        int depth = 0,
        IHttpContentSerializer? contentSerializer = null)
    {
        if (obj == null)
        {
            throw new ArgumentNullException(nameof(obj));
        }

        if (depth > MaxFlattenRecursionDepth) throw new InvalidOperationException("Maximum recursion depth exceeded while flattening object of type " + obj.GetType().Name + ". This may be caused by a circular reference.");

        var serializer = contentSerializer ?? s_defaultSerializer;
        // M6-HC-19：过滤索引器属性，避免 prop.GetValue(obj) 因缺少索引实参抛 TargetParameterCountException。
        var properties = PropertyCache.GetOrAdd(obj.GetType(), t => t.GetProperties().Where(static p => p.GetIndexParameters().Length == 0).ToArray());
        foreach (var prop in properties)
        {
            var value = prop.GetValue(obj);
            var key = string.IsNullOrEmpty(prefix) ? prop.Name : prefix + separator + prop.Name;

            if (value == null)
            {
                if (includeNullValues)
                {
                    if (urlEncode && rawPairs == null)
                        queryParams.Add(key, string.Empty);
                    else if (rawPairs != null)
                        rawPairs.Add(key + "=");
                    else
                        queryParams.Add(key, string.Empty);
                }
                continue;
            }

            var type = value.GetType();
            if (type.IsPrimitive || value is string || value is decimal || type.IsEnum || value is DateTime || value is DateTimeOffset || value is Guid)
            {
                string stringValue;
                if (useJsonSerialization)
                    stringValue = serializer.Serialize(value, type);
                else
                    stringValue = value.ToString() ?? string.Empty;

                if (!urlEncode && rawPairs != null)
                    rawPairs.Add(key + "=" + stringValue);
                else
                    queryParams.Add(key, stringValue);
            }
            else if (value is IQueryParameter queryParam)
            {
                foreach (var kvp in queryParam.ToQueryParameters())
                {
                    var subKey = string.IsNullOrEmpty(key) ? kvp.Key : key + separator + kvp.Key;
                    if (includeNullValues || !string.IsNullOrEmpty(kvp.Value))
                    {
                        if (!urlEncode && rawPairs != null)
                            rawPairs.Add(subKey + "=" + (kvp.Value ?? string.Empty));
                        else
                            queryParams.Add(subKey, kvp.Value ?? string.Empty);
                    }
                }
            }
            else
            {
                FlattenObjectToQueryParams(value, key, separator, queryParams, includeNullValues, useJsonSerialization, urlEncode, rawPairs, depth + 1, contentSerializer);
            }
        }
    }
}
