// -----------------------------------------------------------------------
//  M5-HC-04：缓存键安全分级
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// M5-HC-04：参数对默认缓存键的安全分级。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="Classification.KeySafeScalar"/>：基元/string/decimal/Guid/DateTime 等，可稳定映射为 InvariantCulture 字符串。</item>
///   <item><see cref="Classification.KeySafeCollection"/>：元素为 KeySafeScalar 的数组，可用 string.Join 参与键。</item>
///   <item><see cref="Unsafe"/>：自定义对象 / [Body] / [QueryMap] / Stream 等，默认键会退化为类型名 → 串键。</item>
/// </list>
/// Unsafe 参数在无 <c>CacheKeyTemplate</c> 时触发 HTTPCLIENT031（Error）。
/// </remarks>
internal static class CacheKeySafety
{
    public enum Classification
    {
        KeySafeScalar,
        KeySafeCollection,
        Unsafe,
    }

    /// <summary>强制 Unsafe 的参数特性（Body / Form / Upload / QueryMap / Stream 语义）。</summary>
    private static readonly HashSet<string> ForceUnsafeAttributes = new(StringComparer.Ordinal)
    {
        "BodyAttribute", "Body",
        "FormContentAttribute", "FormContent",
        "FormAttribute", "Form",
        "MultipartFormAttribute", "MultipartForm",
        "UploadAttribute", "Upload",
        "QueryMapAttribute", "QueryMap",
        "FilePathAttribute", "FilePath",
        "HeaderCollectionAttribute", "HeaderCollection",
        "RawQueryStringAttribute", "RawQueryString",
    };

    public static Classification Classify(ParameterInfo parameter)
    {
        // CancellationToken / [Token] 不参与键
        if (TypeDetectionHelper.IsCancellationToken(parameter.Type))
            return Classification.KeySafeScalar; // 生成时会被跳过，分类无实际影响

        foreach (var attr in parameter.Attributes)
        {
            if (ForceUnsafeAttributes.Contains(attr.Name))
                return Classification.Unsafe;
        }

        var type = parameter.Type;

        // Stream / IProgress / IAsyncEnumerable / IFormFile 一律 Unsafe
        if (IsStreamLike(type))
            return Classification.Unsafe;

        // 数组：元素须为简单类型
        if (TypeDetectionHelper.IsArrayType(type))
        {
            var elementType = type.TrimEnd('?');
            if (elementType.EndsWith("[]", StringComparison.Ordinal))
                elementType = elementType.Substring(0, elementType.Length - 2);
            return TypeDetectionHelper.IsSimpleType(elementType)
                ? Classification.KeySafeCollection
                : Classification.Unsafe;
        }

        // 简单标量
        if (TypeDetectionHelper.IsSimpleType(type))
            return Classification.KeySafeScalar;

        // 枚举：TypeDetectionHelper.IsSimpleType 不含 enum，但 enum ToString 稳定 → 视为 KeySafeScalar
        if (parameter.TypeSymbol?.TypeKind == TypeKind.Enum)
            return Classification.KeySafeScalar;

        return Classification.Unsafe;
    }

    /// <summary>生成 KeySafeScalar 的键片段表达式（InvariantCulture）。</summary>
    public static string ScalarKeyExpression(string paramName)
        => $"Convert.ToString({paramName}, CultureInfo.InvariantCulture)";

    /// <summary>生成 KeySafeCollection 的键片段表达式（string.Join + InvariantCulture）。</summary>
    public static string CollectionKeyExpression(string paramName)
        => $"({paramName} == null ? \"-\" : string.Join(\",\", {paramName}.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture) ?? \"-\")))";

    private static bool IsStreamLike(string typeName)
    {
        var t = typeName.TrimEnd('?');
        return t is "Stream" or "System.IO.Stream"
            or "IProgress`1" || t.StartsWith("IProgress<", StringComparison.Ordinal)
            || t.StartsWith("System.IProgress<", StringComparison.Ordinal)
            || t.StartsWith("IAsyncEnumerable<", StringComparison.Ordinal)
            || t.StartsWith("System.Collections.Generic.IAsyncEnumerable<", StringComparison.Ordinal)
            || t.Contains("IFormFile", StringComparison.Ordinal);
    }
}
