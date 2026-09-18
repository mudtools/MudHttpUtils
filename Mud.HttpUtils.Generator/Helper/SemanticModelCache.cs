// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 语义模型缓存管理器，提供线程安全的语义模型缓存功能
/// </summary>
/// <remarks>
/// 使用嵌套 ConditionalWeakTable + ConcurrentDictionary 实现缓存：
/// - 外层以 Compilation 为主键的 ConditionalWeakTable，确保 Compilation 变化时缓存自动失效，
///   避免 Incremental 编译场景下返回过期的 SemanticModel；Compilation 被 GC 回收时关联缓存自动释放。
/// - 内层以 SyntaxTree 为键的 ConcurrentDictionary，利用 GetOrAdd 原子操作消除 TOCTOU 竞态。
///   每个 Compilation 持有独立的内层字典，避免跨 Compilation 的 SemanticModel 串用，
///   因此原 ConditionalWeakTable 实现中的 model.Compilation == compilation 校验不再必要。
/// <para>
/// 缓存大小与活动 Compilation 数量成正比：每个活动 Compilation 持有一个内层字典，
/// 其大小与该编译中的 SyntaxTree 数量成正比。由于外层为 ConditionalWeakTable（弱引用），
/// Compilation 被 GC 回收后关联缓存自动释放，不会在 IDE 长会话中无限增长。
/// </para>
/// </remarks>
internal static class SemanticModelCache
{
    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<SyntaxTree, SemanticModel>> _cache = new();

    /// <summary>
    /// 获取或创建语义模型
    /// </summary>
    /// <param name="compilation">编译对象</param>
    /// <param name="syntaxTree">语法树</param>
    /// <returns>语义模型</returns>
    /// <exception cref="ArgumentNullException">当 compilation 或 syntaxTree 为 null 时抛出</exception>
    public static SemanticModel GetOrCreate(Compilation compilation, SyntaxTree syntaxTree)
    {
        if (!TryGet(compilation, syntaxTree, out var semanticModel))
            throw new ArgumentException(
                $"编译中不包含 SyntaxTree '{syntaxTree.FilePath}'。请使用 <see cref=\"TryGet\"/> 处理可降级场景。",
                nameof(syntaxTree));
        return semanticModel!;
    }

    /// <summary>
    /// 尝试获取（或创建）语义模型，语法树不属于当前编译时返回 <c>false</c> 而不是抛出异常。
    /// </summary>
    /// <remarks>
    /// [HTTPCLIENT004 误报修复] Roslyn 的 <c>Compilation.GetSemanticModel</c> 在传入不属于该编译的
    /// SyntaxTree 时会抛出 <see cref="ArgumentException"/>（paramName 为 "syntaxTree"）。在 IDE 增量
    /// 生成场景下，缓存的 <c>InterfaceModel</c> 可能携带旧编译的 SemanticModel/语法树，当派生分析
    /// （基接口语法定位、方法语法匹配等）把来自不同代际编译的语法树与当前编译组合时即触发该异常，
    /// 经 <c>HandleInterfaceProcessingException</c> 包装为误导性的 HTTPCLIENT004「参数配置错误」。
    /// 语法树不在当前编译中属于可降级场景：调用方应走符号侧降级路径（返回 null / 跳过该子树），
    /// 而不是让整个接口生成失败。
    /// </remarks>
    /// <param name="compilation">编译对象</param>
    /// <param name="syntaxTree">语法树</param>
    /// <param name="semanticModel">获取到的语义模型；返回 <c>false</c> 时为 <c>null</c></param>
    /// <returns>语法树属于当前编译且成功获取语义模型时为 <c>true</c></returns>
    public static bool TryGet(Compilation compilation, SyntaxTree syntaxTree, out SemanticModel? semanticModel)
    {
        if (compilation == null)
            throw new ArgumentNullException(nameof(compilation));
        if (syntaxTree == null)
            throw new ArgumentNullException(nameof(syntaxTree));

        // 包含性校验：Compilation.GetSemanticModel 对外部语法树抛 ArgumentException 的唯一前提。
        // 此处提前短路并降级，避免异常沿生成管道上抛伪装成用户代码错误（HTTPCLIENT004 误报）。
        if (!compilation.ContainsSyntaxTree(syntaxTree))
        {
            GeneratorDebugLogger.LogError("SemanticModelCache.SyntaxTreeNotInCompilation", new InvalidOperationException(
                $"SyntaxTree '{syntaxTree.FilePath}' 不在当前编译 '{compilation.AssemblyName}' 中，已降级处理（跳过语法侧分析）。"));
            semanticModel = null;
            return false;
        }

        var innerDict = _cache.GetOrCreateValue(compilation);
        semanticModel = innerDict.GetOrAdd(syntaxTree, tree => compilation.GetSemanticModel(tree));
        return true;
    }
}
