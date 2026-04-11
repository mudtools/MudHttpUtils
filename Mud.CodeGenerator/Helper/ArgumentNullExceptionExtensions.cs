// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.CodeGenerator;

/// <summary>
/// 参数验证扩展方法（兼容 .NET Standard 2.0）
/// </summary>
internal static class ArgumentNullExceptionExtensions
{
    /// <summary>
    /// 如果参数为 null 则抛出 ArgumentNullException（.NET Standard 2.0 兼容版本）
    /// </summary>
    /// <typeparam name="T">参数类型（必须是引用类型）</typeparam>
    /// <param name="argument">参数值</param>
    /// <param name="paramName">参数名称</param>
    public static void ThrowIfNull<T>(T argument, string? paramName = null) where T : class
    {
        if (argument is null)
        {
            throw paramName != null
                ? new ArgumentNullException(paramName)
                : new ArgumentNullException("", "参数不能为空");
        }
    }

    /// <summary>
    /// 如果参数为 null 或 空字符串 则抛出 ArgumentNullException（.NET Standard 2.0 兼容版本）
    /// </summary>
    /// <param name="argument">参数值</param>
    /// <param name="paramName">参数名称</param>
    /// <exception cref="ArgumentNullException"></exception>
    public static void ThrowIfNullOrEmpty(this string? argument, string? paramName = null)
    {
        if (argument == null)
            throw paramName != null
               ? new ArgumentNullException(paramName)
               : new ArgumentNullException("", "参数不能为空");

        if (string.IsNullOrEmpty(argument))
            throw paramName != null
               ? new ArgumentNullException(paramName)
               : new ArgumentNullException("", "参数不能为空");
    }
}
