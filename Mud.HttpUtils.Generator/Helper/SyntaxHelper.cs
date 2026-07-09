// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

internal static class SyntaxHelper
{
    /// <summary>
    /// 获取指定的路径节点名。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="syntaxNode"></param>
    /// <param name="result"></param>
    /// <returns></returns>
    public static bool TryGetParentSyntax<T>(SyntaxNode syntaxNode, out T result)
        where T : SyntaxNode
    {
        result = null;
        if (syntaxNode == null)
            return false;

        var current = syntaxNode.Parent;
        while (current != null)
        {
            if (current is T typedNode)
            {
                result = typedNode;
                return true;
            }
            current = current.Parent;
        }
        return false;
    }

    /// <summary>
    /// 根据<see cref="TypeDeclarationSyntax"/>获取当前类型所在的完整命名空间。
    /// </summary>
    /// <param name="classNode">类型声明语法节点。</param>
    /// <param name="extNamespace">要追加的扩展命名空间后缀（如 Implementation 子命名空间）。</param>
    /// <returns>完整命名空间名称。</returns>
    /// <remarks>
    /// 正确处理嵌套命名空间块声明（如 <c>namespace A { namespace B { ... } }</c>），
    /// 从内层到外层逐级拼接，返回完整的限定命名空间 <c>A.B</c>。
    /// 对于单块声明 <c>namespace A.B.C { }</c>，Name 本身已是完整限定名，直接返回。
    /// </remarks>
    public static string GetNamespaceName(TypeDeclarationSyntax classNode, string extNamespace = "")
    {
        var result = "";

        // 从内层到外层收集所有命名空间祖先，用 Stack 保证外层在前
        var namespaceParts = new Stack<string>();
        var current = classNode.Parent;
        while (current != null)
        {
            if (current is NamespaceDeclarationSyntax ns)
            {
                // NamespaceDeclarationSyntax.Name 可能本身是限定的（如 A.B），直接使用完整名称
                namespaceParts.Push(ns.Name.ToString());
            }
            else if (current is FileScopedNamespaceDeclarationSyntax fileNs)
            {
                // 文件范围命名空间不可嵌套，找到即停止
                namespaceParts.Push(fileNs.Name.ToString());
                break;
            }
            current = current.Parent;
        }

        if (namespaceParts.Count > 0)
        {
            result = string.Join(".", namespaceParts);
        }

        if (!string.IsNullOrEmpty(extNamespace))
        {
            if (!string.IsNullOrEmpty(result))
            {
                result += ".";
            }
            result += extNamespace;
        }
        return result;
    }
}
