// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;

namespace Mud.CodeGenerator;

internal sealed class GeneratedCodeConsts
{
    public const string CompilerGeneratedAttribute = "[global::System.Runtime.CompilerServices.CompilerGenerated]";

    public static string ServiceGeneratedCodeAttribute => $"[global::System.CodeDom.Compiler.GeneratedCode(\"Mud.ServiceCodeGenerator\", \"{GetAssemblyVersion()}\")]";

    public static string HttpGeneratedCodeAttribute => $"[global::System.CodeDom.Compiler.GeneratedCode(\"Mud.HttpUtils.Generator\", \"{GetAssemblyVersion()}\")]";

    public static string IgnoreGeneratorAttribute = "IgnoreGeneratorAttribute";
    /// <summary>
    /// 获取当前程序集的版本号
    /// </summary>
    /// <returns>程序集版本号字符串</returns>
    private static string GetAssemblyVersion()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version ?? new Version(1, 0, 0);
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch
        {
            // 如果获取失败，返回默认版本号
            return "1.2.5";
        }
    }
}
