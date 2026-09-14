// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.Json;

namespace Mud.HttpUtils;

/// <summary>
/// 命名策略预设（同时配置 JSON 属性名 + URL 参数键格式化器）。
/// </summary>
/// <remarks>
/// </remarks>
public static class MudHttpClientNamingPresets
{
    /// <summary>
    /// CamelCase：JSON 属性名为 camelCase。
    /// </summary>
    /// <returns>配置了 <see cref="JsonNamingPolicy.CamelCase"/> 的 <see cref="JsonSerializerOptions"/>。</returns>
    public static JsonSerializerOptions CamelCase() =>
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// SnakeCase：JSON 属性名为 snake_case。
    /// </summary>
    /// <returns>配置了 snake_case 命名策略的 <see cref="JsonSerializerOptions"/>。</returns>
    public static JsonSerializerOptions SnakeCase() =>
        new() { PropertyNamingPolicy = SeparatedCaseJsonNamingPolicy.Snake };

    /// <summary>
    /// KebabCase：JSON 属性名为 kebab-case。
    /// </summary>
    /// <returns>配置了 kebab-case 命名策略的 <see cref="JsonSerializerOptions"/>。</returns>
    public static JsonSerializerOptions KebabCase() =>
        new() { PropertyNamingPolicy = SeparatedCaseJsonNamingPolicy.Kebab };
}
