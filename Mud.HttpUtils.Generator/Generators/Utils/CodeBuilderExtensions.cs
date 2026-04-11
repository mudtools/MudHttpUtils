// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generators.Utils;

/// <summary>
/// StringBuilder 扩展方法类，用于代码生成
/// </summary>
internal static class CodeBuilderExtensions
{
    /// <summary>
    /// 缩进层级
    /// </summary>
    private const int IndentSize = 4;

    /// <summary>
    /// 添加缩进
    /// </summary>
    public static StringBuilder AppendIndent(this StringBuilder sb, int level = 1)
    {
        sb.Append(' ', IndentSize * level);
        return sb;
    }

    /// <summary>
    /// 添加行并缩进
    /// </summary>
    public static StringBuilder AppendLineIndent(this StringBuilder sb, int level, string text)
    {
        sb.AppendIndent(level);
        sb.AppendLine(text);
        return sb;
    }

    /// <summary>
    /// 添加空行
    /// </summary>
    public static StringBuilder AppendEmptyLine(this StringBuilder sb)
    {
        sb.AppendLine();
        return sb;
    }

    /// <summary>
    /// 开始代码块
    /// </summary>
    public static StringBuilder StartBlock(this StringBuilder sb, int level)
    {
        sb.AppendLineIndent(level, "{");
        return sb;
    }

    /// <summary>
    /// 结束代码块
    /// </summary>
    public static StringBuilder EndBlock(this StringBuilder sb, int level, bool withSemicolon = false)
    {
        if (withSemicolon)
        {
            sb.AppendLineIndent(level, "};");
        }
        else
        {
            sb.AppendLineIndent(level, "}");
        }
        return sb;
    }
}
