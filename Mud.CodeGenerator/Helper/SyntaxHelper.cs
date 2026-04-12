// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.CodeGenerator;

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
        {
            return false;
        }

        syntaxNode = syntaxNode.Parent;
        if (syntaxNode == null)
        {
            return false;
        }

        if (syntaxNode.GetType() == typeof(T))
        {
            result = syntaxNode as T;
            return true;
        }
        return TryGetParentSyntax(syntaxNode, out result);
    }

    /// <summary>
    /// 根据<see cref="ClassDeclarationSyntax"/>获取当前类所在的命名空间。
    /// </summary>
    /// <param name="classNode">类声明语法节点。</param>
    /// <returns>命名空间名称。</returns>
    public static string GetNamespaceName(TypeDeclarationSyntax classNode, string extNamespace = "")
    {
        var result = "";
        if (TryGetParentSyntax(classNode, out NamespaceDeclarationSyntax namespaceDeclarationSyntax))
        {
            result = namespaceDeclarationSyntax.Name.ToString();
        }
        else if (TryGetParentSyntax(classNode, out FileScopedNamespaceDeclarationSyntax fileScopedNamespaceDeclaration))
        {
            result = fileScopedNamespaceDeclaration.Name.ToString();
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
