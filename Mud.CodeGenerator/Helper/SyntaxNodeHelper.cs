// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.CodeGenerator;

internal static class SyntaxNodeHelper
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
    /// 获取类的全路径名。
    /// </summary>
    /// <param name="varClassDec"></param>
    /// <returns></returns>
    public static string ClassFullName(this ClassDeclarationSyntax varClassDec)
    {
        SyntaxNode tempCurCls = varClassDec;
        var tempFullName = new Stack<string>();

        do
        {
            if (tempCurCls.IsKind(SyntaxKind.ClassDeclaration))
            {
                tempFullName.Push(((ClassDeclarationSyntax)tempCurCls).Identifier.ToString());
            }
            else if (tempCurCls.IsKind(SyntaxKind.NamespaceDeclaration))
            {
                tempFullName.Push(((NamespaceDeclarationSyntax)tempCurCls).Name.ToString());
            }
            else if (tempCurCls.IsKind(SyntaxKind.FileScopedNamespaceDeclaration))
            {
                tempFullName.Push(((FileScopedNamespaceDeclarationSyntax)tempCurCls).Name.ToString());
            }

            tempCurCls = tempCurCls.Parent;
        } while (tempCurCls != null);

        return string.Join(".", tempFullName);
    }
}