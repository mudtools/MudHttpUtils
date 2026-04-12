// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generators.Implementation;

/// <summary>
/// Token 方法生成辅助类，提取 GetTokenAsync 方法的公共前导代码
/// </summary>
internal static class TokenMethodHelper
{
    /// <summary>
    /// 生成 GetTokenAsync 方法的前导代码（获取 _appContext、tokenType、tokenManager）
    /// </summary>
    public static void GenerateGetTokenPreamble(StringBuilder codeBuilder, string accessibility)
    {
        codeBuilder.AppendLine($"        {accessibility} async Task<string> GetTokenAsync()");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            if(_appContext == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到当前服务的应用上下文。\");");
        codeBuilder.AppendLine("            var tokenType = GetTokenType();");
        codeBuilder.AppendLine("            var tokenManager = _appContext.GetTokenManager(tokenType);");
        codeBuilder.AppendLine("            if(tokenManager == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到当前服务的令牌管理器，TokenType: {tokenType}\");");
    }
}
