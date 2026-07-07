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
        if (compilation == null)
            throw new ArgumentNullException(nameof(compilation));
        if (syntaxTree == null)
            throw new ArgumentNullException(nameof(syntaxTree));

        var innerDict = _cache.GetOrCreateValue(compilation);
        return innerDict.GetOrAdd(syntaxTree, tree => compilation.GetSemanticModel(tree));
    }
}
