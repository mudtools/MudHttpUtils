// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 查询参数构建器，用于在生成的代码中收集和拼接 URL 查询参数。
/// 替代 <see cref="System.Collections.Specialized.NameValueCollection"/>，因为后者
/// 的 <see cref="object.ToString"/> 不生成 URL 编码的查询字符串。
/// </summary>
public sealed class QueryParameterBuilder
{
    private readonly List<KeyValuePair<string, string>> _params = new();

    /// <summary>
    /// 获取已添加的查询参数数量。
    /// </summary>
    public int Count => _params.Count;

    /// <summary>
    /// 添加一个查询参数。
    /// </summary>
    /// <param name="name">参数名称。</param>
    /// <param name="value">参数值（可为 null，将被替换为空字符串）。</param>
    public void Add(string name, string? value)
    {
        _params.Add(new KeyValuePair<string, string>(name, value ?? string.Empty));
    }

    /// <summary>
    /// 获取指定名称的最后一个参数值（兼容 NameValueCollection 索引器语义）。
    /// </summary>
    /// <param name="name">参数名称。</param>
    /// <returns>最后添加的值；如果不存在则返回 null。</returns>
    public string? this[string name]
    {
        get
        {
            for (var i = _params.Count - 1; i >= 0; i--)
            {
                if (_params[i].Key == name)
                    return _params[i].Value;
            }
            return null;
        }
    }

    /// <summary>
    /// 生成 URL 编码的查询字符串（key1=value1&amp;key2=value2）。
    /// 使用 <see cref="Uri.EscapeDataString(string)"/> 进行 URL 编码，兼容所有目标框架且无需 System.Web 依赖。
    /// </summary>
    /// <returns>URL 编码的查询字符串；如果没有参数则返回空字符串。</returns>
    public override string ToString()
    {
        if (_params.Count == 0)
            return string.Empty;

        var sb = new StringBuilder(_params.Count * 32);
        for (var i = 0; i < _params.Count; i++)
        {
            if (i > 0)
                sb.Append('&');
            sb.Append(Uri.EscapeDataString(_params[i].Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(_params[i].Value));
        }
        return sb.ToString();
    }
}
