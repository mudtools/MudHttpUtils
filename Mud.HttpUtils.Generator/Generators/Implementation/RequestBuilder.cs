// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generators.Implementation;

/// <summary>
/// HTTP 请求构建器，负责生成 HTTP 请求的构建逻辑
/// </summary>
internal class RequestBuilder
{

    /// <summary>
    /// 生成 URL 字符串
    /// </summary>
    public string BuildUrlString(MethodAnalysisResult methodInfo)
    {
        var pathParams = methodInfo.Parameters
            .Where(p => p.Attributes.Any(attr => HttpClientGeneratorConstants.PathAttributes.Contains(attr.Name)))
            .ToList();

        var urlTemplate = methodInfo.UrlTemplate;
        var interpolatedUrl = urlTemplate;
        var hasPathParams = pathParams.Any();
        var isTokenPathMode = methodInfo.InterfaceTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModePath;

        // 处理 Token Path 模式：将 URL 中的 {tokenName} 替换为 access_token
        if (isTokenPathMode && !string.IsNullOrEmpty(methodInfo.InterfaceTokenName))
        {
            var tokenPlaceholder = $"{{{methodInfo.InterfaceTokenName}}}";
            if (interpolatedUrl.Contains(tokenPlaceholder))
            {
                interpolatedUrl = interpolatedUrl.Replace(tokenPlaceholder, "{access_token}");
            }
        }

        // 处理路径参数插值
        if (hasPathParams)
        {
            foreach (var param in pathParams)
            {
                if (interpolatedUrl.Contains($"{{{param.Name}}}"))
                {
                    var formatString = GetFormatString(param.Attributes.First(a => HttpClientGeneratorConstants.PathAttributes.Contains(a.Name)));
                    interpolatedUrl = FormatUrlParameter(interpolatedUrl, param.Name, formatString);
                }
            }
        }

        return $"            var url = $\"{interpolatedUrl}\";";
    }

    /// <summary>
    /// 生成查询参数
    /// </summary>
    public void GenerateQueryParameters(StringBuilder codeBuilder, MethodAnalysisResult methodInfo)
    {
        var queryParams = methodInfo.Parameters
            .Where(p => p.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.QueryAttribute))
            .ToList();

        var arrayQueryParams = methodInfo.Parameters
            .Where(p => p.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.ArrayQueryAttribute))
            .ToList();

        var hasTokenQuery = ShouldGenerateTokenQuery(methodInfo);

        if (!queryParams.Any() && !arrayQueryParams.Any() && !hasTokenQuery)
            return;

        codeBuilder.AppendLine($"            var queryParams = HttpUtility.ParseQueryString(string.Empty);");

        foreach (var param in queryParams)
        {
            GenerateSingleQueryParameter(codeBuilder, param);
        }

        foreach (var param in arrayQueryParams)
        {
            GenerateArrayQueryParameter(codeBuilder, param);
        }

        if (hasTokenQuery)
        {
            var tokenQueryName = GetTokenQueryName(methodInfo);
            codeBuilder.AppendLine($"            queryParams.Add(\"{tokenQueryName}\", access_token);");
        }

        codeBuilder.AppendLine("            if (queryParams.Count > 0)");
        codeBuilder.AppendLine("            {");
        codeBuilder.AppendLine("                url += \"?\" + queryParams.ToString();");
        codeBuilder.AppendLine("            }");
    }

    /// <summary>
    /// 生成请求设置
    /// </summary>
    public void GenerateRequestSetup(StringBuilder codeBuilder, MethodAnalysisResult methodInfo)
    {
        if (methodInfo.HttpMethod.Equals("patch", StringComparison.OrdinalIgnoreCase))
        {
            codeBuilder.AppendLine("#if NETSTANDARD2_0");
            codeBuilder.AppendLine("            HttpMethod httpMethod = new HttpMethod(\"PATCH\");");
            codeBuilder.AppendLine($"            using var httpRequest = new HttpRequestMessage(httpMethod, url);");
            codeBuilder.AppendLine("#else");
            codeBuilder.AppendLine($"            using var httpRequest = new HttpRequestMessage(HttpMethod.{methodInfo.HttpMethod}, url);");
            codeBuilder.AppendLine("#endif");
        }
        else
        {
            codeBuilder.AppendLine($"            using var httpRequest = new HttpRequestMessage(HttpMethod.{methodInfo.HttpMethod}, url);");
        }
    }

    /// <summary>
    /// 生成 Header 参数
    /// </summary>
    public void GenerateHeaderParameters(StringBuilder codeBuilder, MethodAnalysisResult methodInfo)
    {
        var headerParams = methodInfo.Parameters
            .Where(p => p.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.HeaderAttribute))
            .ToList();

        string? interfaceHeaderName = GetTokenHeaderName(methodInfo);

        foreach (var param in headerParams)
        {
            var headerAttr = param.Attributes.First(a => a.Name == HttpClientGeneratorConstants.HeaderAttribute);
            var headerName = headerAttr.Arguments.FirstOrDefault()?.ToString() ?? param.Name;

            var isTokenParam = param.Attributes.Any(attr => HttpClientGeneratorConstants.TokenAttributeNames.Contains(attr.Name));
            if (isTokenParam && !string.IsNullOrEmpty(interfaceHeaderName))
            {
                headerName = interfaceHeaderName;
            }

            codeBuilder.AppendLine($"            if (!string.IsNullOrEmpty({param.Name}))");
            codeBuilder.AppendLine($"                httpRequest.Headers.Add(\"{headerName}\", {param.Name});");
        }
    }

    /// <summary>
    /// 生成 Body 参数
    /// </summary>
    public void GenerateBodyParameter(StringBuilder codeBuilder, MethodAnalysisResult methodInfo, bool hasHttpClient)
    {
        var bodyParam = methodInfo.Parameters
            .FirstOrDefault(p => p.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.BodyAttribute));

        var formContentParam = methodInfo.Parameters
            .FirstOrDefault(p => p.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.FormContentAttribute));

        if (formContentParam != null)
        {
            GenerateFormContentParameter(codeBuilder, formContentParam, methodInfo);
            return;
        }

        if (bodyParam == null)
            return;

        var bodyAttr = bodyParam.Attributes.First(a => a.Name == HttpClientGeneratorConstants.BodyAttribute);
        var useStringContent = GetUseStringContentFlag(bodyAttr);
        var contentType = GetBodyContentType(bodyAttr);
        var hasExplicitContentType = bodyAttr.Arguments.Length > 0 || bodyAttr.NamedArguments.ContainsKey("ContentType");

        string contentTypeExpression;
        string? effectiveContentType = null;
        if (hasExplicitContentType)
        {
            contentTypeExpression = $"\"{contentType}\"";
            effectiveContentType = contentType;
        }
        else
        {
            effectiveContentType = methodInfo.GetEffectiveContentType();
            contentTypeExpression = !string.IsNullOrEmpty(effectiveContentType)
                ? $"\"{effectiveContentType}\""
                : "GetMediaType(_defaultContentType)";
        }

        var isXmlContentType = IsXmlContentType(effectiveContentType ?? contentType);

        if (methodInfo.BodyEnableEncrypt)
        {
            var propertyName = methodInfo.BodyEncryptPropertyName ?? "data";
            var serializeType = methodInfo.BodyEncryptSerializeType ?? "Json";
            string httpClient = "_appContext.HttpClient";
            if (hasHttpClient)
            {
                httpClient = "_httpClient";
            }

            codeBuilder.AppendLine($"            var encryptedContent = {httpClient}.EncryptContent({bodyParam.Name}, \"{propertyName}\", SerializeType.{serializeType});");
            codeBuilder.AppendLine($"            httpRequest.Content = new StringContent(encryptedContent, Encoding.UTF8, {contentTypeExpression});");
        }
        else if (useStringContent)
        {
            codeBuilder.AppendLine($"            httpRequest.Content = new StringContent({bodyParam.Name}.ToString() ?? \"\", Encoding.UTF8, {contentTypeExpression});");
        }
        else if (isXmlContentType)
        {
            codeBuilder.AppendLine($"            var xmlContent = XmlSerialize.Serialize({bodyParam.Name});");
            codeBuilder.AppendLine($"            httpRequest.Content = new StringContent(xmlContent, Encoding.UTF8, {contentTypeExpression});");
        }
        else
        {
            codeBuilder.AppendLine($"            var jsonContent = JsonSerializer.Serialize({bodyParam.Name}, _jsonSerializerOptions);");
            codeBuilder.AppendLine($"            httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, {contentTypeExpression});");
        }
    }

    /// <summary>
    /// 生成 FormContent 参数（用于 multipart/form-data）
    /// </summary>
    private void GenerateFormContentParameter(StringBuilder codeBuilder, ParameterInfo formContentParam, MethodAnalysisResult methodInfo)
    {
        // 获取 CancellationToken 参数
        var cancellationTokenParam = methodInfo.Parameters.FirstOrDefault(p => p.Type.Contains("CancellationToken"));
        var cancellationTokenArg = cancellationTokenParam?.Name ?? "default";

        codeBuilder.AppendLine($"            using var formData = await {formContentParam.Name}.GetFormDataContentAsync({cancellationTokenArg});");
        codeBuilder.AppendLine($"            httpRequest.Content = formData;");
    }

    /// <summary>
    /// 生成请求执行
    /// </summary>
    public void GenerateRequestExecution(StringBuilder codeBuilder, MethodAnalysisResult methodInfo, string cancellationTokenArg, bool hasHttpClient)
    {
        var filePathParam = methodInfo.Parameters.Where(p => p.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.FilePathAttribute)).FirstOrDefault();

        var deserializeType = methodInfo.IsAsyncMethod ? methodInfo.AsyncInnerReturnType : methodInfo.ReturnType;
        codeBuilder.AppendLine();
        string httpClient = "_appContext.HttpClient";
        if (hasHttpClient)
        {
            httpClient = "_httpClient";
        }
        if (filePathParam != null)
        {
            codeBuilder.AppendLine($"            await {httpClient}.DownloadLargeAsync(httpRequest, {filePathParam.Name}{cancellationTokenArg});");
        }
        else
        {
            if (TypeDetectionHelper.IsByteArrayType(deserializeType))
            {
                codeBuilder.AppendLine($"            return await {httpClient}.DownloadAsync(httpRequest{cancellationTokenArg});");
            }
            else
            {
                var responseContentType = methodInfo.ResponseContentType;
                var isXmlResponse = IsXmlContentType(responseContentType);

                if (isXmlResponse)
                {
                    codeBuilder.AppendLine($"            return await {httpClient}.SendXmlAsync<{deserializeType}>(httpRequest, null{cancellationTokenArg});");
                }
                else
                {
                    codeBuilder.AppendLine($"            return await {httpClient}.SendAsync<{deserializeType}>(httpRequest{cancellationTokenArg});");
                }
            }
        }
    }

    #region 辅助方法

    private bool ShouldGenerateTokenQuery(MethodAnalysisResult methodInfo)
    {
        if (!string.IsNullOrEmpty(methodInfo.InterfaceTokenInjectionMode) &&
            methodInfo.InterfaceTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModeQuery)
            return true;

        return methodInfo.InterfaceAttributes?.Any(attr => attr.StartsWith("Query:", StringComparison.Ordinal)) == true;
    }

    private string GetTokenQueryName(MethodAnalysisResult methodInfo)
    {
        if (!string.IsNullOrEmpty(methodInfo.InterfaceTokenName))
            return methodInfo.InterfaceTokenName;

        var queryAttr = methodInfo.InterfaceAttributes?.FirstOrDefault(attr => attr.StartsWith("Query:", StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(queryAttr))
            return queryAttr.Substring(6);

        return "access_token";
    }

    internal string? GetTokenHeaderName(MethodAnalysisResult methodInfo)
    {
        if (!string.IsNullOrEmpty(methodInfo.InterfaceTokenInjectionMode) &&
            methodInfo.InterfaceTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModeHeader &&
            !string.IsNullOrEmpty(methodInfo.InterfaceTokenName))
            return methodInfo.InterfaceTokenName;

        var headerAttr = methodInfo.InterfaceAttributes?.FirstOrDefault(attr => attr.StartsWith("Header:", StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(headerAttr))
            return headerAttr.Substring(7);

        return null;
    }

    private string FormatUrlParameter(string url, string paramName, string? formatString)
    {
        return string.IsNullOrEmpty(formatString)
            ? url
            : url.Replace($"{{{paramName}}}", $"{{{paramName}.ToString(\"{formatString}\")}}");
    }

    private void GenerateSingleQueryParameter(StringBuilder codeBuilder, ParameterInfo param)
    {
        var queryAttr = param.Attributes.First(a => a.Name == HttpClientGeneratorConstants.QueryAttribute);
        var paramName = GetQueryParameterName(queryAttr, param.Name);
        var formatString = GetFormatString(queryAttr);

        if (TypeDetectionHelper.IsSimpleType(param.Type))
        {
            GenerateSimpleQueryParameter(codeBuilder, param, paramName, formatString);
        }
        else
        {
            GenerateComplexQueryParameter(codeBuilder, param);
        }
    }

    private void GenerateArrayQueryParameter(StringBuilder codeBuilder, ParameterInfo param)
    {
        var arrayQueryAttr = param.Attributes.First(a => a.Name == HttpClientGeneratorConstants.ArrayQueryAttribute);
        var paramName = GetQueryParameterName(arrayQueryAttr, param.Name);
        var separator = GetArrayQuerySeparator(arrayQueryAttr);

        codeBuilder.AppendLine($"            if ({param.Name} != null && {param.Name}.Any())");
        codeBuilder.AppendLine("            {");

        if (string.IsNullOrEmpty(separator))
        {
            // 使用重复键名格式：user_ids=id0&user_ids=id1&user_ids=id2
            codeBuilder.AppendLine($"                foreach (var item in {param.Name})");
            codeBuilder.AppendLine("                {");
            codeBuilder.AppendLine($"                    if (item != null)");
            codeBuilder.AppendLine("                    {");
            codeBuilder.AppendLine($"                        var encodedValue = HttpUtility.UrlEncode(item.ToString());");
            codeBuilder.AppendLine($"                        queryParams.Add(\"{paramName}\", encodedValue);");
            codeBuilder.AppendLine("                    }");
            codeBuilder.AppendLine("                }");
        }
        else
        {
            // 使用分隔符连接格式：user_ids=id0;id1;id2
            codeBuilder.AppendLine($"                var joinedValues = string.Join(\"{separator}\", {param.Name}.Where(item => item != null).Select(item => HttpUtility.UrlEncode(item.ToString())));");
            codeBuilder.AppendLine($"                queryParams.Add(\"{paramName}\", joinedValues);");
        }

        codeBuilder.AppendLine("            }");
    }

    private void GenerateSimpleQueryParameter(StringBuilder codeBuilder, ParameterInfo param, string paramName, string? formatString)
    {
        if (TypeDetectionHelper.IsArrayType(param.Type))
        {
            // 处理数组类型：使用默认分号分隔符格式
            codeBuilder.AppendLine($"            if ({param.Name} != null && {param.Name}.Any())");
            codeBuilder.AppendLine("            {");
            codeBuilder.AppendLine($"                var joinedValues = string.Join(\";\", {param.Name}.Where(item => item != null).Select(item => HttpUtility.UrlEncode(item.ToString())));");
            codeBuilder.AppendLine($"                queryParams.Add(\"{paramName}\", joinedValues);");
            codeBuilder.AppendLine("            }");
        }
        else if (TypeDetectionHelper.IsStringType(param.Type))
        {
            codeBuilder.AppendLine($"            if (!string.IsNullOrEmpty({param.Name}))");
            codeBuilder.AppendLine("            {");
            codeBuilder.AppendLine($"                var encodedValue = HttpUtility.UrlEncode({param.Name});");
            codeBuilder.AppendLine($"                queryParams.Add(\"{paramName}\", encodedValue);");
            codeBuilder.AppendLine("            }");
        }
        else
        {
            if (TypeDetectionHelper.IsNullableType(param.Type))
            {
                codeBuilder.AppendLine($"            if ({param.Name}.HasValue)");
                var formatExpression = !string.IsNullOrEmpty(formatString)
                   ? $".Value.ToString(\"{formatString}\")"
                   : ".Value.ToString()";
                codeBuilder.AppendLine($"                queryParams.Add(\"{paramName}\", {param.Name}{formatExpression});");
            }
            else
            {
                var formatExpression = !string.IsNullOrEmpty(formatString)
                  ? $".ToString(\"{formatString}\")"
                  : ".ToString()";
                codeBuilder.AppendLine($"            queryParams.Add(\"{paramName}\", {param.Name}{formatExpression});");
            }
        }
    }

    private void GenerateComplexQueryParameter(StringBuilder codeBuilder, ParameterInfo param)
    {
        codeBuilder.AppendLine($"            if ({param.Name} != null)");
        codeBuilder.AppendLine("            {");
        codeBuilder.AppendLine($"                var properties = {param.Name}.GetType().GetProperties();");
        codeBuilder.AppendLine("                foreach (var prop in properties)");
        codeBuilder.AppendLine("                {");
        codeBuilder.AppendLine($"                    var value = prop.GetValue({param.Name});");
        codeBuilder.AppendLine("                    if (value != null)");
        codeBuilder.AppendLine("                    {");
        codeBuilder.AppendLine($"                        queryParams.Add(prop.Name, HttpUtility.UrlEncode(value.ToString()));");
        codeBuilder.AppendLine("                    }");
        codeBuilder.AppendLine("                }");
        codeBuilder.AppendLine("            }");
    }

    private string GetFormatString(ParameterAttributeInfo attribute)
    {
        // 检查构造函数参数
        if (attribute.Arguments.Length > 1)
        {
            return attribute.Arguments[1] as string ?? "";
        }
        else if (attribute.Arguments.Length == 1 && HttpClientGeneratorConstants.PathAttributes.Contains(attribute.Name))
        {
            return attribute.Arguments[0] as string ?? "";
        }

        // 检查命名参数
        return attribute.NamedArguments.TryGetValue("FormatString", out var formatString)
            ? formatString as string
            : null;
    }

    private string GetQueryParameterName(ParameterAttributeInfo attribute, string defaultName)
    {
        if (attribute.Arguments.Length > 0)
        {
            var nameArg = attribute.Arguments[0] as string;
            if (!string.IsNullOrEmpty(nameArg))
                return nameArg;
        }

        return attribute.NamedArguments.TryGetValue("Name", out var nameNamedArg)
            ? nameNamedArg as string ?? defaultName
            : defaultName;
    }

    private string? GetArrayQuerySeparator(ParameterAttributeInfo attribute)
    {
        // 检查构造函数参数
        if (attribute.Arguments.Length > 1)
        {
            return attribute.Arguments[1] as string;
        }

        // 检查命名参数
        return attribute.NamedArguments.TryGetValue("Separator", out var separator)
            ? separator as string
            : null;
    }

    private string GetBodyContentType(ParameterAttributeInfo bodyAttr)
    {
        // 先检查构造函数参数（如 [Body("application/xml")]）
        if (bodyAttr.Arguments.Length > 0)
        {
            var ctorContentType = bodyAttr.Arguments[0]?.ToString();
            if (!string.IsNullOrEmpty(ctorContentType))
                return ctorContentType;
        }

        // 再检查命名参数（如 [Body(ContentType = "application/xml")]）
        return bodyAttr.NamedArguments.TryGetValue("ContentType", out var contentTypeArg)
            ? (contentTypeArg?.ToString() ?? "application/json")
            : "application/json";
    }

    private bool GetUseStringContentFlag(ParameterAttributeInfo bodyAttr)
    {
        if (!bodyAttr.NamedArguments.TryGetValue("UseStringContent", out var useStringContentArg))
            return false;

        return bool.TryParse(useStringContentArg?.ToString(), out var result) && result;
    }

    /// <summary>
    /// 检查内容类型是否为XML
    /// </summary>
    /// <param name="contentType">内容类型字符串</param>
    /// <returns>如果是XML类型返回true，否则返回false</returns>
    private bool IsXmlContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return false;

        // 检查是否包含xml（不区分大小写）
        return contentType.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    #endregion
}
