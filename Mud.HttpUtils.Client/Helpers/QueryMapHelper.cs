// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Reflection;
using System.Text.Json;

namespace Mud.HttpUtils;

/// <summary>
/// 查询参数映射辅助类
/// </summary>
public static class QueryMapHelper
{
    private const int MaxFlattenRecursionDepth = 10;

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();


    /// <summary>
    /// 递归解释查询参数对象，将其属性展平为键值对，并添加到 NameValueCollection 中。支持基本类型、字符串、枚举、日期时间、GUID，以及实现了 IQueryParameter 接口的对象。对于复杂对象，会继续递归展平其属性。可以选择是否包含 null 值，是否使用 JSON 序列化，以及是否对键和值进行 URL 编码。
    /// </summary>
#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2072:Target parameter return value does not satisfy DynamicallyAccessedMemberTypes requirements")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("QueryMap uses reflection to flatten POCO objects and is not compatible with Native AOT. Consider using IQueryParameter or individual [Query] parameters instead.")]
#endif
    public static void FlattenObjectToQueryParams(
        object obj,
        string prefix,
        string separator,
        NameValueCollection queryParams,
        bool includeNullValues,
        bool useJsonSerialization,
        bool urlEncode = true,
        List<string>? rawPairs = null,
        int depth = 0)
    {
        if (obj == null)
        {
            throw new ArgumentNullException(nameof(obj));
        }

        if (depth > MaxFlattenRecursionDepth) throw new InvalidOperationException("Maximum recursion depth exceeded while flattening object of type " + obj.GetType().Name + ". This may be caused by a circular reference.");

        var properties = PropertyCache.GetOrAdd(obj.GetType(), t => t.GetProperties());
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
                        rawPairs.Add(Uri.EscapeDataString(key) + "=");
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
                    stringValue = JsonSerializer.Serialize(value);
                else
                    stringValue = value.ToString() ?? string.Empty;

                if (!urlEncode && rawPairs != null)
                    rawPairs.Add(Uri.EscapeDataString(key) + "=" + stringValue);
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
                            rawPairs.Add(Uri.EscapeDataString(subKey) + "=" + (kvp.Value ?? string.Empty));
                        else
                            queryParams.Add(subKey, kvp.Value ?? string.Empty);
                    }
                }
            }
            else
            {
                FlattenObjectToQueryParams(value, key, separator, queryParams, includeNullValues, useJsonSerialization, urlEncode, rawPairs, depth + 1);
            }
        }
    }
}
