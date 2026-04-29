// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
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
    public string BuildUrlString(MethodAnalysisResult methodInfo, string? basePath = null)
    {
        var pathParams = methodInfo.Parameters
            .Where(p => p.Attributes.Any(attr => HttpClientGeneratorConstants.PathAttributes.Contains(attr.Name)))
            .ToList();

        var urlTemplate = methodInfo.UrlTemplate;

        // 规则1：如果是绝对 URL，直接使用
        if (urlTemplate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            urlTemplate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return BuildUrlWithPlaceholders(urlTemplate, pathParams, methodInfo);
        }

        // 规则2：如果以 / 开头，忽略 BasePath
        if (urlTemplate.StartsWith("/"))
        {
            return BuildUrlWithPlaceholders(urlTemplate, pathParams, methodInfo);
        }

        // 规则3：正常情况，拼接 BasePath
        if (!string.IsNullOrEmpty(basePath))
        {
            var normalizedBasePath = basePath.TrimEnd('/');
            var normalizedUrlTemplate = urlTemplate;
            var combinedPath = $"{normalizedBasePath}/{normalizedUrlTemplate}";
            return BuildUrlWithPlaceholders(combinedPath, pathParams, methodInfo);
        }

        return BuildUrlWithPlaceholders(urlTemplate, pathParams, methodInfo);
    }

    /// <summary>
    /// 生成带占位符替换的 URL 字符串
    /// </summary>
    private string BuildUrlWithPlaceholders(string urlTemplate, List<ParameterInfo> pathParams, MethodAnalysisResult? methodInfo = null)
    {
        var interpolatedUrl = urlTemplate;
        var hasPathParams = pathParams.Any();

        if (methodInfo != null)
        {
            var isTokenPathMode = methodInfo.InterfaceTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModePath;
            if (isTokenPathMode && !string.IsNullOrEmpty(methodInfo.InterfaceTokenName))
            {
                var tokenPlaceholder = $"{{{methodInfo.InterfaceTokenName}}}";
                if (interpolatedUrl.Contains(tokenPlaceholder))
                {
                    interpolatedUrl = interpolatedUrl.Replace(tokenPlaceholder, "{access_token}");
                }
            }

            foreach (var pathParam in methodInfo.InterfacePathParameters)
            {
                var placeholder = $"{{{pathParam.Name}}}";
                if (interpolatedUrl.Contains(placeholder))
                {
                    interpolatedUrl = interpolatedUrl.Replace(placeholder, Uri.EscapeDataString(pathParam.Value ?? ""));
                }
            }

            foreach (var interfacePathProp in methodInfo.InterfaceProperties.Where(p => p.AttributeType == "Path"))
            {
                var placeholder = $"{{{interfacePathProp.ParameterName}}}";
                if (interpolatedUrl.Contains(placeholder))
                {
                    var formatExpression = !string.IsNullOrEmpty(interfacePathProp.Format)
                        ? $".ToString(\"{interfacePathProp.Format}\")"
                        : ".ToString()";

                    if (interfacePathProp.UrlEncode)
                    {
                        interpolatedUrl = interpolatedUrl.Replace(placeholder, $"{{System.Uri.EscapeDataString({interfacePathProp.Name}{formatExpression})}}");
                    }
                    else
                    {
                        interpolatedUrl = interpolatedUrl.Replace(placeholder, $"{{{interfacePathProp.Name}{formatExpression}}}");
                    }
                }
            }
        }

        if (hasPathParams)
        {
            foreach (var param in pathParams)
            {
                var pathAttr = param.Attributes.First(a => HttpClientGeneratorConstants.PathAttributes.Contains(a.Name));
                var placeholderName = GetPathParameterName(pathAttr, param.Name);

                if (interpolatedUrl.Contains($"{{{placeholderName}}}"))
                {
                    var formatString = GetFormatString(pathAttr);
                    var urlEncode = GetUrlEncodeValue(pathAttr);
                    interpolatedUrl = FormatUrlParameter(interpolatedUrl, placeholderName, formatString, urlEncode, param.Name, param.Type);
                }
            }
        }

        return $"            var url = $\"{interpolatedUrl}\";";
    }

    /// <summary>
    /// 获取路径参数的占位符名称
    /// </summary>
    private static string GetPathParameterName(ParameterAttributeInfo pathAttr, string paramName)
    {
        if (pathAttr.NamedArguments.TryGetValue("Name", out var nameValue) && nameValue is string name && !string.IsNullOrEmpty(name))
            return name;
        return paramName;
    }

    /// <summary>
    /// 获取路径参数的 UrlEncode 值
    /// </summary>
    private static bool GetUrlEncodeValue(ParameterAttributeInfo pathAttr)
    {
        if (pathAttr.NamedArguments.TryGetValue("UrlEncode", out var value) && value is bool urlEncode)
            return urlEncode;
        return true;
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

        var queryMapParams = methodInfo.Parameters
            .Where(p => p.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.QueryMapAttribute))
            .ToList();

        var rawQueryStringParams = methodInfo.Parameters
            .Where(p => p.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.RawQueryStringAttribute))
            .ToList();

        var hasTokenQuery = ShouldGenerateTokenQuery(methodInfo);

        var interfaceQueryProperties = methodInfo.InterfaceProperties
            .Where(p => p.AttributeType == "Query")
            .ToList();

        if (!queryParams.Any() && !arrayQueryParams.Any() && !queryMapParams.Any() && !rawQueryStringParams.Any() && !hasTokenQuery && !interfaceQueryProperties.Any())
            return;

        codeBuilder.AppendLine($"            var queryParams = HttpUtility.ParseQueryString(string.Empty);");

        foreach (var interfaceQueryProp in interfaceQueryProperties)
        {
            GenerateInterfaceQueryProperty(codeBuilder, interfaceQueryProp);
        }

        foreach (var param in queryParams)
        {
            GenerateSingleQueryParameter(codeBuilder, param);
        }

        foreach (var param in arrayQueryParams)
        {
            GenerateArrayQueryParameter(codeBuilder, param);
        }

        foreach (var param in queryMapParams)
        {
            GenerateQueryMapParameter(codeBuilder, param);
        }

        if (hasTokenQuery)
        {
            var tokenQueryName = GetTokenQueryName(methodInfo);
            codeBuilder.AppendLine($"            queryParams.Add(\"{tokenQueryName}\", access_token);");
        }

        foreach (var interfaceQuery in methodInfo.InterfaceQueryParameters)
        {
            if (!string.IsNullOrEmpty(interfaceQuery.Name) && interfaceQuery.Value != null)
            {
                codeBuilder.AppendLine($"            queryParams.Add(\"{interfaceQuery.Name}\", \"{interfaceQuery.Value}\");");
            }
        }

        codeBuilder.AppendLine("            if (queryParams.Count > 0)");
        codeBuilder.AppendLine("            {");
        codeBuilder.AppendLine("                url += \"?\" + queryParams.ToString();");
        codeBuilder.AppendLine("            }");

        foreach (var param in rawQueryStringParams)
        {
            GenerateRawQueryStringParameter(codeBuilder, param);
        }
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

        var headerMergeMode = methodInfo.HeaderMergeMode;

        foreach (var param in headerParams)
        {
            var headerAttr = param.Attributes.First(a => a.Name == HttpClientGeneratorConstants.HeaderAttribute);
            var headerName = headerAttr.Arguments.FirstOrDefault()?.ToString() ?? param.Name;
            var formatString = GetFormatString(headerAttr);
            var replace = headerAttr.NamedArguments.TryGetValue("Replace", out var replaceVal) && replaceVal is true;

            var isTokenParam = param.Attributes.Any(attr => HttpClientGeneratorConstants.TokenAttributeNames.Contains(attr.Name));
            if (isTokenParam && !string.IsNullOrEmpty(interfaceHeaderName))
            {
                headerName = interfaceHeaderName;
            }

            var isStringType = TypeDetectionHelper.IsStringType(param.Type);
            var shouldReplace = replace || headerMergeMode == "Replace";
            var shouldIgnore = headerMergeMode == "Ignore";

            if (shouldIgnore)
                continue;

            if (isStringType)
            {
                codeBuilder.AppendLine($"            if (!string.IsNullOrEmpty({param.Name}))");
                if (shouldReplace)
                {
                    codeBuilder.AppendLine($"                httpRequest.Headers.Remove(\"{headerName}\");");
                    codeBuilder.AppendLine($"                httpRequest.Headers.Add(\"{headerName}\", {param.Name});");
                }
                else
                {
                    codeBuilder.AppendLine($"                httpRequest.Headers.Add(\"{headerName}\", {param.Name});");
                }
            }
            else
            {
                var formatExpression = !string.IsNullOrEmpty(formatString)
                    ? $"string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{{0:{formatString}}}\", {param.Name})"
                    : $"{param.Name}.ToString()";
                if (shouldReplace)
                {
                    codeBuilder.AppendLine($"            httpRequest.Headers.Remove(\"{headerName}\");");
                    codeBuilder.AppendLine($"            httpRequest.Headers.Add(\"{headerName}\", {formatExpression});");
                }
                else
                {
                    codeBuilder.AppendLine($"            httpRequest.Headers.Add(\"{headerName}\", {formatExpression});");
                }
            }
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

        var multipartFormParam = methodInfo.Parameters
            .FirstOrDefault(p => p.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.MultipartFormAttribute));

        var uploadParams = methodInfo.Parameters
            .Where(p => p.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.UploadAttribute))
            .ToList();

        var formParams = methodInfo.Parameters
            .Where(p => p.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.FormAttribute))
            .ToList();

        if (formContentParam != null)
        {
            GenerateFormContentParameter(codeBuilder, formContentParam, methodInfo);
            return;
        }

        if (multipartFormParam != null || uploadParams.Any())
        {
            GenerateMultipartFormDataParameter(codeBuilder, methodInfo, multipartFormParam, uploadParams, formParams);
            return;
        }

        if (formParams.Any())
        {
            GenerateUrlEncodedFormParameter(codeBuilder, formParams);
            return;
        }

        if (bodyParam == null)
            return;

        var bodyAttr = bodyParam.Attributes.First(a => a.Name == HttpClientGeneratorConstants.BodyAttribute);
        var useStringContent = GetUseStringContentFlag(bodyAttr);
        var rawString = GetRawStringFlag(bodyAttr);
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

        var isXmlContentType = ContentTypeHelper.IsXmlContentType(effectiveContentType ?? contentType);

        var effectiveSerializationMethod = methodInfo.SerializationMethod;
        if (!hasExplicitContentType && effectiveSerializationMethod == "Xml")
        {
            isXmlContentType = true;
            contentTypeExpression = "\"application/xml\"";
        }
        else if (!hasExplicitContentType && effectiveSerializationMethod == "FormUrlEncoded")
        {
            GenerateUrlEncodedBodyParameter(codeBuilder, bodyParam);
            return;
        }

        if (methodInfo.BodyEnableEncrypt)
        {
            var propertyName = methodInfo.BodyEncryptPropertyName ?? "data";
            var serializeType = methodInfo.BodyEncryptSerializeType ?? "Json";
            string httpClient = hasHttpClient ? "_httpClient" : "_appContext.Value!.HttpClient";

            codeBuilder.AppendLine($"            var encryptedContent = {httpClient}.EncryptContent({bodyParam.Name}, \"{propertyName}\", SerializeType.{serializeType});");
            codeBuilder.AppendLine($"            httpRequest.Content = new StringContent(encryptedContent, Encoding.UTF8, {contentTypeExpression});");
        }
        else if (rawString)
        {
            codeBuilder.AppendLine($"            httpRequest.Content = new StringContent({bodyParam.Name}, Encoding.UTF8, {contentTypeExpression});");
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
        var cancellationTokenParam = methodInfo.Parameters.FirstOrDefault(p => TypeDetectionHelper.IsCancellationToken(p.Type));
        var cancellationTokenArg = cancellationTokenParam?.Name ?? "default";

        codeBuilder.AppendLine($"            var formData = await {formContentParam.Name}.GetFormDataContentAsync({cancellationTokenArg});");
        codeBuilder.AppendLine($"            httpRequest.Content = formData;");
    }

    /// <summary>
    /// 生成 URL 编码的表单参数（用于 [Form] 特性）
    /// </summary>
    private void GenerateUrlEncodedFormParameter(StringBuilder codeBuilder, List<ParameterInfo> formParams)
    {
        codeBuilder.AppendLine("            var __formParameters = new Dictionary<string, string>();");

        foreach (var formParam in formParams)
        {
            var formAttr = formParam.Attributes.First(a => a.Name == HttpClientGeneratorConstants.FormAttribute);
            var fieldName = GetFormFieldName(formAttr, formParam.Name);

            codeBuilder.AppendLine($"            if ({formParam.Name} != null)");
            codeBuilder.AppendLine("            {");
            codeBuilder.AppendLine($"                __formParameters[\"{fieldName}\"] = {formParam.Name}.ToString() ?? \"\";");
            codeBuilder.AppendLine("            }");
        }

        codeBuilder.AppendLine("            httpRequest.Content = new System.Net.Http.FormUrlEncodedContent(__formParameters);");
    }

    /// <summary>
    /// 生成 URL 编码的 Body 参数（用于 [SerializationMethod(FormUrlEncoded)] + [Body]）
    /// </summary>
    private void GenerateUrlEncodedBodyParameter(StringBuilder codeBuilder, ParameterInfo bodyParam)
    {
        codeBuilder.AppendLine($"            if ({bodyParam.Name} != null)");
        codeBuilder.AppendLine("            {");
        codeBuilder.AppendLine($"#if NET6_0_OR_GREATER");
        codeBuilder.AppendLine($"#pragma warning disable IL2072");
        codeBuilder.AppendLine($"#endif");
        codeBuilder.AppendLine($"                var __bodyFormParams = new Dictionary<string, string>();");
        codeBuilder.AppendLine($"                var __bodyProperties = {bodyParam.Name}.GetType().GetProperties();");
        codeBuilder.AppendLine($"                foreach (var __prop in __bodyProperties)");
        codeBuilder.AppendLine("                {");
        codeBuilder.AppendLine($"                    var __val = __prop.GetValue({bodyParam.Name});");
        codeBuilder.AppendLine("                    if (__val != null)");
        codeBuilder.AppendLine("                    {");
        codeBuilder.AppendLine("                        __bodyFormParams[__prop.Name] = __val.ToString() ?? \"\";");
        codeBuilder.AppendLine("                    }");
        codeBuilder.AppendLine("                }");
        codeBuilder.AppendLine("                httpRequest.Content = new System.Net.Http.FormUrlEncodedContent(__bodyFormParams);");
        codeBuilder.AppendLine($"#if NET6_0_OR_GREATER");
        codeBuilder.AppendLine($"#pragma warning restore IL2072");
        codeBuilder.AppendLine($"#endif");
        codeBuilder.AppendLine("            }");
    }

    /// <summary>
    /// 生成 MultipartFormDataContent 参数（用于 [MultipartForm] 和 [Upload] 特性）
    /// </summary>
    private void GenerateMultipartFormDataParameter(StringBuilder codeBuilder, MethodAnalysisResult methodInfo,
        ParameterInfo? multipartFormParam, List<ParameterInfo> uploadParams, List<ParameterInfo> formParams)
    {
        codeBuilder.AppendLine("            var __multipartContent = new System.Net.Http.MultipartFormDataContent();");

        // 处理 [Form] 参数：无论是 [MultipartForm] 还是 [Upload] 存在时，[Form] 参数都应加入 multipart
        if (multipartFormParam != null || uploadParams.Any())
        {
            foreach (var formProp in formParams)
            {
                var formAttr = formProp.Attributes.First(a => a.Name == HttpClientGeneratorConstants.FormAttribute);
                var fieldName = GetFormFieldName(formAttr, formProp.Name);
                codeBuilder.AppendLine($"            if ({formProp.Name} != null)");
                codeBuilder.AppendLine("            {");
                codeBuilder.AppendLine($"                __multipartContent.Add(new System.Net.Http.StringContent({formProp.Name}.ToString() ?? \"\"), \"{fieldName}\");");
                codeBuilder.AppendLine("            }");
            }
        }

        foreach (var uploadParam in uploadParams)
        {
            var uploadAttr = uploadParam.Attributes.First(a => a.Name == HttpClientGeneratorConstants.UploadAttribute);
            var fieldName = GetUploadFieldName(uploadAttr, uploadParam.Name);
            var fileName = GetUploadFileName(uploadAttr);
            var contentType = GetUploadContentType(uploadAttr);

            if (!string.IsNullOrEmpty(contentType))
            {
                codeBuilder.AppendLine($"            if ({uploadParam.Name} != null)");
                codeBuilder.AppendLine("            {");
                codeBuilder.AppendLine($"                var __{uploadParam.Name}Content = new System.Net.Http.StreamContent({uploadParam.Name});");
                codeBuilder.AppendLine($"                __{uploadParam.Name}Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(\"{contentType}\");");
                if (!string.IsNullOrEmpty(fileName))
                    codeBuilder.AppendLine($"                __multipartContent.Add(__{uploadParam.Name}Content, \"{fieldName}\", \"{fileName}\");");
                else
                    codeBuilder.AppendLine($"                __multipartContent.Add(__{uploadParam.Name}Content, \"{fieldName}\");");
                codeBuilder.AppendLine("            }");
            }
            else
            {
                codeBuilder.AppendLine($"            if ({uploadParam.Name} != null)");
                codeBuilder.AppendLine("            {");
                if (!string.IsNullOrEmpty(fileName))
                    codeBuilder.AppendLine($"                __multipartContent.Add(new System.Net.Http.StreamContent({uploadParam.Name}), \"{fieldName}\", \"{fileName}\");");
                else
                    codeBuilder.AppendLine($"                __multipartContent.Add(new System.Net.Http.StreamContent({uploadParam.Name}), \"{fieldName}\");");
                codeBuilder.AppendLine("            }");
            }
        }

        codeBuilder.AppendLine("            httpRequest.Content = __multipartContent;");
    }

    private static string GetUploadFieldName(ParameterAttributeInfo uploadAttr, string paramName)
    {
        if (uploadAttr.NamedArguments.TryGetValue("FieldName", out var fieldName) && fieldName is string fn && !string.IsNullOrEmpty(fn))
            return fn;
        if (uploadAttr.Arguments.Length > 0 && uploadAttr.Arguments[0] is string argFn && !string.IsNullOrEmpty(argFn))
            return argFn;
        return paramName;
    }

    private static string? GetUploadFileName(ParameterAttributeInfo uploadAttr)
    {
        if (uploadAttr.NamedArguments.TryGetValue("FileName", out var fileName) && fileName is string fn && !string.IsNullOrEmpty(fn))
            return fn;
        return null;
    }

    private static string? GetUploadContentType(ParameterAttributeInfo uploadAttr)
    {
        if (uploadAttr.NamedArguments.TryGetValue("ContentType", out var ct) && ct is string s && !string.IsNullOrEmpty(s))
            return s;
        return null;
    }

    private static string GetFormFieldName(ParameterAttributeInfo formAttr, string paramName)
    {
        if (formAttr.NamedArguments.TryGetValue("FieldName", out var fieldName) && fieldName is string fn && !string.IsNullOrEmpty(fn))
            return fn;
        if (formAttr.Arguments.Length > 0 && formAttr.Arguments[0] is string argFn && !string.IsNullOrEmpty(argFn))
            return argFn;
        return paramName;
    }

    /// <summary>
    /// 生成请求执行
    /// </summary>
    public void GenerateRequestExecution(StringBuilder codeBuilder, MethodAnalysisResult methodInfo, string cancellationTokenArg, bool hasHttpClient)
    {
        var filePathParam = methodInfo.Parameters.Where(p => p.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.FilePathAttribute)).FirstOrDefault();

        var deserializeType = methodInfo.IsAsyncMethod ? methodInfo.AsyncInnerReturnType : methodInfo.ReturnType;
        codeBuilder.AppendLine();
        string httpClient = "_appContext.Value!.HttpClient";
        if (hasHttpClient)
        {
            httpClient = "_httpClient";
        }

        if (methodInfo.IsAsyncEnumerableReturn && !string.IsNullOrEmpty(methodInfo.AsyncEnumerableElementType))
        {
            var elementType = methodInfo.AsyncEnumerableElementType;
            var cancellationTokenParam = methodInfo.Parameters.FirstOrDefault(p => TypeDetectionHelper.IsCancellationToken(p.Type));
            var cancellationTokenName = cancellationTokenParam?.Name ?? "default";
            codeBuilder.AppendLine($"            await foreach (var __item in {httpClient}.SendAsAsyncEnumerable<{elementType}>(httpRequest, _jsonSerializerOptions, {cancellationTokenName}))");
            codeBuilder.AppendLine("            {");
            codeBuilder.AppendLine("                yield return __item;");
            codeBuilder.AppendLine("            }");
            return;
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
                var isResponseType = IsResponseType(deserializeType, out var responseInnerType);

                if (isResponseType)
                {
                    GenerateResponseExecution(codeBuilder, methodInfo, httpClient, cancellationTokenArg, responseInnerType);
                }
                else if (methodInfo.AllowAnyStatusCode)
                {
                    GenerateAllowAnyStatusCodeExecution(codeBuilder, methodInfo, httpClient, cancellationTokenArg, deserializeType);
                }
                else
                {
                    GenerateStandardExecution(codeBuilder, methodInfo, httpClient, cancellationTokenArg, deserializeType);
                }
            }
        }
    }

    /// <summary>
    /// 检测返回类型是否为 Response&lt;T&gt;，并提取内部类型 T
    /// </summary>
    private static bool IsResponseType(string type, out string innerType)
    {
        innerType = string.Empty;

        if (type.StartsWith("Response<", StringComparison.Ordinal) ||
            type.StartsWith("Mud.HttpUtils.Response<", StringComparison.Ordinal) ||
            type.StartsWith("Mud.HttpUtils.HttpClient.Response<", StringComparison.Ordinal))
        {
            var startIdx = type.IndexOf('<');
            var endIdx = type.LastIndexOf('>');
            if (startIdx >= 0 && endIdx > startIdx)
            {
                innerType = type.Substring(startIdx + 1, endIdx - startIdx - 1);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 生成 Response&lt;T&gt; 返回类型的执行代码
    /// </summary>
    private void GenerateResponseExecution(StringBuilder codeBuilder, MethodAnalysisResult methodInfo, string httpClient, string cancellationTokenArg, string innerType)
    {
        codeBuilder.AppendLine($"            using var __httpResponse = await {httpClient}.SendRawAsync(httpRequest{cancellationTokenArg});");
        codeBuilder.AppendLine($"            var __statusCode = __httpResponse.StatusCode;");
        codeBuilder.AppendLine("#if NET6_0_OR_GREATER");
        codeBuilder.AppendLine($"            var __rawContent = await __httpResponse.Content.ReadAsStringAsync({GetCancellationTokenArgument(cancellationTokenArg)}).ConfigureAwait(false);");
        codeBuilder.AppendLine("#else");
        codeBuilder.AppendLine($"            var __rawContent = await __httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);");
        codeBuilder.AppendLine("#endif");
        codeBuilder.AppendLine($"            var __responseHeaders = __httpResponse.Headers.ToDictionary(h => h.Key, h => h.Value.ToList());");

        codeBuilder.AppendLine($"            if ((int)__statusCode >= 200 && (int)__statusCode <= 299)");
        codeBuilder.AppendLine($"            {{");

        var isVoidType = innerType == "void" || innerType == "System.Void";
        if (!isVoidType)
        {
            codeBuilder.AppendLine($"                var __content = System.Text.Json.JsonSerializer.Deserialize<{innerType}>(__rawContent, _jsonSerializerOptions);");
            codeBuilder.AppendLine($"                return new Mud.HttpUtils.Response<{innerType}>(__statusCode, __content, __rawContent, __responseHeaders);");
        }
        else
        {
            codeBuilder.AppendLine($"                return new Mud.HttpUtils.Response<{innerType}>(__statusCode, default, __rawContent, __responseHeaders);");
        }

        codeBuilder.AppendLine($"            }}");
        codeBuilder.AppendLine($"            else");
        codeBuilder.AppendLine($"            {{");
        codeBuilder.AppendLine($"                return new Mud.HttpUtils.Response<{innerType}>(__statusCode, __rawContent, __responseHeaders);");
        codeBuilder.AppendLine($"            }}");
    }

    /// <summary>
    /// 生成 AllowAnyStatusCode 模式的执行代码（非 Response&lt;T&gt; 返回类型）
    /// </summary>
    private void GenerateAllowAnyStatusCodeExecution(StringBuilder codeBuilder, MethodAnalysisResult methodInfo, string httpClient, string cancellationTokenArg, string deserializeType)
    {
        var responseContentType = methodInfo.ResponseContentType;
        var isXmlResponse = ContentTypeHelper.IsXmlContentType(responseContentType);

        if (isXmlResponse)
        {
            codeBuilder.AppendLine($"            var __result = await {httpClient}.SendXmlAsync<{deserializeType}>(httpRequest, null{cancellationTokenArg});");
        }
        else
        {
            codeBuilder.AppendLine($"            var __result = await {httpClient}.SendAsync<{deserializeType}>(httpRequest, _jsonSerializerOptions{cancellationTokenArg});");
        }

        if (methodInfo.ResponseEnableDecrypt)
        {
            string encryptableClient = httpClient;
            codeBuilder.AppendLine($"            if (__result != null)");
            codeBuilder.AppendLine($"            {{");
            codeBuilder.AppendLine($"                var __rawJson = JsonSerializer.Serialize(__result, _jsonSerializerOptions);");
            codeBuilder.AppendLine($"                var decryptedJson = {encryptableClient}.DecryptContent(__rawJson);");
            codeBuilder.AppendLine($"                __result = JsonSerializer.Deserialize<{deserializeType}>(decryptedJson, _jsonSerializerOptions);");
            codeBuilder.AppendLine($"            }}");
        }

        codeBuilder.AppendLine($"            return __result;");
    }

    private string GetCancellationTokenArgument(string cancellationTokenArg)
    {
        if (string.IsNullOrEmpty(cancellationTokenArg))
            return string.Empty;
        return cancellationTokenArg.Remove(0, 1);
    }

    /// <summary>
    /// 生成标准模式的执行代码（默认行为，错误状态码抛出 ApiException）
    /// </summary>
    private void GenerateStandardExecution(StringBuilder codeBuilder, MethodAnalysisResult methodInfo, string httpClient, string cancellationTokenArg, string deserializeType)
    {
        var responseContentType = methodInfo.ResponseContentType;
        var isXmlResponse = ContentTypeHelper.IsXmlContentType(responseContentType);
        var resultVariable = $"__result_{methodInfo.MethodName}";

        codeBuilder.AppendLine($"            using var __httpResponse = await {httpClient}.SendRawAsync(httpRequest{cancellationTokenArg});");

        codeBuilder.AppendLine($"            if (!__httpResponse.IsSuccessStatusCode)");
        codeBuilder.AppendLine($"            {{");
        codeBuilder.AppendLine("#if NET6_0_OR_GREATER");
        codeBuilder.AppendLine($"               var __errorContent = await __httpResponse.Content.ReadAsStringAsync({GetCancellationTokenArgument(cancellationTokenArg)}).ConfigureAwait(false);");
        codeBuilder.AppendLine("#else");
        codeBuilder.AppendLine($"               var __errorContent = await __httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);");
        codeBuilder.AppendLine("#endif");
        codeBuilder.AppendLine($"                throw new Mud.HttpUtils.ApiException(__httpResponse.StatusCode, __errorContent, httpRequest.RequestUri?.ToString());");
        codeBuilder.AppendLine($"            }}");

        var rawContentVar = $"__rawContent_{methodInfo.MethodName}";

        codeBuilder.AppendLine("#if NET6_0_OR_GREATER");
        codeBuilder.AppendLine($"            var {rawContentVar} = await __httpResponse.Content.ReadAsStringAsync({GetCancellationTokenArgument(cancellationTokenArg)}).ConfigureAwait(false);");
        codeBuilder.AppendLine("#else");
        codeBuilder.AppendLine($"            var {rawContentVar} = await __httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);");
        codeBuilder.AppendLine("#endif");


        if (isXmlResponse)
        {
            codeBuilder.AppendLine($"            var {resultVariable} = ({deserializeType}?)new System.Xml.Serialization.XmlSerializer(typeof({deserializeType})).Deserialize(new System.IO.StringReader({rawContentVar}));");
        }
        else
        {
            codeBuilder.AppendLine($"            var {resultVariable} = System.Text.Json.JsonSerializer.Deserialize<{deserializeType}>({rawContentVar}, _jsonSerializerOptions);");
        }

        if (methodInfo.ResponseEnableDecrypt)
        {
            string encryptableClient = httpClient;
            codeBuilder.AppendLine($"            if ({resultVariable} != null)");
            codeBuilder.AppendLine($"            {{");
            codeBuilder.AppendLine($"                var __rawJson = JsonSerializer.Serialize({resultVariable}, _jsonSerializerOptions);");
            codeBuilder.AppendLine($"                var decryptedJson = {encryptableClient}.DecryptContent(__rawJson);");
            codeBuilder.AppendLine($"                {resultVariable} = JsonSerializer.Deserialize<{deserializeType}>(decryptedJson, _jsonSerializerOptions);");
            codeBuilder.AppendLine($"            }}");
        }

        codeBuilder.AppendLine($"            return {resultVariable};");
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

    private string FormatUrlParameter(string url, string placeholderName, string? formatString, bool urlEncode, string paramName, string paramType)
    {
        var isStringType = TypeDetectionHelper.IsStringType(paramType);

        if (string.IsNullOrEmpty(formatString))
        {
            if (urlEncode)
            {
                if (isStringType)
                {
                    return url.Replace($"{{{placeholderName}}}", $"{{Uri.EscapeDataString({paramName})}}");
                }
                else
                {
                    return url.Replace($"{{{placeholderName}}}", $"{{Uri.EscapeDataString(({paramName}).ToString() ?? string.Empty)}}");
                }
            }
            return url.Replace($"{{{placeholderName}}}", $"{{{paramName}}}");
        }

        if (formatString.Contains("{0}"))
        {
            var formatExpr = $"string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{formatString}\", {paramName})";
            if (urlEncode)
            {
                return url.Replace($"{{{placeholderName}}}", $"{{Uri.EscapeDataString({formatExpr})}}");
            }
            return url.Replace($"{{{placeholderName}}}", $"{{{formatExpr}}}");
        }

        var standardFormatExpr = $"string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{{0:{formatString}}}\", {paramName})";
        if (urlEncode)
        {
            return url.Replace($"{{{placeholderName}}}", $"{{Uri.EscapeDataString({standardFormatExpr})}}");
        }
        return url.Replace($"{{{placeholderName}}}", $"{{{standardFormatExpr}}}");
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
        codeBuilder.AppendLine($"                if ({param.Name} is IQueryParameter queryParam)");
        codeBuilder.AppendLine("                {");
        codeBuilder.AppendLine($"                    foreach (var kvp in queryParam.ToQueryParameters())");
        codeBuilder.AppendLine("                    {");
        codeBuilder.AppendLine("                        if (!string.IsNullOrEmpty(kvp.Value))");
        codeBuilder.AppendLine("                        {");
        codeBuilder.AppendLine("                            var encodedValue = HttpUtility.UrlEncode(kvp.Value);");
        codeBuilder.AppendLine("                            queryParams.Add(kvp.Key, encodedValue);");
        codeBuilder.AppendLine("                        }");
        codeBuilder.AppendLine("                    }");
        codeBuilder.AppendLine("                }");
        codeBuilder.AppendLine("                else");
        codeBuilder.AppendLine("                {");
        codeBuilder.AppendLine($"#if NET6_0_OR_GREATER");
        codeBuilder.AppendLine($"#pragma warning disable IL2072 // AOT 警告：参数类型 {param.Type} 未实现 IQueryParameter 接口");
        codeBuilder.AppendLine($"#endif");
        codeBuilder.AppendLine($"                    var properties = {param.Name}.GetType().GetProperties();");
        codeBuilder.AppendLine("                    foreach (var prop in properties)");
        codeBuilder.AppendLine("                    {");
        codeBuilder.AppendLine($"                        var value = prop.GetValue({param.Name});");
        codeBuilder.AppendLine("                        if (value != null)");
        codeBuilder.AppendLine("                        {");
        codeBuilder.AppendLine($"                            queryParams.Add(prop.Name, HttpUtility.UrlEncode(value.ToString()));");
        codeBuilder.AppendLine("                        }");
        codeBuilder.AppendLine("                    }");
        codeBuilder.AppendLine($"#if NET6_0_OR_GREATER");
        codeBuilder.AppendLine($"#pragma warning restore IL2072");
        codeBuilder.AppendLine($"#endif");
        codeBuilder.AppendLine("                }");
        codeBuilder.AppendLine("            }");
    }

    /// <summary>
    /// 生成 QueryMap 参数代码
    /// </summary>
    private void GenerateQueryMapParameter(StringBuilder codeBuilder, ParameterInfo param)
    {
        var queryMapAttr = param.Attributes.First(a => a.Name == HttpClientGeneratorConstants.QueryMapAttribute);
        var separator = "_";
        if (queryMapAttr.NamedArguments.TryGetValue("PropertySeparator", out var sepValue) && sepValue is string sep && !string.IsNullOrEmpty(sep))
            separator = sep;

        var urlEncode = true;
        if (queryMapAttr.NamedArguments.TryGetValue("UrlEncode", out var encValue) && encValue is bool enc)
            urlEncode = enc;

        var includeNullValues = false;
        if (queryMapAttr.NamedArguments.TryGetValue("IncludeNullValues", out var incNull) && incNull is bool inc)
            includeNullValues = inc;

        var serializationMethod = "ToString";
        if (queryMapAttr.NamedArguments.TryGetValue("SerializationMethod", out var serValue))
        {
            var serInt = serValue as int? ?? 0;
            if (serInt == 1)
                serializationMethod = "Json";
        }

        var isDictionaryType = TypeDetectionHelper.IsDictionaryType(param.Type);

        codeBuilder.AppendLine($"            if ({param.Name} != null)");
        codeBuilder.AppendLine("            {");

        if (isDictionaryType)
        {
            GenerateDictionaryQueryMap(codeBuilder, param, urlEncode, includeNullValues, serializationMethod);
        }
        else
        {
            codeBuilder.AppendLine($"                if ({param.Name} is IQueryParameter queryParam_{param.Name})");
            codeBuilder.AppendLine("                {");
            codeBuilder.AppendLine($"                    foreach (var kvp in queryParam_{param.Name}.ToQueryParameters())");
            codeBuilder.AppendLine("                    {");
            if (includeNullValues)
            {
                GenerateQueryMapValueAddition(codeBuilder, param, urlEncode, serializationMethod, "kvp.Key", "kvp.Value");
            }
            else
            {
                codeBuilder.AppendLine("                        if (!string.IsNullOrEmpty(kvp.Value))");
                codeBuilder.AppendLine("                        {");
                GenerateQueryMapValueAddition(codeBuilder, param, urlEncode, serializationMethod, "kvp.Key", "kvp.Value", "                            ");
                codeBuilder.AppendLine("                        }");
            }
            codeBuilder.AppendLine("                    }");
            codeBuilder.AppendLine("                }");
            codeBuilder.AppendLine($"                else");
            codeBuilder.AppendLine("                {");
            var useJson = serializationMethod == "Json" ? "true" : "false";
            codeBuilder.AppendLine($"                    FlattenObjectToQueryParams({param.Name}, \"\", \"{separator}\", queryParams, {urlEncode.ToString().ToLowerInvariant()}, {includeNullValues.ToString().ToLowerInvariant()}, {useJson});");
            codeBuilder.AppendLine("                }");
        }

        codeBuilder.AppendLine("            }");
    }

    private void GenerateQueryMapValueAddition(
        StringBuilder codeBuilder,
        ParameterInfo param,
        bool urlEncode,
        string serializationMethod,
        string keyExpression,
        string valueExpression,
        string indent = "                        ",
        string? separator = null)
    {
        if (serializationMethod == "Json")
        {
            if (urlEncode)
            {
                codeBuilder.AppendLine($"{indent}var jsonValue_{param.Name} = System.Text.Json.JsonSerializer.Serialize({valueExpression});");
                codeBuilder.AppendLine($"{indent}queryParams.Add({keyExpression}, HttpUtility.UrlEncode(jsonValue_{param.Name}));");
            }
            else
            {
                codeBuilder.AppendLine($"{indent}queryParams.Add({keyExpression}, System.Text.Json.JsonSerializer.Serialize({valueExpression}));");
            }
        }
        else
        {
            if (urlEncode)
            {
                codeBuilder.AppendLine($"{indent}var encodedValue_{param.Name} = {valueExpression} != null ? HttpUtility.UrlEncode({valueExpression}.ToString()) : null;");
                codeBuilder.AppendLine($"{indent}queryParams.Add({keyExpression}, encodedValue_{param.Name});");
            }
            else
            {
                codeBuilder.AppendLine($"{indent}queryParams.Add({keyExpression}, {valueExpression}?.ToString());");
            }
        }
    }

    private void GenerateDictionaryQueryMap(StringBuilder codeBuilder, ParameterInfo param, bool urlEncode, bool includeNullValues, string serializationMethod = "ToString")
    {
        codeBuilder.AppendLine($"                foreach (var kvp_{param.Name} in {param.Name})");
        codeBuilder.AppendLine("                {");
        if (includeNullValues)
        {
            GenerateQueryMapValueAddition(codeBuilder, param, urlEncode, serializationMethod, $"kvp_{param.Name}.Key", $"kvp_{param.Name}.Value", "                    ");
        }
        else
        {
            codeBuilder.AppendLine($"                    if (kvp_{param.Name}.Value != null)");
            codeBuilder.AppendLine("                    {");
            GenerateQueryMapValueAddition(codeBuilder, param, urlEncode, serializationMethod, $"kvp_{param.Name}.Key", $"kvp_{param.Name}.Value", "                        ");
            codeBuilder.AppendLine("                    }");
        }
        codeBuilder.AppendLine("                }");
    }

    private void GenerateInterfaceQueryProperty(StringBuilder codeBuilder, InterfacePropertyInfo property)
    {
        var paramName = property.ParameterName ?? property.Name;
        var formatExpression = !string.IsNullOrEmpty(property.Format)
            ? $".ToString(\"{property.Format}\")"
            : ".ToString()";

        codeBuilder.AppendLine($"            if ({property.Name} != null)");
        codeBuilder.AppendLine("            {");

        if (property.Type == "string" || property.Type == "System.String")
        {
            codeBuilder.AppendLine($"                if (!string.IsNullOrEmpty({property.Name}))");
            codeBuilder.AppendLine("                {");
            codeBuilder.AppendLine($"                    queryParams.Add(\"{paramName}\", {property.Name});");
            codeBuilder.AppendLine("                }");
        }
        else
        {
            codeBuilder.AppendLine($"                queryParams.Add(\"{paramName}\", {property.Name}{formatExpression});");
        }

        codeBuilder.AppendLine("            }");
    }

    /// <summary>
    /// 生成 RawQueryString 参数代码
    /// </summary>
    private void GenerateRawQueryStringParameter(StringBuilder codeBuilder, ParameterInfo param)
    {
        codeBuilder.AppendLine($"            if (!string.IsNullOrEmpty({param.Name}))");
        codeBuilder.AppendLine("            {");
        codeBuilder.AppendLine($"                var __rawQS = {param.Name}.TrimStart('?', '&').TrimEnd('&');");
        codeBuilder.AppendLine("                if (!string.IsNullOrEmpty(__rawQS))");
        codeBuilder.AppendLine("                {");
        codeBuilder.AppendLine("                    var separator = url.Contains('?') ? \"&\" : \"?\";");
        codeBuilder.AppendLine("                    url += separator + __rawQS;");
        codeBuilder.AppendLine("                }");
        codeBuilder.AppendLine("            }");
    }

    private static readonly HashSet<string> FormatStringFirstArgAttributes = new HashSet<string>(StringComparer.Ordinal)
    {
        HttpClientGeneratorConstants.QueryAttribute,
        HttpClientGeneratorConstants.ArrayQueryAttribute,
    };

    private static readonly HashSet<string> FirstArgIsNameAttributes = new HashSet<string>(StringComparer.Ordinal)
    {
        HttpClientGeneratorConstants.HeaderAttribute,
    };

    private string GetFormatString(ParameterAttributeInfo attribute)
    {
        if (FirstArgIsNameAttributes.Contains(attribute.Name))
        {
            return attribute.NamedArguments.TryGetValue("FormatString", out var formatString)
                ? formatString as string
                : attribute.NamedArguments.TryGetValue("Format", out var formatAlias)
                    ? formatAlias as string
                    : null;
        }

        if (attribute.Arguments.Length > 1)
        {
            return attribute.Arguments[1] as string ?? "";
        }

        if (attribute.Arguments.Length == 1
            && !HttpClientGeneratorConstants.PathAttributes.Contains(attribute.Name)
            && !FormatStringFirstArgAttributes.Contains(attribute.Name))
        {
            return attribute.Arguments[0] as string ?? "";
        }

        if (attribute.NamedArguments.TryGetValue("FormatString", out var fs))
            return fs as string;

        if (attribute.NamedArguments.TryGetValue("Format", out var f))
            return f as string;

        return null;
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
            ? (contentTypeArg?.ToString() ?? HttpClientGeneratorConstants.DefaultContentType)
            : HttpClientGeneratorConstants.DefaultContentType;
    }

    private bool GetUseStringContentFlag(ParameterAttributeInfo bodyAttr)
    {
        if (!bodyAttr.NamedArguments.TryGetValue("UseStringContent", out var useStringContentArg))
            return false;

        return bool.TryParse(useStringContentArg?.ToString(), out var result) && result;
    }

    private bool GetRawStringFlag(ParameterAttributeInfo bodyAttr)
    {
        if (!bodyAttr.NamedArguments.TryGetValue("RawString", out var rawStringArg))
            return false;

        return bool.TryParse(rawStringArg?.ToString(), out var result) && result;
    }

    #endregion
}
