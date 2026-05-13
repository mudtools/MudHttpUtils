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
            var normalizedUrlTemplate = urlTemplate.TrimStart('/');
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
                    var escapedValue = Uri.EscapeDataString(pathParam.Value ?? "");
                    interpolatedUrl = interpolatedUrl.Replace(placeholder, escapedValue);
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

        return $"            var __url = $\"{interpolatedUrl}\";";
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
        var queryBinder = new QueryParameterBinder();
        var queryParams = methodInfo.Parameters
            .Where(p => queryBinder.CanBind(p))
            .ToList();

        var hasTokenQuery = ShouldGenerateTokenQuery(methodInfo);

        var interfaceQueryProperties = methodInfo.InterfaceProperties
            .Where(p => p.AttributeType == "Query")
            .ToList();

        var hasInterfaceQueryParams = methodInfo.InterfaceQueryParameters.Any();

        if (!queryParams.Any() && !hasTokenQuery && !interfaceQueryProperties.Any() && !hasInterfaceQueryParams)
            return;

        codeBuilder.AppendLine($"            var __queryParams = HttpUtility.ParseQueryString(string.Empty);");
        codeBuilder.AppendLine("            var __rawQueryPairs = new System.Collections.Generic.List<string>();");

        foreach (var interfaceQueryProp in interfaceQueryProperties)
        {
            GenerateInterfaceQueryProperty(codeBuilder, interfaceQueryProp);
        }

        foreach (var param in queryParams)
        {
            queryBinder.GenerateBindingCode(codeBuilder, param, methodInfo, "            ");
        }

        if (hasTokenQuery)
        {
            var tokenQueryName = GetTokenQueryName(methodInfo);
            codeBuilder.AppendLine($"            __queryParams.Add(\"{tokenQueryName}\", access_token);");
        }

        foreach (var interfaceQuery in methodInfo.InterfaceQueryParameters)
        {
            if (!string.IsNullOrEmpty(interfaceQuery.Name) && interfaceQuery.Value != null)
            {
                codeBuilder.AppendLine($"            __queryParams.Add(\"{interfaceQuery.Name}\", \"{interfaceQuery.Value}\");");
            }
        }

        codeBuilder.AppendLine("            if (__queryParams.Count > 0)");
        codeBuilder.AppendLine("            {");
        codeBuilder.AppendLine("                __url += \"?\" + __queryParams.ToString();");
        codeBuilder.AppendLine("            }");
        codeBuilder.AppendLine("            if (__rawQueryPairs.Count > 0)");
        codeBuilder.AppendLine("            {");
        codeBuilder.AppendLine("                __url += (__url.Contains(\"?\") ? \"&\" : \"?\") + string.Join(\"&\", __rawQueryPairs);");
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
            codeBuilder.AppendLine("            var __httpMethod = new HttpMethod(\"PATCH\");");
            codeBuilder.AppendLine($"            using var __httpRequest = new HttpRequestMessage(__httpMethod, __url);");
            codeBuilder.AppendLine("#else");
            codeBuilder.AppendLine($"            using var __httpRequest = new HttpRequestMessage(HttpMethod.{methodInfo.HttpMethod}, __url);");
            codeBuilder.AppendLine("#endif");
        }
        else
        {
            codeBuilder.AppendLine($"            using var __httpRequest = new HttpRequestMessage(HttpMethod.{methodInfo.HttpMethod}, __url);");
        }
    }

    /// <summary>
    /// 生成 Header 参数
    /// </summary>
    public void GenerateHeaderParameters(StringBuilder codeBuilder, MethodAnalysisResult methodInfo)
    {
        var headerBinder = new HeaderParameterBinder();
        var headerParams = methodInfo.Parameters
            .Where(p => headerBinder.CanBind(p))
            .ToList();

        foreach (var param in headerParams)
        {
            headerBinder.GenerateBindingCode(codeBuilder, param, methodInfo, "            ");
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
                : "_defaultContentType";
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
            string httpClient = hasHttpClient ? "_httpClient" : "_appContextHolder.Current!.HttpClient";

            codeBuilder.AppendLine($"            var __encryptedContent = {httpClient}.EncryptContent({bodyParam.Name}, \"{propertyName}\", SerializeType.{serializeType});");
            codeBuilder.AppendLine($"            using var __encryptedStrContent = new StringContent(__encryptedContent, Encoding.UTF8, {contentTypeExpression});");
            codeBuilder.AppendLine($"            __httpRequest.Content = __encryptedStrContent;");
        }
        else if (rawString)
        {
            codeBuilder.AppendLine($"            using var __rawStrContent = new StringContent({bodyParam.Name}, Encoding.UTF8, {contentTypeExpression});");
            codeBuilder.AppendLine($"            __httpRequest.Content = __rawStrContent;");
        }
        else if (useStringContent)
        {
            codeBuilder.AppendLine($"            using var __useStrContent = new StringContent({bodyParam.Name}.ToString() ?? \"\", Encoding.UTF8, {contentTypeExpression});");
            codeBuilder.AppendLine($"            __httpRequest.Content = __useStrContent;");
        }
        else if (isXmlContentType)
        {
            codeBuilder.AppendLine($"            var __xmlContent = XmlSerialize.Serialize({bodyParam.Name});");
            codeBuilder.AppendLine($"            using var __xmlStrContent = new StringContent(__xmlContent, Encoding.UTF8, {contentTypeExpression});");
            codeBuilder.AppendLine($"            __httpRequest.Content = __xmlStrContent;");
        }
        else
        {
            codeBuilder.AppendLine($"            var __jsonContent = JsonSerializer.Serialize({bodyParam.Name}, _jsonSerializerOptions);");
            codeBuilder.AppendLine($"            using var __jsonStrContent = new StringContent(__jsonContent, Encoding.UTF8, {contentTypeExpression});");
            codeBuilder.AppendLine($"            __httpRequest.Content = __jsonStrContent;");
        }
    }

    /// <summary>
    /// 生成 FormContent 参数（用于 multipart/form-data）
    /// </summary>
    private void GenerateFormContentParameter(StringBuilder codeBuilder, ParameterInfo formContentParam, MethodAnalysisResult methodInfo)
    {
        var cancellationTokenParam = methodInfo.Parameters.FirstOrDefault(p => TypeDetectionHelper.IsCancellationToken(p.Type));
        var cancellationTokenArg = cancellationTokenParam?.Name ?? "default";

        codeBuilder.AppendLine($"            using var __formData = await {formContentParam.Name}.GetFormDataContentAsync({cancellationTokenArg}).ConfigureAwait(false);");
        codeBuilder.AppendLine($"            __httpRequest.Content = __formData;");
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

        codeBuilder.AppendLine("            using var __formContent = new System.Net.Http.FormUrlEncodedContent(__formParameters);");
        codeBuilder.AppendLine("            __httpRequest.Content = __formContent;");
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
        codeBuilder.AppendLine("                using var __bodyFormContent = new System.Net.Http.FormUrlEncodedContent(__bodyFormParams);");
        codeBuilder.AppendLine("                __httpRequest.Content = __bodyFormContent;");
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
        codeBuilder.AppendLine("            using var __multipartContent = new System.Net.Http.MultipartFormDataContent();");

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
                codeBuilder.AppendLine($"                using var __{uploadParam.Name}Content = new System.Net.Http.StreamContent({uploadParam.Name});");
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

        codeBuilder.AppendLine("            __httpRequest.Content = __multipartContent;");
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
        string httpClient = "_appContextHolder.Current!.HttpClient";
        if (hasHttpClient)
        {
            httpClient = "_httpClient";
        }

        if (methodInfo.IsAsyncEnumerableReturn && !string.IsNullOrEmpty(methodInfo.AsyncEnumerableElementType))
        {
            var elementType = methodInfo.AsyncEnumerableElementType;
            var cancellationTokenParam = methodInfo.Parameters.FirstOrDefault(p => TypeDetectionHelper.IsCancellationToken(p.Type));
            var cancellationTokenName = cancellationTokenParam?.Name ?? "default";
            codeBuilder.AppendLine($"            await foreach (var __item in {httpClient}.SendAsAsyncEnumerable<{elementType}>(__httpRequest, _jsonSerializerOptions, {cancellationTokenName}))");
            codeBuilder.AppendLine("            {");
            codeBuilder.AppendLine("                yield return __item;");
            codeBuilder.AppendLine("            }");
            return;
        }

        if (filePathParam != null)
        {
            codeBuilder.AppendLine($"            await {httpClient}.DownloadLargeAsync(__httpRequest, {filePathParam.Name}{cancellationTokenArg}).ConfigureAwait(false);");
        }
        else
        {
            if (TypeDetectionHelper.IsByteArrayType(deserializeType))
            {
                codeBuilder.AppendLine($"            return await {httpClient}.DownloadAsync(__httpRequest{cancellationTokenArg}).ConfigureAwait(false);");
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
        var responseContentType = methodInfo.ResponseContentType;
        var isXmlResponse = ContentTypeHelper.IsXmlContentType(responseContentType);

        codeBuilder.AppendLine($"            using var __httpResponse = await {httpClient}.SendRawAsync(__httpRequest{cancellationTokenArg}).ConfigureAwait(false);");
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
            var innerTypeWithoutNullable = GetVariableTypeString(innerType);
            var innerTypeWithoutNullableClean = innerType.EndsWith("?", StringComparison.OrdinalIgnoreCase) ? innerType.TrimEnd('?') : innerType;

            if (TypeDetectionHelper.IsStringType(innerTypeWithoutNullableClean))
            {
                if (methodInfo.ResponseEnableDecrypt)
                {
                    codeBuilder.AppendLine($"                var __decryptedContent = {httpClient}.DecryptContent(__rawContent);");
                    codeBuilder.AppendLine($"                return new Mud.HttpUtils.Response<{innerType}>(__statusCode, __decryptedContent, __rawContent, __responseHeaders);");
                }
                else
                {
                    codeBuilder.AppendLine($"                return new Mud.HttpUtils.Response<{innerType}>(__statusCode, __rawContent, __rawContent, __responseHeaders);");
                }
            }
            else
            {
                codeBuilder.AppendLine($"                {innerTypeWithoutNullable} __content;");
                codeBuilder.AppendLine($"                try");
                codeBuilder.AppendLine($"                {{");

                if (isXmlResponse)
                {
                    codeBuilder.AppendLine($"                    var __deserializedObj = {GetXmlSerializerFieldReference(innerType)}.Deserialize(new System.IO.StringReader(__rawContent));");
                    codeBuilder.AppendLine($"                    __content = __deserializedObj is {innerType} __typed ? __typed : default;");
                }
                else
                {
                    codeBuilder.AppendLine($"                    __content = System.Text.Json.JsonSerializer.Deserialize<{innerType}>(__rawContent, _jsonSerializerOptions);");
                }

                codeBuilder.AppendLine($"                }}");

                if (isXmlResponse)
                {
                    codeBuilder.AppendLine($"                catch (System.Exception ex) when (ex is System.InvalidOperationException or System.Xml.XmlException)");
                    codeBuilder.AppendLine($"                {{");
                    codeBuilder.AppendLine($"                    return new Mud.HttpUtils.Response<{innerType}>(__statusCode, \"Failed to deserialize XML response: \" + ex.Message + \". Raw content: \" + __rawContent, __responseHeaders);");
                }
                else
                {
                    codeBuilder.AppendLine($"                catch (System.Text.Json.JsonException ex)");
                    codeBuilder.AppendLine($"                {{");
                    codeBuilder.AppendLine($"                    return new Mud.HttpUtils.Response<{innerType}>(__statusCode, \"Failed to deserialize JSON response: \" + ex.Message + \". Raw content: \" + __rawContent, __responseHeaders);");
                }

                codeBuilder.AppendLine($"                }}");

                if (methodInfo.ResponseEnableDecrypt)
                {
                    codeBuilder.AppendLine($"                if (__content != null)");
                    codeBuilder.AppendLine($"                {{");
                    codeBuilder.AppendLine($"                    var __rawJson = JsonSerializer.Serialize(__content, _jsonSerializerOptions);");
                    codeBuilder.AppendLine($"                    var __decryptedJson = {httpClient}.DecryptContent(__rawJson);");
                    codeBuilder.AppendLine($"                    __content = JsonSerializer.Deserialize<{innerType}>(__decryptedJson, _jsonSerializerOptions);");
                    codeBuilder.AppendLine($"                }}");
                }

                codeBuilder.AppendLine($"                return new Mud.HttpUtils.Response<{innerType}>(__statusCode, __content, __rawContent, __responseHeaders);");
            }
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

    private string GetVariableTypeString(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return type ?? string.Empty;

        // 去掉已有的 ? 后缀
        var baseType = type.TrimEnd('?');

        // 只有值类型才添加 ? 后缀
        // JsonSerializer.Deserialize<T>() 返回 T?，对于值类型（如 bool、int），
        // 变量必须声明为 T? 才能接收返回值；引用类型则始终添加 ? 以匹配 nullable 上下文
        return baseType + "?";
    }

    /// <summary>
    /// 生成 AllowAnyStatusCode 模式的执行代码（非 Response&lt;T&gt; 返回类型）
    /// </summary>
    private void GenerateAllowAnyStatusCodeExecution(StringBuilder codeBuilder, MethodAnalysisResult methodInfo, string httpClient, string cancellationTokenArg, string deserializeType)
    {
        var isVoidType = deserializeType == "void" || deserializeType == "System.Void";

        codeBuilder.AppendLine($"            using var __httpResponse = await {httpClient}.SendRawAsync(__httpRequest{cancellationTokenArg}).ConfigureAwait(false);");

        if (isVoidType)
            return;

        var responseContentType = methodInfo.ResponseContentType;
        var isXmlResponse = ContentTypeHelper.IsXmlContentType(responseContentType);
        var resultVariable = $"__result_{methodInfo.MethodName}";

        var rawContentVar = $"__rawContent_{methodInfo.MethodName}";

        codeBuilder.AppendLine("#if NET6_0_OR_GREATER");
        codeBuilder.AppendLine($"            var {rawContentVar} = await __httpResponse.Content.ReadAsStringAsync({GetCancellationTokenArgument(cancellationTokenArg)}).ConfigureAwait(false);");
        codeBuilder.AppendLine("#else");
        codeBuilder.AppendLine($"            var {rawContentVar} = await __httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);");
        codeBuilder.AppendLine("#endif");

        var deserializeTypeWithoutNullable = deserializeType.EndsWith("?", StringComparison.OrdinalIgnoreCase) ? deserializeType.TrimEnd('?') : deserializeType;
        var innerTypeWithoutNullable = GetVariableTypeString(deserializeType);

        if (TypeDetectionHelper.IsStringType(deserializeTypeWithoutNullable))
        {
            if (methodInfo.ResponseEnableDecrypt)
            {
                codeBuilder.AppendLine($"            var __decryptedContent = {httpClient}.DecryptContent({rawContentVar});");
                codeBuilder.AppendLine($"            return __decryptedContent;");
            }
            else
            {
                codeBuilder.AppendLine($"            return {rawContentVar};");
            }
            return;
        }

        if (isXmlResponse)
        {
            codeBuilder.AppendLine($"            {innerTypeWithoutNullable} {resultVariable};");
            codeBuilder.AppendLine($"            try");
            codeBuilder.AppendLine($"            {{");
            codeBuilder.AppendLine($"                var __deserializedObj = {GetXmlSerializerFieldReference(deserializeType)}.Deserialize(new System.IO.StringReader({rawContentVar}));");
            codeBuilder.AppendLine($"                {resultVariable} = __deserializedObj is {deserializeType} __typed ? __typed : default;");
            codeBuilder.AppendLine($"            }}");
            codeBuilder.AppendLine($"            catch (System.Exception ex) when (ex is System.InvalidOperationException or System.Xml.XmlException)");
            codeBuilder.AppendLine($"            {{");
            codeBuilder.AppendLine($"                throw new Mud.HttpUtils.ApiException(__httpResponse.StatusCode, \"Failed to deserialize XML response: \" + ex.Message + \". Raw content: \" + {rawContentVar}, __httpRequest.RequestUri?.ToString());");
            codeBuilder.AppendLine($"            }}");
        }
        else
        {
            codeBuilder.AppendLine($"            {innerTypeWithoutNullable} {resultVariable};");
            codeBuilder.AppendLine($"            try");
            codeBuilder.AppendLine($"            {{");
            codeBuilder.AppendLine($"                {resultVariable} = System.Text.Json.JsonSerializer.Deserialize<{deserializeType}>({rawContentVar}, _jsonSerializerOptions);");
            codeBuilder.AppendLine($"            }}");
            codeBuilder.AppendLine($"            catch (System.Text.Json.JsonException ex)");
            codeBuilder.AppendLine($"            {{");
            codeBuilder.AppendLine($"                throw new Mud.HttpUtils.ApiException(__httpResponse.StatusCode, \"Failed to deserialize JSON response: \" + ex.Message + \". Raw content: \" + {rawContentVar}, __httpRequest.RequestUri?.ToString());");
            codeBuilder.AppendLine($"            }}");
        }

        if (methodInfo.ResponseEnableDecrypt)
        {
            string encryptableClient = httpClient;
            codeBuilder.AppendLine($"            if ({resultVariable} != null)");
            codeBuilder.AppendLine($"            {{");
            codeBuilder.AppendLine($"                var __rawJson = JsonSerializer.Serialize({resultVariable}, _jsonSerializerOptions);");
            codeBuilder.AppendLine($"                var __decryptedJson = {encryptableClient}.DecryptContent(__rawJson);");
            codeBuilder.AppendLine($"                {resultVariable} = JsonSerializer.Deserialize<{deserializeType}>(__decryptedJson, _jsonSerializerOptions);");
            codeBuilder.AppendLine($"            }}");
        }

        codeBuilder.AppendLine($"            return {GetReturnExpression(resultVariable, deserializeType)};");
    }

    private string GetCancellationTokenArgument(string cancellationTokenArg)
    {
        if (string.IsNullOrEmpty(cancellationTokenArg))
            return string.Empty;
        return cancellationTokenArg.Remove(0, 1);
    }

    /// <summary>
    /// 生成返回表达式，当反序列化变量类型与方法返回类型不一致时进行适当转换
    /// </summary>
    private static string GetReturnExpression(string resultVariable, string deserializeType)
    {
        if (string.IsNullOrEmpty(deserializeType))
            return resultVariable;

        // 如果原始返回类型不是可空类型（不以 ? 结尾），
        // 需要将可空的变量值（T?）转换为非空类型（T）
        if (!deserializeType.EndsWith("?", StringComparison.OrdinalIgnoreCase))
        {
            // 值类型：使用.GetValueOrDefault()，这比强制转换更安全
            var baseType = deserializeType.TrimEnd('.');
            var valueTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "int", "long", "float", "double", "decimal", "bool", "char",
                "byte", "sbyte", "short", "ushort", "uint", "ulong",
                "System.Int32", "System.Int64", "System.Single", "System.Double",
                "System.Decimal", "System.Boolean", "System.Char",
                "System.Byte", "System.SByte", "System.Int16", "System.UInt16",
                "System.UInt32", "System.UInt64"
            };

            if (valueTypes.Contains(baseType))
            {
                return $"{resultVariable}.GetValueOrDefault()";
            }

            // 引用类型：使用 ! null-forgiving 操作符
            return $"{resultVariable}!";
        }

        return resultVariable;
    }

    /// <summary>
    /// 生成标准模式的执行代码（默认行为，错误状态码抛出 ApiException）
    /// </summary>
    private void GenerateStandardExecution(StringBuilder codeBuilder, MethodAnalysisResult methodInfo, string httpClient, string cancellationTokenArg, string deserializeType)
    {
        var isVoidType = deserializeType == "void" || deserializeType == "System.Void";

        codeBuilder.AppendLine($"            using var __httpResponse = await {httpClient}.SendRawAsync(__httpRequest{cancellationTokenArg}).ConfigureAwait(false);");

        codeBuilder.AppendLine($"            if (!__httpResponse.IsSuccessStatusCode)");
        codeBuilder.AppendLine($"            {{");
        codeBuilder.AppendLine("#if NET6_0_OR_GREATER");
        codeBuilder.AppendLine($"               var __errorContent = await __httpResponse.Content.ReadAsStringAsync({GetCancellationTokenArgument(cancellationTokenArg)}).ConfigureAwait(false);");
        codeBuilder.AppendLine("#else");
        codeBuilder.AppendLine($"               var __errorContent = await __httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);");
        codeBuilder.AppendLine("#endif");
        codeBuilder.AppendLine($"                throw new Mud.HttpUtils.ApiException(__httpResponse.StatusCode, __errorContent, __httpRequest.RequestUri?.ToString());");
        codeBuilder.AppendLine($"            }}");

        if (isVoidType)
            return;

        var responseContentType = methodInfo.ResponseContentType;
        var isXmlResponse = ContentTypeHelper.IsXmlContentType(responseContentType);
        var resultVariable = $"__result_{methodInfo.MethodName}";

        var rawContentVar = $"__rawContent_{methodInfo.MethodName}";

        codeBuilder.AppendLine("#if NET6_0_OR_GREATER");
        codeBuilder.AppendLine($"            var {rawContentVar} = await __httpResponse.Content.ReadAsStringAsync({GetCancellationTokenArgument(cancellationTokenArg)}).ConfigureAwait(false);");
        codeBuilder.AppendLine("#else");
        codeBuilder.AppendLine($"            var {rawContentVar} = await __httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);");
        codeBuilder.AppendLine("#endif");

        var deserializeTypeWithoutNullable = deserializeType.EndsWith("?", StringComparison.OrdinalIgnoreCase) ? deserializeType.TrimEnd('?') : deserializeType;
        var innerTypeWithoutNullable = GetVariableTypeString(deserializeType);

        if (TypeDetectionHelper.IsStringType(deserializeTypeWithoutNullable))
        {
            codeBuilder.AppendLine($"            return {rawContentVar};");
            return;
        }

        if (isXmlResponse)
        {
            codeBuilder.AppendLine($"            {innerTypeWithoutNullable} {resultVariable};");
            codeBuilder.AppendLine($"            try");
            codeBuilder.AppendLine($"            {{");
            codeBuilder.AppendLine($"                var __deserializedObj = {GetXmlSerializerFieldReference(deserializeType)}.Deserialize(new System.IO.StringReader({rawContentVar}));");
            codeBuilder.AppendLine($"                {resultVariable} = __deserializedObj is {deserializeType} __typed ? __typed : throw new System.InvalidOperationException(\"Failed to deserialize XML response to type \" + typeof({deserializeType}).Name);");
            codeBuilder.AppendLine($"            }}");
            codeBuilder.AppendLine($"            catch (System.InvalidOperationException ex) when (ex.Message.StartsWith(\"Failed to deserialize XML\"))");
            codeBuilder.AppendLine($"            {{");
            codeBuilder.AppendLine($"                throw;");
            codeBuilder.AppendLine($"            }}");
            codeBuilder.AppendLine($"            catch (System.Exception ex) when (ex is System.InvalidOperationException or System.Xml.XmlException)");
            codeBuilder.AppendLine($"            {{");
            codeBuilder.AppendLine($"                throw new Mud.HttpUtils.ApiException(__httpResponse.StatusCode, \"Failed to deserialize XML response: \" + ex.Message + \". Raw content: \" + {rawContentVar}, __httpRequest.RequestUri?.ToString());");
            codeBuilder.AppendLine($"            }}");
        }
        else
        {
            codeBuilder.AppendLine($"            {innerTypeWithoutNullable} {resultVariable};");
            codeBuilder.AppendLine($"            try");
            codeBuilder.AppendLine($"            {{");
            codeBuilder.AppendLine($"                {resultVariable} = System.Text.Json.JsonSerializer.Deserialize<{deserializeType}>({rawContentVar}, _jsonSerializerOptions);");
            codeBuilder.AppendLine($"            }}");
            codeBuilder.AppendLine($"            catch (System.Text.Json.JsonException ex)");
            codeBuilder.AppendLine($"            {{");
            codeBuilder.AppendLine($"                throw new Mud.HttpUtils.ApiException(__httpResponse.StatusCode, \"Failed to deserialize JSON response: \" + ex.Message + \". Raw content: \" + {rawContentVar}, __httpRequest.RequestUri?.ToString());");
            codeBuilder.AppendLine($"            }}");
        }

        if (methodInfo.ResponseEnableDecrypt)
        {
            string encryptableClient = httpClient;
            codeBuilder.AppendLine($"            if ({resultVariable} != null)");
            codeBuilder.AppendLine($"            {{");
            codeBuilder.AppendLine($"                var __rawJson = JsonSerializer.Serialize({resultVariable}, _jsonSerializerOptions);");
            codeBuilder.AppendLine($"                var __decryptedJson = {encryptableClient}.DecryptContent(__rawJson);");
            codeBuilder.AppendLine($"                {resultVariable} = JsonSerializer.Deserialize<{deserializeType}>(__decryptedJson, _jsonSerializerOptions);");
            codeBuilder.AppendLine($"            }}");
        }

        codeBuilder.AppendLine($"            return {GetReturnExpression(resultVariable, deserializeType)};");
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
            codeBuilder.AppendLine($"                foreach (var __item in {param.Name})");
            codeBuilder.AppendLine("                {");
            codeBuilder.AppendLine($"                    if (__item != null)");
            codeBuilder.AppendLine("                    {");
            codeBuilder.AppendLine($"                        __queryParams.Add(\"{paramName}\", __item.ToString());");
            codeBuilder.AppendLine("                    }");
            codeBuilder.AppendLine("                }");
        }
        else
        {
            // 使用分隔符连接格式：user_ids=id0;id1;id2
            codeBuilder.AppendLine($"                var __joinedValues = string.Join(\"{separator}\", {param.Name}.Where(__item => __item != null).Select(__item => __item.ToString()));");
            codeBuilder.AppendLine($"                __queryParams.Add(\"{paramName}\", __joinedValues);");
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
            codeBuilder.AppendLine($"                var __joinedValues = string.Join(\";\", {param.Name}.Where(__item => __item != null).Select(__item => __item.ToString()));");
            codeBuilder.AppendLine($"                __queryParams.Add(\"{paramName}\", __joinedValues);");
            codeBuilder.AppendLine("            }");
        }
        else if (TypeDetectionHelper.IsStringType(param.Type))
        {
            codeBuilder.AppendLine($"            if (!string.IsNullOrWhiteSpace({param.Name}))");
            codeBuilder.AppendLine("            {");
            codeBuilder.AppendLine($"                __queryParams.Add(\"{paramName}\", {param.Name});");
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
                codeBuilder.AppendLine($"                __queryParams.Add(\"{paramName}\", {param.Name}{formatExpression});");
            }
            else if (TypeDetectionHelper.IsValueType(param.Type))
            {
                var formatExpression = !string.IsNullOrEmpty(formatString)
                  ? $".ToString(\"{formatString}\")"
                  : ".ToString()";
                codeBuilder.AppendLine($"            __queryParams.Add(\"{paramName}\", {param.Name}{formatExpression});");
            }
            else
            {
                // 引用类型需要 null 检查
                var formatExpression = !string.IsNullOrEmpty(formatString)
                  ? $".ToString(\"{formatString}\")"
                  : ".ToString()";
                codeBuilder.AppendLine($"            if ({param.Name} != null)");
                codeBuilder.AppendLine($"                __queryParams.Add(\"{paramName}\", {param.Name}{formatExpression});");
            }
        }
    }

    private void GenerateComplexQueryParameter(StringBuilder codeBuilder, ParameterInfo param)
    {
        codeBuilder.AppendLine($"            if ({param.Name} != null)");
        codeBuilder.AppendLine("            {");
        codeBuilder.AppendLine($"                if ({param.Name} is IQueryParameter __queryParam_{param.Name})");
        codeBuilder.AppendLine("                {");
        codeBuilder.AppendLine($"                    foreach (var __kvp in __queryParam_{param.Name}.ToQueryParameters())");
        codeBuilder.AppendLine("                    {");
        codeBuilder.AppendLine("                        if (!string.IsNullOrEmpty(__kvp.Value))");
        codeBuilder.AppendLine("                        {");
        codeBuilder.AppendLine("                            __queryParams.Add(__kvp.Key, __kvp.Value);");
        codeBuilder.AppendLine("                        }");
        codeBuilder.AppendLine("                    }");
        codeBuilder.AppendLine("                }");
        codeBuilder.AppendLine("                else");
        codeBuilder.AppendLine("                {");
        codeBuilder.AppendLine($"                    FlattenObjectToQueryParams({param.Name}, \"\", \"_\", __queryParams, false, false);");
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

        var urlEncode = true;
        if (queryMapAttr.NamedArguments.TryGetValue("UrlEncode", out var ueValue) && ueValue is bool ue)
            urlEncode = ue;

        var isDictionaryType = TypeDetectionHelper.IsDictionaryType(param.Type);

        var outerIndent = "            ";
        var innerIndent = "                ";
        if (param.IsValidated)
        {
            outerIndent = "            ";
            innerIndent = "            ";
        }
        else
        {
            codeBuilder.AppendLine($"{outerIndent}if ({param.Name} != null)");
            codeBuilder.AppendLine($"{outerIndent}{{");
        }

        if (isDictionaryType)
        {
            GenerateDictionaryQueryMap(codeBuilder, param, includeNullValues, serializationMethod, innerIndent, urlEncode);
        }
        else
        {
            codeBuilder.AppendLine($"{innerIndent}if ({param.Name} is IQueryParameter queryParam_{param.Name})");
            codeBuilder.AppendLine($"{innerIndent}{{");
            codeBuilder.AppendLine($"{innerIndent}    foreach (var kvp in queryParam_{param.Name}.ToQueryParameters())");
            codeBuilder.AppendLine($"{innerIndent}    {{");
            if (includeNullValues)
            {
                GenerateQueryMapValueAddition(codeBuilder, param, serializationMethod, "kvp.Key", "kvp.Value", $"{innerIndent}        ", urlEncode);
            }
            else
            {
                codeBuilder.AppendLine($"{innerIndent}        if (!string.IsNullOrEmpty(kvp.Value))");
                codeBuilder.AppendLine($"{innerIndent}        {{");
                GenerateQueryMapValueAddition(codeBuilder, param, serializationMethod, "kvp.Key", "kvp.Value", $"{innerIndent}            ", urlEncode);
                codeBuilder.AppendLine($"{innerIndent}        }}");
            }
            codeBuilder.AppendLine($"{innerIndent}    }}");
            codeBuilder.AppendLine($"{innerIndent}}}");
            codeBuilder.AppendLine($"{innerIndent}else");
            codeBuilder.AppendLine($"{innerIndent}{{");
            var useJson = serializationMethod == "Json" ? "true" : "false";
            var urlEncodeStr = urlEncode.ToString().ToLowerInvariant();
            var rawPairsArg = urlEncode ? "" : ", __rawQueryPairs";
            codeBuilder.AppendLine($"{innerIndent}    FlattenObjectToQueryParams({param.Name}, \"\", \"{separator}\", __queryParams, {includeNullValues.ToString().ToLowerInvariant()}, {useJson}, {urlEncodeStr}{rawPairsArg});");
            codeBuilder.AppendLine($"{innerIndent}}}");
        }

        if (!param.IsValidated)
        {
            codeBuilder.AppendLine($"{outerIndent}}}");
        }
    }

    private void GenerateQueryMapValueAddition(
        StringBuilder codeBuilder,
        ParameterInfo param,
        string serializationMethod,
        string keyExpression,
        string valueExpression,
        string indent = "                        ",
        bool urlEncode = true)
    {
        if (!urlEncode)
        {
            if (serializationMethod == "Json")
            {
                codeBuilder.AppendLine($"{indent}var __jsonValue_{param.Name} = System.Text.Json.JsonSerializer.Serialize({valueExpression});");
                codeBuilder.AppendLine($"{indent}__rawQueryPairs.Add(System.Uri.EscapeDataString({keyExpression}) + \"=\" + __jsonValue_{param.Name});");
            }
            else
            {
                codeBuilder.AppendLine($"{indent}__rawQueryPairs.Add(System.Uri.EscapeDataString({keyExpression}) + \"=\" + ({valueExpression}?.ToString() ?? string.Empty));");
            }
        }
        else
        {
            if (serializationMethod == "Json")
            {
                codeBuilder.AppendLine($"{indent}var __jsonValue_{param.Name} = System.Text.Json.JsonSerializer.Serialize({valueExpression});");
                codeBuilder.AppendLine($"{indent}__queryParams.Add({keyExpression}, __jsonValue_{param.Name});");
            }
            else
            {
                codeBuilder.AppendLine($"{indent}__queryParams.Add({keyExpression}, {valueExpression}?.ToString());");
            }
        }
    }

    private void GenerateDictionaryQueryMap(StringBuilder codeBuilder, ParameterInfo param, bool includeNullValues, string serializationMethod = "ToString", string indent = "                ", bool urlEncode = true)
    {
        codeBuilder.AppendLine($"{indent}foreach (var kvp_{param.Name} in {param.Name})");
        codeBuilder.AppendLine($"{indent}{{");
        if (includeNullValues)
        {
            GenerateQueryMapValueAddition(codeBuilder, param, serializationMethod, $"kvp_{param.Name}.Key", $"kvp_{param.Name}.Value", $"{indent}    ", urlEncode);
        }
        else
        {
            codeBuilder.AppendLine($"{indent}    if (kvp_{param.Name}.Value != null)");
            codeBuilder.AppendLine($"{indent}    {{");
            GenerateQueryMapValueAddition(codeBuilder, param, serializationMethod, $"kvp_{param.Name}.Key", $"kvp_{param.Name}.Value", $"{indent}        ", urlEncode);
            codeBuilder.AppendLine($"{indent}    }}");
        }
        codeBuilder.AppendLine($"{indent}}}");
    }

    private void GenerateInterfaceQueryProperty(StringBuilder codeBuilder, InterfacePropertyInfo property)
    {
        var paramName = property.ParameterName ?? property.Name;
        var formatExpression = !string.IsNullOrEmpty(property.Format)
            ? $".ToString(\"{property.Format}\")"
            : ".ToString()";

        if (property.Type == "string" || property.Type == "System.String")
        {
            codeBuilder.AppendLine($"            if (!string.IsNullOrWhiteSpace({property.Name}))");
            codeBuilder.AppendLine("            {");
            codeBuilder.AppendLine($"                __queryParams.Add(\"{paramName}\", {property.Name});");
            codeBuilder.AppendLine("            }");
        }
        else
        {
            codeBuilder.AppendLine($"            if ({property.Name} != null)");
            codeBuilder.AppendLine("            {");
            codeBuilder.AppendLine($"                __queryParams.Add(\"{paramName}\", {property.Name}{formatExpression});");
            codeBuilder.AppendLine("            }");
        }
    }

    /// <summary>
    /// 生成 RawQueryString 参数代码
    /// </summary>
    private void GenerateRawQueryStringParameter(StringBuilder codeBuilder, ParameterInfo param)
    {
        var rawQsVar = $"__rawQS_{param.Name}";
        if (param.IsValidated)
        {
            codeBuilder.AppendLine($"            var {rawQsVar} = {param.Name}.TrimStart('?', '&').TrimEnd('&');");
            codeBuilder.AppendLine($"            if (!string.IsNullOrWhiteSpace({rawQsVar}))");
            codeBuilder.AppendLine("            {");
            codeBuilder.AppendLine("                var __separator = __url.Contains('?') ? \"&\" : \"?\";");
            codeBuilder.AppendLine($"                __url += __separator + {rawQsVar};");
            codeBuilder.AppendLine("            }");
        }
        else
        {
            codeBuilder.AppendLine($"            if (!string.IsNullOrWhiteSpace({param.Name}))");
            codeBuilder.AppendLine("            {");
            codeBuilder.AppendLine($"                var {rawQsVar} = {param.Name}.TrimStart('?', '&').TrimEnd('&');");
            codeBuilder.AppendLine($"                if (!string.IsNullOrWhiteSpace({rawQsVar}))");
            codeBuilder.AppendLine("                {");
            codeBuilder.AppendLine("                    var __separator = __url.Contains('?') ? \"&\" : \"?\";");
            codeBuilder.AppendLine($"                    __url += __separator + {rawQsVar};");
            codeBuilder.AppendLine("                }");
            codeBuilder.AppendLine("            }");
        }
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

    internal static string GetXmlSerializerFieldReference(string typeName)
    {
        var safeName = typeName
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(",", "_")
            .Replace(" ", "")
            .Replace(".", "_")
            .Replace("[", "_")
            .Replace("]", "_");
        return $"_xmlSerializer_{safeName}";
    }

    #endregion
}
