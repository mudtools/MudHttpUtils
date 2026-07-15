// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mud.HttpUtils;

/// <summary>
/// 基于 <see cref="JsonSerializer"/> 的 <see cref="IHttpContentSerializer"/> 默认实现。
/// </summary>
/// <remarks>
/// <para>
/// 此实现行为等价于直接调用 <c>JsonSerializer</c>，保证向后兼容。
/// 请求体序列化使用 <see cref="StringContent"/> 包装 JSON 字符串（与原 EnhancedHttpClient 行为一致）。
/// </para>
/// <para>
/// <b>Native AOT</b>：构造时传入的 options 应包含 <c>TypeInfoResolver</c>
/// （由 <c>JsonSerializerContext</c> 提供），以确保 AOT 下序列化/反序列化路径安全。
/// </para>
/// </remarks>
public class SystemTextJsonContentSerializer : IHttpContentSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// 获取此序列化器使用的 <see cref="JsonSerializerOptions"/>。
    /// </summary>
    public JsonSerializerOptions Options => _options;

    /// <summary>
    /// 初始化 <see cref="SystemTextJsonContentSerializer"/> 实例。
    /// </summary>
    /// <param name="options">JSON 序列化选项。为 null 时使用默认选项。</param>
    public SystemTextJsonContentSerializer(JsonSerializerOptions? options = null)
    {
        _options = options ?? new JsonSerializerOptions();
    }

    /// <inheritdoc/>
    public HttpContent? ToHttpContent<T>(T item, object? options = null)
    {
        if (item is null) return null;
        var opts = ResolveOptions(options);
        var json = JsonSerializer.Serialize(item, opts);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    /// <inheritdoc/>
    public async Task<T?> FromHttpContentAsync<T>(HttpContent content, object? options = null, CancellationToken cancellationToken = default)
    {

        var opts = ResolveOptions(options);
#if NET5_0_OR_GREATER
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
        using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
        {
            return await JsonSerializer.DeserializeAsync<T>(stream, opts, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public string Serialize<T>(T item, object? options = null)
    {
        var opts = ResolveOptions(options);
        return JsonSerializer.Serialize(item, opts);
    }

    /// <inheritdoc/>
    public string Serialize(object? item, System.Type type, object? options = null)
    {
        var opts = ResolveOptions(options);
        return JsonSerializer.Serialize(item, type, opts);
    }

    /// <inheritdoc/>
    public T? Deserialize<T>(string json, object? options = null)
    {
#if NET6_0_OR_GREATER
        if (options is System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo)
        {
            return JsonSerializer.Deserialize(json, jsonTypeInfo);
        }
#endif
        var opts = ResolveOptions(options);
        return JsonSerializer.Deserialize<T>(json, opts);
    }

    /// <inheritdoc/>
#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("GetFieldNameForProperty 使用反射读取属性特性，AOT 场景应由源生成器在编译期提供字段名映射。")]
#endif
    public string? GetFieldNameForProperty(PropertyInfo propertyInfo)
    {
        var attr = propertyInfo.GetCustomAttribute<JsonPropertyNameAttribute>();
        return attr?.Name ?? _options.PropertyNamingPolicy?.ConvertName(propertyInfo.Name);
    }

    private JsonSerializerOptions ResolveOptions(object? options)
    {
        return options as JsonSerializerOptions ?? _options;
    }
}
