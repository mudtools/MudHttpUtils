using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Reflection;
using System.Text.Json;

namespace Mud.HttpUtils;

internal static class QueryMapHelper
{
    private const int MaxFlattenRecursionDepth = 10;

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

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
        if (obj == null) throw new ArgumentNullException(nameof(obj));
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
                    var subKey = string.IsNullOrEmpty(prefix) ? kvp.Key : prefix + separator + kvp.Key;
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
