// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Analyzers;
using Mud.HttpUtils.Generators.Base;
using Mud.HttpUtils.Generators.Context;

namespace Mud.HttpUtils.Generators.Implementation;

/// <summary>
/// 方法生成器，负责生成接口方法的实现代码
/// </summary>
internal class MethodGenerator : ICodeFragmentGenerator
{
    private readonly RequestBuilder _requestBuilder;

    public MethodGenerator()
    {
        _requestBuilder = new RequestBuilder();
    }

    /// <summary>
    /// 生成方法实现代码
    /// </summary>
    public void Generate(StringBuilder codeBuilder, GeneratorContext context)
    {
        IEnumerable<IMethodSymbol> methodsToGenerate = GetMethodsToGenerate(context);
        var isAbstractClass = context.Configuration.IsAbstract;

        foreach (var methodSymbol in methodsToGenerate)
        {
            var isHttpMethod = MethodAnalyzer.FindHttpMethodAttributeFromSymbol(methodSymbol) != null;
            if (!isHttpMethod)
                continue;

            GenerateMethodImplementation(codeBuilder, context, methodSymbol, isAbstractClass);
        }
    }

    /// <summary>
    /// 获取需要生成的方法列表
    /// - 有 InheritedFrom：只获取当前接口自身定义的方法（父接口方法由基类生成）
    /// - 无 InheritedFrom：获取当前接口及所有父接口的方法（需实现全部接口方法）
    /// 函数在哪个接口定义就在哪个生成类中生成，InheritedFrom场景下父接口方法由基类负责
    /// </summary>
    private IEnumerable<IMethodSymbol> GetMethodsToGenerate(GeneratorContext context)
    {
        if (context.HasInheritedFrom)
        {
            return context.InterfaceSymbol.GetMembers().OfType<IMethodSymbol>();
        }

        try
        {
            return TypeSymbolHelper.GetAllMethods(context.InterfaceSymbol, true);
        }
        catch
        {
            return context.InterfaceSymbol.GetMembers().OfType<IMethodSymbol>();
        }
    }

    /// <summary>
    /// 生成单个方法的实现代码
    /// </summary>
    private void GenerateMethodImplementation(StringBuilder codeBuilder, GeneratorContext context, IMethodSymbol methodSymbol, bool isVirtual = false)
    {
        var methodInfo = MethodAnalyzer.AnalyzeMethod(
            context.Compilation,
            methodSymbol,
            context.InterfaceDeclaration,
            context.SemanticModel);

        if (!methodInfo.IsValid) return;

        if (!string.IsNullOrEmpty(methodInfo.UrlTemplate) &&
            !CSharpCodeValidator.IsValidUrlTemplate(methodInfo.UrlTemplate, out var urlError))
        {
            context.ProductionContext.ReportDiagnostic(
                Diagnostic.Create(
                    Diagnostics.HttpClientInvalidUrlTemplate,
                    context.InterfaceDeclaration.GetLocation(),
                    context.InterfaceDeclaration.Identifier.Text,
                    methodInfo.UrlTemplate,
                    urlError));
            return;
        }

        if (methodInfo.IgnoreImplement) return;

        var hasTokenManager = !string.IsNullOrEmpty(context.Configuration.TokenManager);
        var hasHttpClient = !string.IsNullOrEmpty(context.Configuration.HttpClient);
        var needsTokenInjection = ShouldInjectToken(methodInfo, hasTokenManager, hasHttpClient);

        codeBuilder.AppendLine();
        codeBuilder.AppendLine($"        /// <summary>");
        codeBuilder.AppendLine($"        /// <inheritdoc />");
        codeBuilder.AppendLine($"        /// </summary>");
        codeBuilder.AppendLine($"        {GeneratedCodeConsts.HttpGeneratedCodeAttribute}");
        var asyncKeyword = methodInfo.IsAsyncMethod ? "async " : "";
        var virtualKeyword = isVirtual ? "virtual " : "";
        var returnTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
        var returnType = methodSymbol.ReturnType.ToDisplayString(returnTypeFormat);
        codeBuilder.AppendLine($"        public {virtualKeyword}{asyncKeyword}{returnType} {methodSymbol.Name}({TypeSymbolHelper.GetParameterList(methodSymbol)})");
        codeBuilder.AppendLine("        {");

        if (needsTokenInjection)
        {
            codeBuilder.AppendLine($"            var access_token = await GetTokenAsync();");
        }

        ParameterValidationHelper.GenerateParameterValidation(codeBuilder, methodInfo.Parameters);

        codeBuilder.AppendLine();

        var urlCode = _requestBuilder.BuildUrlString(methodInfo);
        codeBuilder.AppendLine(urlCode);

        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        _requestBuilder.GenerateRequestSetup(codeBuilder, methodInfo);
        _requestBuilder.GenerateHeaderParameters(codeBuilder, methodInfo);
        codeBuilder.AppendLine();
        _requestBuilder.GenerateBodyParameter(codeBuilder, methodInfo, hasHttpClient);

        if (needsTokenInjection && IsTokenHeaderMode(methodInfo))
        {
            var headerName = GetTokenHeaderName(methodInfo);
            codeBuilder.AppendLine($"            httpRequest.Headers.Add(\"{headerName}\", access_token);");
        }

        if (methodInfo.InterfaceHeaderAttributes?.Any() == true)
        {
            GenerateInterfaceHeaders(codeBuilder, context, methodInfo);
        }

        var cancellationTokenArg = GetCancellationTokenParams(methodInfo);
        _requestBuilder.GenerateRequestExecution(codeBuilder, methodInfo, cancellationTokenArg, hasHttpClient);

        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();
    }

    /// <summary>
    /// 获取 CancellationToken 参数
    /// </summary>
    private string GetCancellationTokenParams(MethodAnalysisResult methodInfo)
    {
        var cancellationTokenParam = methodInfo.Parameters.FirstOrDefault(
            p => p.Type.Contains("CancellationToken"));
        var paramValue = cancellationTokenParam?.Name;

        return paramValue != null ? $", cancellationToken: {paramValue}" : "";
    }

    /// <summary>
    /// 生成接口定义的Header代码
    /// </summary>
    private void GenerateInterfaceHeaders(StringBuilder codeBuilder, GeneratorContext context, MethodAnalysisResult methodInfo)
    {
        var hasTokenManager = !string.IsNullOrEmpty(context.Configuration.TokenManager);
        var hasAuthorizationHeader = TypeSymbolHelper.HasPropertyAttribute(
            context.InterfaceSymbol!, "Header", "Authorization");

        foreach (var interfaceHeader in methodInfo.InterfaceHeaderAttributes)
        {
            if (string.IsNullOrEmpty(interfaceHeader.Name))
                continue;

            if (hasTokenManager && hasAuthorizationHeader &&
                interfaceHeader.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (interfaceHeader.Value == null)
            {
                continue;
            }

            var headerValue = interfaceHeader.Value?.ToString() ?? "null";
            var escapedHeaderValue = headerValue.Replace("\\", "\\\\").Replace("\"", "\\\"");

            if (interfaceHeader.Replace)
            {
                codeBuilder.AppendLine($"            // 替换接口定义的Header: {interfaceHeader.Name}");
                codeBuilder.AppendLine($"            if (httpRequest.Headers.Contains(\"{interfaceHeader.Name}\"))");
                codeBuilder.AppendLine($"                httpRequest.Headers.Remove(\"{interfaceHeader.Name}\");");
                codeBuilder.AppendLine($"            httpRequest.Headers.Add(\"{interfaceHeader.Name}\", \"{escapedHeaderValue}\");");
            }
            else
            {
                codeBuilder.AppendLine($"            // 添加接口定义的Header: {interfaceHeader.Name}");
                codeBuilder.AppendLine($"            httpRequest.Headers.Add(\"{interfaceHeader.Name}\", \"{escapedHeaderValue}\");");
            }
        }
    }

    private bool ShouldInjectToken(MethodAnalysisResult methodInfo, bool hasTokenManager, bool hasHttpClient)
    {
        // HttpClient 模式下不注入 Token
        if (hasHttpClient)
            return false;

        if (!hasTokenManager)
            return false;

        if (!string.IsNullOrEmpty(methodInfo.InterfaceTokenInjectionMode))
            return true;

        return methodInfo.InterfaceAttributes?.Any(attr =>
            attr.StartsWith("Header:", StringComparison.Ordinal) ||
            attr.StartsWith("Query:", StringComparison.Ordinal)) == true;
    }

    private bool IsTokenHeaderMode(MethodAnalysisResult methodInfo)
    {
        if (!string.IsNullOrEmpty(methodInfo.InterfaceTokenInjectionMode))
            return methodInfo.InterfaceTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModeHeader;

        return methodInfo.InterfaceAttributes?.Any(attr => attr.StartsWith("Header:", StringComparison.Ordinal)) == true;
    }

    private string GetTokenHeaderName(MethodAnalysisResult methodInfo)
    {
        return _requestBuilder.GetTokenHeaderName(methodInfo) ?? "Authorization";
    }
}
