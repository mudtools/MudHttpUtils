// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Diagnostics;

namespace Mud.CodeGenerator;

/// <summary>
/// 错误处理器，统一管理代码生成器的错误处理
/// </summary>
internal static class ErrorHandler
{
    /// <summary>
    /// 报告错误诊断信息
    /// </summary>
    /// <param name="context">源生成上下文</param>
    /// <param name="descriptor">诊断描述符</param>
    /// <param name="args">诊断参数</param>
    public static void ReportError(SourceProductionContext context, DiagnosticDescriptor descriptor, params object[] args)
    {
        var diagnostic = Diagnostic.Create(descriptor, Location.None, args);
        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// 报告警告诊断信息
    /// </summary>
    /// <param name="context">源生成上下文</param>
    /// <param name="descriptor">诊断描述符</param>
    /// <param name="args">诊断参数</param>
    public static void ReportWarning(SourceProductionContext context, DiagnosticDescriptor descriptor, params object[] args)
    {
        var diagnostic = Diagnostic.Create(descriptor, Location.None, args);
        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// 报告信息诊断信息
    /// </summary>
    /// <param name="context">源生成上下文</param>
    /// <param name="descriptor">诊断描述符</param>
    /// <param name="args">诊断参数</param>
    public static void ReportInfo(SourceProductionContext context, DiagnosticDescriptor descriptor, params object[] args)
    {
        var diagnostic = Diagnostic.Create(descriptor, Location.None, args);
        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// 安全执行代码生成操作
    /// </summary>
    /// <param name="context">源生成上下文</param>
    /// <param name="className">类名</param>
    /// <param name="action">要执行的操作</param>
    /// <param name="errorDescriptor">错误描述符</param>
    public static void SafeExecute(
        SourceProductionContext context,
        string className,
        Action action,
        DiagnosticDescriptor errorDescriptor = null)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            var descriptor = errorDescriptor ?? Diagnostics.EntityMethodGenerationError;
            ReportError(context, descriptor, className, ex.Message);
            Debug.WriteLine($"代码生成错误 - {className}: {ex.Message}");
        }
    }

    /// <summary>
    /// 安全执行代码生成操作并返回结果
    /// </summary>
    /// <typeparam name="T">返回类型</typeparam>
    /// <param name="context">源生成上下文</param>
    /// <param name="className">类名</param>
    /// <param name="func">要执行的函数</param>
    /// <param name="errorDescriptor">错误描述符</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>执行结果或默认值</returns>
    public static T SafeExecute<T>(
        SourceProductionContext context,
        string className,
        Func<T> func,
        DiagnosticDescriptor errorDescriptor = null,
        T defaultValue = default)
    {
        try
        {
            return func();
        }
        catch (Exception ex)
        {
            var descriptor = errorDescriptor ?? Diagnostics.EntityMethodGenerationError;
            ReportError(context, descriptor, className, ex.Message);
            Debug.WriteLine($"代码生成错误 - {className}: {ex.Message}");
            return defaultValue;
        }
    }

    /// <summary>
    /// 记录调试信息
    /// </summary>
    /// <param name="message">调试消息</param>
    /// <param name="args">消息参数</param>
    public static void LogDebug(string message, params object[] args)
    {
        Debug.WriteLine($"[DEBUG] {string.Format(message, args)}");
    }

    /// <summary>
    /// 记录信息
    /// </summary>
    /// <param name="message">信息消息</param>
    /// <param name="args">消息参数</param>
    public static void LogInfo(string message, params object[] args)
    {
        Debug.WriteLine($"[INFO] {string.Format(message, args)}");
    }

    /// <summary>
    /// 记录警告
    /// </summary>
    /// <param name="message">警告消息</param>
    /// <param name="args">消息参数</param>
    public static void LogWarning(string message, params object[] args)
    {
        Debug.WriteLine($"[WARN] {string.Format(message, args)}");
    }

    /// <summary>
    /// 记录错误
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="args">消息参数</param>
    public static void LogError(string message, params object[] args)
    {
        Debug.WriteLine($"[ERROR] {string.Format(message, args)}");
    }

    /// <summary>
    /// 创建安全的属性生成器
    /// </summary>
    /// <typeparam name="TGenerator">生成器类型</typeparam>
    /// <typeparam name="TInput">输入类型</typeparam>
    /// <typeparam name="TOutput">输出类型</typeparam>
    /// <param name="generator">生成器实例</param>
    /// <param name="propertyGenerator">属性生成委托</param>
    /// <returns>安全的属性生成委托</returns>
    public static Func<TInput, TOutput> CreateSafePropertyGenerator<TGenerator, TInput, TOutput>(
        TGenerator generator,
        Func<TInput, TOutput> propertyGenerator)
        where TGenerator : class
    {
        return input =>
        {
            try
            {
                return propertyGenerator(input);
            }
            catch (Exception ex)
            {
                LogError($"属性生成失败: {ex.Message}");
                // 返回默认值或空值，避免整个生成过程失败
                return default;
            }
        };
    }

    /// <summary>
    /// 验证生成结果
    /// </summary>
    /// <param name="context">源码生成上下文</param>
    /// <param name="generatedNode">生成的语法节点</param>
    /// <param name="className">类名</param>
    /// <param name="failureDescriptor">失败描述符</param>
    /// <returns>验证是否通过</returns>
    public static bool ValidateGenerationResult(
        SourceProductionContext context,
        SyntaxNode generatedNode,
        string className,
        DiagnosticDescriptor failureDescriptor)
    {
        if (generatedNode == null)
        {
            ReportError(context, failureDescriptor, className);
            return false;
        }
        return true;
    }
}