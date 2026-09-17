// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;

namespace Mud.HttpUtils;

internal sealed class GeneratedCodeConsts
{
    public const string CompilerGeneratedAttribute = "[global::System.Runtime.CompilerServices.CompilerGenerated]";

    private static readonly Lazy<string> s_cachedVersion = new(GetAssemblyVersionCore);

    public static string HttpGeneratedCodeAttribute => $"[global::System.CodeDom.Compiler.GeneratedCode(\"Mud.HttpUtils.Generator\", \"{s_cachedVersion.Value}\")]";

    private static string GetAssemblyVersionCore()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version ?? new Version(1, 0, 0);
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch
        {
            return "1.2.5";
        }
    }

    /// <summary>生成器自身版本号（用于增量 salt，见 E-3）。</summary>
    internal static string GeneratorVersion => s_cachedVersion.Value;

    /// <summary>
    /// 实现类生成文件必须引用的命名空间（生成代码引用的类型均需在此可见）。
    /// <para>单一事实源：审计 F2 的成因正是 <c>ClassStructureGenerator</c> 私有静态列表、
    /// <c>HttpInvokeBaseSourceGenerator.GetFileUsingNameSpaces()</c>（又被子类 override）与
    /// BCL 实际依赖形成三份分叉。所有「实现类生成文件」的 using 头必须引用本列表。</para>
    /// </summary>
    public static readonly string[] ImplementationFileUsings =
    [
        "System",
        "System.Collections.Generic",   // Dictionary<,>（[Form]/FormUrlEncoded，审计 F2）
        "System.Globalization",         // CultureInfo.InvariantCulture（M5-HC-04 缓存键）
        "System.Linq",                  // Any/Where/Select（数组 [Query]/[ArrayQuery]，审计 F2）
        "System.Net.Http",
        "System.Text",
        "System.Text.Json",
        "System.Threading",
        "System.Threading.Tasks",
        "Microsoft.Extensions.Logging",
        "Microsoft.Extensions.Options",
        "Mud.HttpUtils",
    ];
}
