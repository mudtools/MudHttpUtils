// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// 测试辅助工具类，用于获取 Mud.HttpUtils.Generator 程序集中的内部类型
/// </summary>
public static class TestHelper
{
    private static readonly Assembly GeneratorAssembly;

    static TestHelper()
    {
        var assemblyName = "Mud.HttpUtils.Generator";
        
        GeneratorAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == assemblyName);

        if (GeneratorAssembly == null)
        {
            var assemblyPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                $"{assemblyName}.dll");
            
            if (File.Exists(assemblyPath))
            {
                GeneratorAssembly = Assembly.LoadFrom(assemblyPath);
            }
            else
            {
                throw new InvalidOperationException(
                    $"无法找到 {assemblyName} 程序集。搜索路径: {assemblyPath}");
            }
        }
    }

    public static Type GetType(string typeName)
    {
        return GeneratorAssembly.GetType(typeName)
            ?? throw new InvalidOperationException($"无法在 Mud.HttpUtils.Generator 程序集中找到类型: {typeName}");
    }

    public static MethodInfo GetMethod(Type type, string methodName, BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.Static)
    {
        return type.GetMethod(methodName, bindingFlags)
            ?? throw new InvalidOperationException($"无法在类型 {type.Name} 中找到方法: {methodName}");
    }
}
