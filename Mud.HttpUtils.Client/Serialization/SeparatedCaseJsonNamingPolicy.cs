// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.Json;

namespace Mud.HttpUtils;

/// <summary>
/// 分隔符式 JSON 命名策略（snake_case / kebab-case），兼容 net6.0 / netstandard2.0。
/// </summary>
/// <remarks>
/// .NET 8+ 可直接使用 <c>JsonNamingPolicy.SnakeCaseLower</c> / <c>KebabCaseLower</c>，
/// 本类为旧框架提供等价实现。
/// </remarks>
public sealed class SeparatedCaseJsonNamingPolicy : JsonNamingPolicy
{
    private readonly char _separator;

    private SeparatedCaseJsonNamingPolicy(char separator) => _separator = separator;

    /// <summary>snake_case 命名策略（下划线分隔）。</summary>
    public static SeparatedCaseJsonNamingPolicy Snake { get; } = new('_');

    /// <summary>kebab-case 命名策略（短横线分隔）。</summary>
    public static SeparatedCaseJsonNamingPolicy Kebab { get; } = new('-');

    /// <inheritdoc/>
    public override string ConvertName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var sb = new System.Text.StringBuilder(name.Length * 2);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                sb.Append(_separator);
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }
}
