// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mud.HttpUtils;

/// <summary>
/// R-P2-05：默认异常擦除器 —— 按敏感字段词表掩码 <see cref="ApiException.Content"/> 与
/// <see cref="ApiException.RequestContent"/> 中的敏感值（form 与 JSON 双格式）。
/// </summary>
/// <remarks>
/// <para>
/// <b>缺陷（C5）</b>：原实现仅在宿主<b>显式</b>注册 <see cref="IExceptionRedactor"/> 时才擦除；
/// 未注册时异常对象会长期持有请求体与响应体明文（含 <c>access_token</c> / <c>password</c> /
/// <c>client_secret</c> 等），随异常日志、APM 上报、崩溃转储外泄。
/// </para>
/// <para>
/// <b>词表单一事实源</b>：直接复用 <c>SensitiveUrlRedactor.SensitiveFieldNames</c>
/// （与 URL query 脱敏、<c>MessageSanitizer</c> 同一份），不另行维护第三份词表。
/// </para>
/// <para>
/// <b>刻意不做的两件事</b>：① <b>不截断</b>正文（对照 <c>MessageSanitizer.Sanitize</c> 的长度上限语义，
/// 异常正文是排障依据，截断会丢失信息）；② <b>不改动非结构化正文</b>（纯文本无法可靠识别键值边界，
/// 猜测式掩码会误伤）。两者都会让"擦除"从可预测的字段级操作变成不可预测的文本改写。
/// </para>
/// <para>
/// <b>可覆盖</b>：容器经 <c>TryAddSingleton</c> 注册，宿主显式注册的实现优先
/// （<c>AddSingleton&lt;IExceptionRedactor&gt;()</c> 且先于库的注册调用）。
/// </para>
/// </remarks>
internal sealed class DefaultSensitiveFieldExceptionRedactor : IExceptionRedactor
{
    /// <summary>掩码后的占位符（与 URL query 脱敏同文案，便于日志检索对齐）。</summary>
    private const string Mask = "***REDACTED***";

    /// <inheritdoc />
    public void Redact(ApiException exception)
    {
        if (exception is null)
            return;

        exception.RequestContent = RedactBody(exception.RequestContent);
        exception.Content = RedactBody(exception.Content);
    }

    /// <summary>
    /// R-P2-05：按正文形态分派掩码（JSON 对象/数组 → JSON 路径；<c>k=v&amp;k2=v2</c> → form 路径；其余原样返回）。
    /// </summary>
    /// <param name="body">正文（可为 null / 空）。</param>
    /// <returns>掩码后的正文；非 JSON / form 形态时与输入一致。</returns>
    internal static string? RedactBody(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return body;

        var trimmed = body!.TrimStart();
        if (trimmed.Length == 0)
            return body;

        if (trimmed[0] == '{' || trimmed[0] == '[')
        {
            try
            {
                var node = JsonNode.Parse(body, new JsonNodeOptions { PropertyNameCaseInsensitive = true });
                return node is null ? body : (RedactJsonNode(node, 0)?.ToJsonString() ?? body);
            }
            catch (JsonException)
            {
                return body;      // 形态像 JSON 但解析失败：保持原样，不做启发式改写
            }
        }

        return RedactForm(body);
    }

    private static JsonNode? RedactJsonNode(JsonNode? node, int depth)
    {
        // 与 MessageSanitizer 同口径的深度护栏：畸形深嵌套输入不得导致栈溢出。
        if (node == null || depth > 32)
            return "***";

        switch (node)
        {
            case JsonObject obj:
                var redactedObj = new JsonObject();
                foreach (var property in obj)
                {
                    redactedObj[property.Key] = IsSensitiveField(property.Key)
                        ? Mask
                        : RedactJsonNode(property.Value, depth + 1);
                }
                return redactedObj;

            case JsonArray arr:
                var redactedArr = new JsonArray();
                foreach (var item in arr)
                {
                    redactedArr.Add(RedactJsonNode(item, depth + 1));
                }
                return redactedArr;

            default:
                return node.DeepClone();
        }
    }

    /// <summary>
    /// form 形态掩码：<c>access_token=abc&amp;page=2</c> → <c>access_token=***REDACTED***&amp;page=2</c>。
    /// </summary>
    private static string RedactForm(string body)
    {
        // 无分隔且无 '=' ⇒ 不可能是 form（如纯文本 "unauthorized"），零改写返回。
        if (body.IndexOf('=') < 0)
            return body;

        var parts = body.Split('&');
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;                              // 裸键或空键：无值可掩码

            var key = part.Substring(0, eq);
            if (!IsSensitiveField(Uri.UnescapeDataString(key)))
                continue;

            parts[i] = key + "=" + Mask;
        }

        return string.Join("&", parts);
    }

    private static bool IsSensitiveField(string fieldName)
        => Helpers.SensitiveUrlRedactor.SensitiveFieldNames.Contains(fieldName);
}
