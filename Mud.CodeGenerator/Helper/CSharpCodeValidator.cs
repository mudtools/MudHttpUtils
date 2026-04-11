// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.CodeGenerator;

/// <summary>
/// C# 代码验证工具类
/// </summary>
internal static class CSharpCodeValidator
{
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate",
        "do", "double", "else", "enum", "event", "explicit", "extern", "false",
        "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
        "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
        "new", "null", "object", "operator", "out", "override", "params", "private",
        "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
        "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    };

    /// <summary>
    /// 验证字符串是否为合法的C#标识符
    /// </summary>
    /// <param name="identifier">要验证的标识符</param>
    /// <returns>如果合法返回true，否则返回false</returns>
    public static bool IsValidCSharpIdentifier(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return false;

        // 检查第一个字符是否为字母或下划线（不能以数字开头）
        if (!char.IsLetter(identifier[0]) && identifier[0] != '_')
            return false;

        // C# 关键字检查
        if (IsCSharpKeyword(identifier))
            return false;

        // 检查其余字符是否为字母、数字或下划线
        for (int i = 1; i < identifier.Length; i++)
        {
            if (!char.IsLetterOrDigit(identifier[i]) && identifier[i] != '_')
                return false;
        }

        return true;
    }

    /// <summary>
    /// 检查字符串是否为C#关键字
    /// </summary>
    private static bool IsCSharpKeyword(string identifier)
    {
        return CSharpKeywords.Contains(identifier);
    }

    /// <summary>
    /// 验证URL模板格式
    /// </summary>
    /// <param name="urlTemplate">URL模板</param>
    /// <param name="errorMessage">错误信息（如果验证失败）</param>
    /// <returns>如果合法返回true，否则返回false</returns>
    public static bool IsValidUrlTemplate(string urlTemplate, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrEmpty(urlTemplate))
            return true;

        int openBraceCount = 0;
        int closeBraceCount = 0;

        for (int i = 0; i < urlTemplate.Length; i++)
        {
            char c = urlTemplate[i];

            if (c == '{')
            {
                openBraceCount++;
                // 查找对应的右花括号
                int endBrace = urlTemplate.IndexOf('}', i + 1);
                if (endBrace == -1)
                {
                    errorMessage = "URL模板中存在未闭合的花括号 '{'";
                    return false;
                }

                var paramName = urlTemplate.Substring(i + 1, endBrace - i - 1).Trim();
                if (string.IsNullOrEmpty(paramName))
                {
                    errorMessage = "URL模板中存在空的花括号 '{}'";
                    return false;
                }

                // 检查参数名是否为合法的C#标识符
                if (!IsValidCSharpIdentifier(paramName))
                {
                    errorMessage = $"URL模板中的参数名 '{paramName}' 不是合法的C#标识符";
                    return false;
                }

                i = endBrace;
                closeBraceCount++;
            }
            else if (c == '}')
            {
                // 这里只检查是否有未配对的右花括号
                // 但实际上，上面的逻辑已经处理了所有匹配的花括号
                // 所以如果这里还遇到右花括号，说明是未配对的
                closeBraceCount++;
                if (closeBraceCount > openBraceCount)
                {
                    errorMessage = "URL模板中存在多余闭合花括号 '}'";
                    return false;
                }
            }
        }

        if (openBraceCount != closeBraceCount)
        {
            errorMessage = "URL模板中花括号不匹配";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 验证RegistryGroupName并报告诊断信息
    /// </summary>
    /// <param name="context">源代码生成上下文</param>
    /// <param name="location">错误位置</param>
    /// <param name="registryGroupName">注册组名称</param>
    /// <returns>如果验证通过返回true，否则返回false</returns>
    public static bool ValidateAndReportRegistryGroupName(
        SourceProductionContext context,
        Location location,
        string? registryGroupName)
    {
        if (string.IsNullOrEmpty(registryGroupName))
            return true;

        if (!IsValidCSharpIdentifier(registryGroupName))
        {
            ReportInvalidRegistryGroupName(context, location, registryGroupName);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 报告无效的RegistryGroupName诊断信息
    /// </summary>
    private static void ReportInvalidRegistryGroupName(
        SourceProductionContext context,
        Location location,
        string registryGroupName)
    {
        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.HttpClientInvalidRegistryGroupName, location, registryGroupName));
    }
}
