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
        // 转义 URL 模板中的特殊字符（\ 和 "），占位符 {name} 不含这些字符，转义不影响后续替换
        var sb = new StringBuilder(StringEscapeHelper.EscapeString(urlTemplate));
        var hasPathParams = pathParams.Any();

        if (methodInfo != null)
        {
            var isTokenPathMode = methodInfo.InterfaceTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModePath;
            if (isTokenPathMode && !string.IsNullOrEmpty(methodInfo.InterfaceTokenName))
            {
                var tokenPlaceholder = $"{{{methodInfo.InterfaceTokenName}}}";
                sb.Replace(tokenPlaceholder, "{access_token}");
            }

            foreach (var pathParam in methodInfo.InterfacePathParameters)
            {
                var placeholder = $"{{{pathParam.Name}}}";
                var escapedValue = Uri.EscapeDataString(pathParam.Value ?? "");
                sb.Replace(placeholder, escapedValue);
            }

            foreach (var interfacePathProp in methodInfo.InterfaceProperties.Where(p => p.AttributeType == "Path"))
            {
                var placeholder = $"{{{interfacePathProp.ParameterName}}}";

                var formatExpression = !string.IsNullOrEmpty(interfacePathProp.Format)
                    ? $".ToString(\"{StringEscapeHelper.EscapeString(interfacePathProp.Format)}\")"
                    : ".ToString()";

                if (interfacePathProp.UrlEncode)
                {
                    sb.Replace(placeholder, $"{{System.Uri.EscapeDataString({interfacePathProp.Name}{formatExpression})}}");
                }
                else
                {
                    sb.Replace(placeholder, $"{{{interfacePathProp.Name}{formatExpression}}}");
                }
            }
        }

        if (hasPathParams)
        {
            foreach (var param in pathParams)
            {
                var pathAttr = param.Attributes.First(a => HttpClientGeneratorConstants.PathAttributes.Contains(a.Name));
                var placeholderName = GetPathParameterName(pathAttr, param.Name);

                var formatString = GetFormatString(pathAttr);
                var urlEncode = GetUrlEncodeValue(pathAttr);
                FormatUrlParameter(sb, placeholderName, formatString, urlEncode, param.Name, param.Type);
            }
        }

        return $"            var __url = $\"{sb}\";";
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

        // 按需声明变量：仅当参数或接口配置实际引用时才生成
        var needsQueryParams = hasTokenQuery || hasInterfaceQueryParams || interfaceQueryProperties.Any() ||
            queryParams.Any(QueryParameterBinder.UsesQueryParams);
        var needsRawQueryPairs = queryParams.Any(QueryParameterBinder.UsesRawQueryPairs);

        if (needsQueryParams)
            codeBuilder.AppendLine($"            var __queryParams = global::Mud.HttpUtils.QueryParameterBuilder.Create();");
        if (needsRawQueryPairs)
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
            codeBuilder.AppendLine($"            __queryParams.Add(\"{StringEscapeHelper.EscapeString(tokenQueryName)}\", access_token);");
        }

        foreach (var interfaceQuery in methodInfo.InterfaceQueryParameters)
        {
            if (!string.IsNullOrEmpty(interfaceQuery.Name) && interfaceQuery.Value != null)
            {
                codeBuilder.AppendLine($"            __queryParams.Add(\"{StringEscapeHelper.EscapeString(interfaceQuery.Name)}\", \"{StringEscapeHelper.EscapeString(interfaceQuery.Value)}\");");
            }
        }

        if (needsQueryParams)
        {
            codeBuilder.AppendLine("            if (__queryParams.Count > 0)");
            codeBuilder.AppendLine("            {");
            codeBuilder.AppendLine("                __url += \"?\" + __queryParams.ToString();");
            codeBuilder.AppendLine("            }");
        }
        if (needsRawQueryPairs)
        {
            codeBuilder.AppendLine("            if (__rawQueryPairs.Count > 0)");
            codeBuilder.AppendLine("            {");
            codeBuilder.AppendLine("                __url += (__url.Contains(\"?\") ? \"&\" : \"?\") + string.Join(\"&\", __rawQueryPairs);");
            codeBuilder.AppendLine("            }");
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
            string httpClient = hasHttpClient ? "_httpClient" : "__appContext.HttpClient";

            codeBuilder.AppendLine($"            var __encryptedContent = {httpClient}.EncryptContent({bodyParam.Name}, \"{StringEscapeHelper.EscapeString(propertyName)}\", SerializeType.{serializeType});");
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
            // AOT 改造（Phase 5）：使用静态 XmlSerializer 字段替代运行时 XmlSerialize.Serialize，
            // 消除 [RequiresDynamicCode] 路径。静态字段由 ConstructorGenerator 预生成。
            var xmlFieldRef = GetXmlSerializerFieldReference(bodyParam.Type);
            codeBuilder.AppendLine("            var __xmlSettings = new System.Xml.XmlWriterSettings { Encoding = Encoding.UTF8, Indent = true, IndentChars = \"  \", OmitXmlDeclaration = false };");
            codeBuilder.AppendLine("            using var __xmlStream = new System.IO.MemoryStream();");
            codeBuilder.AppendLine("            using var __xmlWriter = System.Xml.XmlWriter.Create(__xmlStream, __xmlSettings);");
            codeBuilder.AppendLine("            var __xmlNs = new System.Xml.Serialization.XmlSerializerNamespaces();");
            codeBuilder.AppendLine("            __xmlNs.Add(\"\", \"\");");
            codeBuilder.AppendLine($"            {xmlFieldRef}.Serialize(__xmlWriter, {bodyParam.Name}, __xmlNs);");
            codeBuilder.AppendLine("            __xmlWriter.Flush();");
            codeBuilder.AppendLine("            var __xmlContent = Encoding.UTF8.GetString(__xmlStream.ToArray());");
            codeBuilder.AppendLine($"            using var __xmlStrContent = new StringContent(__xmlContent, Encoding.UTF8, {contentTypeExpression});");
            codeBuilder.AppendLine("            __httpRequest.Content = __xmlStrContent;");
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

            if (TypeDetectionHelper.IsStringType(formParam.Type))
            {
                codeBuilder.AppendLine($"            if (!string.IsNullOrWhiteSpace({formParam.Name}))");
                codeBuilder.AppendLine("            {");
                codeBuilder.AppendLine($"                __formParameters[\"{StringEscapeHelper.EscapeString(fieldName)}\"] = {formParam.Name};");
                codeBuilder.AppendLine("            }");
            }
            else if (TypeDetectionHelper.IsValueType(formParam.Type) && !TypeDetectionHelper.IsNullableType(formParam.Type))
            {
                // 非可空值类型（int、long、Guid 等）永远不会为 null，无需 null 检查
                codeBuilder.AppendLine($"            __formParameters[\"{StringEscapeHelper.EscapeString(fieldName)}\"] = {formParam.Name}.ToString() ?? \"\";");
            }
            else
            {
                codeBuilder.AppendLine($"            if ({formParam.Name} != null)");
                codeBuilder.AppendLine("            {");
                codeBuilder.AppendLine($"                __formParameters[\"{StringEscapeHelper.EscapeString(fieldName)}\"] = {formParam.Name}.ToString() ?? \"\";");
                codeBuilder.AppendLine("            }");
            }
        }

        codeBuilder.AppendLine("            using var __formContent = new System.Net.Http.FormUrlEncodedContent(__formParameters);");
        codeBuilder.AppendLine("            __httpRequest.Content = __formContent;");
    }

    /// <summary>
    /// 生成 URL 编码的 Body 参数（用于 [SerializationMethod(FormUrlEncoded)] + [Body]）
    /// </summary>
    /// <remarks>
    /// AOT 改造（Task 2）：原实现使用运行时反射 <c>GetType().GetProperties()</c> 枚举属性，
    /// 在 Native AOT 裁剪后属性元数据丢失导致表单体为空。现改为编译期枚举属性并发射静态属性访问，
    /// 彻底消除反射依赖。若 <see cref="ParameterInfo.TypeSymbol"/> 不可用（如测试模拟场景），
    /// 则回退到原始反射路径并保留 IL2072 压制。
    /// </remarks>
    private void GenerateUrlEncodedBodyParameter(StringBuilder codeBuilder, ParameterInfo bodyParam)
    {
        // 非可空值类型永远不会为 null；已通过 ParameterValidationHelper 验证的参数也无需重复检查
        var isNonNullableValueType = TypeDetectionHelper.IsValueType(bodyParam.Type) && !TypeDetectionHelper.IsNullableType(bodyParam.Type);
        var needsNullCheck = !isNonNullableValueType && !bodyParam.IsValidated;

        if (needsNullCheck)
        {
            codeBuilder.AppendLine($"            if ({bodyParam.Name} != null)");
            codeBuilder.AppendLine("            {");
        }

        // 尝试使用编译期类型符号枚举属性（AOT 安全路径）
        if (bodyParam.TypeSymbol != null)
        {
            // 遍历继承链枚举所有公共可读属性（与运行时 GetProperties() 行为一致）
            var properties = new List<IPropertySymbol>();
            var currentType = bodyParam.TypeSymbol;
            while (currentType != null && currentType.SpecialType != SpecialType.System_Object)
            {
                var declaredProps = currentType.GetMembers()
                    .OfType<IPropertySymbol>()
                    .Where(p => p.DeclaredAccessibility == Accessibility.Public
                                && !p.IsStatic
                                && p.GetMethod != null
                                && p.GetMethod.DeclaredAccessibility == Accessibility.Public);
                foreach (var prop in declaredProps)
                {
                    // 避免重复添加被 override 的属性
                    if (!properties.Any(p => SymbolEqualityComparer.Default.Equals(p, prop.OriginalDefinition) || p.Name == prop.Name))
                        properties.Add(prop);
                }
                currentType = currentType.BaseType;
            }

            codeBuilder.AppendLine("                var __bodyFormParams = new Dictionary<string, string>();");

            foreach (var prop in properties)
            {
                var propName = prop.Name;
                var isValueType = prop.Type.IsValueType;
                var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated
                                 || (isValueType && prop.Type.OriginalDefinition?.SpecialType == SpecialType.System_Nullable_T);

                if (isValueType && !isNullable)
                {
                    // 非可空值类型：直接 ToString()
                    codeBuilder.AppendLine($"                __bodyFormParams[\"{propName}\"] = {bodyParam.Name}.{propName}.ToString() ?? \"\";");
                }
                else
                {
                    // 引用类型或可空值类型：加 null 检查
                    codeBuilder.AppendLine($"                var __val_{propName} = {bodyParam.Name}.{propName};");
                    codeBuilder.AppendLine($"                if (__val_{propName} != null)");
                    codeBuilder.AppendLine("                {");
                    codeBuilder.AppendLine($"                    __bodyFormParams[\"{propName}\"] = __val_{propName}.ToString() ?? \"\";");
                    codeBuilder.AppendLine("                }");
                }
            }

            codeBuilder.AppendLine("                using var __bodyFormContent = new System.Net.Http.FormUrlEncodedContent(__bodyFormParams);");
            codeBuilder.AppendLine("                __httpRequest.Content = __bodyFormContent;");
        }
        else
        {
            // 回退路径：TypeSymbol 不可用时使用反射（仅测试/模拟场景）
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
        }

        if (needsNullCheck)
        {
            codeBuilder.AppendLine("            }");
        }
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
                if (TypeDetectionHelper.IsStringType(formProp.Type))
                {
                    codeBuilder.AppendLine($"            if (!string.IsNullOrWhiteSpace({formProp.Name}))");
                    codeBuilder.AppendLine("            {");
                    codeBuilder.AppendLine($"                __multipartContent.Add(new System.Net.Http.StringContent({formProp.Name}), \"{StringEscapeHelper.EscapeString(fieldName)}\");");
                    codeBuilder.AppendLine("            }");
                }
                else if (TypeDetectionHelper.IsValueType(formProp.Type) && !TypeDetectionHelper.IsNullableType(formProp.Type))
                {
                    // 非可空值类型（int、long、Guid 等）永远不会为 null，无需 null 检查
                    codeBuilder.AppendLine($"            __multipartContent.Add(new System.Net.Http.StringContent({formProp.Name}.ToString() ?? \"\"), \"{StringEscapeHelper.EscapeString(fieldName)}\");");
                }
                else
                {
                    codeBuilder.AppendLine($"            if ({formProp.Name} != null)");
                    codeBuilder.AppendLine("            {");
                    codeBuilder.AppendLine($"                __multipartContent.Add(new System.Net.Http.StringContent({formProp.Name}.ToString() ?? \"\"), \"{StringEscapeHelper.EscapeString(fieldName)}\");");
                    codeBuilder.AppendLine("            }");
                }
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
                codeBuilder.AppendLine($"                __{uploadParam.Name}Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(\"{StringEscapeHelper.EscapeString(contentType)}\");");
                if (!string.IsNullOrEmpty(fileName))
                    codeBuilder.AppendLine($"                __multipartContent.Add(__{uploadParam.Name}Content, \"{StringEscapeHelper.EscapeString(fieldName)}\", \"{StringEscapeHelper.EscapeString(fileName)}\");");
                else
                    codeBuilder.AppendLine($"                __multipartContent.Add(__{uploadParam.Name}Content, \"{StringEscapeHelper.EscapeString(fieldName)}\");");
                codeBuilder.AppendLine("            }");
            }
            else
            {
                codeBuilder.AppendLine($"            if ({uploadParam.Name} != null)");
                codeBuilder.AppendLine("            {");
                if (!string.IsNullOrEmpty(fileName))
                    codeBuilder.AppendLine($"                __multipartContent.Add(new System.Net.Http.StreamContent({uploadParam.Name}), \"{StringEscapeHelper.EscapeString(fieldName)}\", \"{StringEscapeHelper.EscapeString(fileName)}\");");
                else
                    codeBuilder.AppendLine($"                __multipartContent.Add(new System.Net.Http.StreamContent({uploadParam.Name}), \"{StringEscapeHelper.EscapeString(fieldName)}\");");
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

    #region 辅助方法

    private bool ShouldGenerateTokenQuery(MethodAnalysisResult methodInfo)
    {
        if (!string.IsNullOrEmpty(methodInfo.InterfaceTokenInjectionMode) &&
            methodInfo.InterfaceTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModeQuery)
            return true;

        return methodInfo.InterfaceAttributes?.Any(attr => attr.StartsWith("Query:", StringComparison.Ordinal)) == true;
    }

    internal string GetTokenQueryName(MethodAnalysisResult methodInfo)
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

    private void FormatUrlParameter(StringBuilder sb, string placeholderName, string? formatString, bool urlEncode, string paramName, string paramType)
    {
        var placeholder = $"{{{placeholderName}}}";
        var isStringType = TypeDetectionHelper.IsStringType(paramType);

        if (string.IsNullOrEmpty(formatString))
        {
            if (urlEncode)
            {
                if (isStringType)
                {
                    sb.Replace(placeholder, $"{{Uri.EscapeDataString({paramName})}}");
                }
                else
                {
                    sb.Replace(placeholder, $"{{Uri.EscapeDataString(({paramName}).ToString() ?? string.Empty)}}");
                }
            }
            else
            {
                sb.Replace(placeholder, $"{{{paramName}}}");
            }
            return;
        }

        if (formatString.Contains("{0}"))
        {
            var escapedFormat = StringEscapeHelper.EscapeString(formatString);
            var formatExpr = $"string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{escapedFormat}\", {paramName})";
            if (urlEncode)
            {
                sb.Replace(placeholder, $"{{Uri.EscapeDataString({formatExpr})}}");
            }
            else
            {
                sb.Replace(placeholder, $"{{{formatExpr}}}");
            }
            return;
        }

        var escapedStandardFormat = StringEscapeHelper.EscapeString(formatString);
        var standardFormatExpr = $"string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{{0:{escapedStandardFormat}}}\", {paramName})";
        if (urlEncode)
        {
            sb.Replace(placeholder, $"{{Uri.EscapeDataString({standardFormatExpr})}}");
        }
        else
        {
            sb.Replace(placeholder, $"{{{standardFormatExpr}}}");
        }
    }

    private void GenerateInterfaceQueryProperty(StringBuilder codeBuilder, InterfacePropertyInfo property)
    {
        var paramName = property.ParameterName ?? property.Name;
        var escapedParamName = StringEscapeHelper.EscapeString(paramName);

        if (property.Type == "string" || property.Type == "System.String")
        {
            // Add() 内部已跳过 null/空白值，无需外部检查
            codeBuilder.AppendLine($"            __queryParams.Add(\"{escapedParamName}\", {property.Name});");
        }
        else
        {
            // 检查是否有类型专用的 Add 重载（如 int?, Guid?, DateTime?, bool? 等）
            var overloadKind = TypeDetectionHelper.GetQueryAddOverloadKind(property.Type);

            if (overloadKind == TypeDetectionHelper.QueryAddOverloadKind.WithFormat)
            {
                // 带格式化参数的重载：Add(name, value, formatString)
                var formatArg = !string.IsNullOrEmpty(property.Format)
                    ? $"\"{StringEscapeHelper.EscapeString(property.Format)}\""
                    : "null";
                codeBuilder.AppendLine($"            __queryParams.Add(\"{escapedParamName}\", {property.Name}, {formatArg});");
            }
            else if (TypeDetectionHelper.IsValueType(property.Type) && !TypeDetectionHelper.IsNullableType(property.Type))
            {
                // 非可空值类型（无专用重载，如 byte, char 等）：使用 ToString()
                var formatExpression = !string.IsNullOrEmpty(property.Format)
                    ? $".ToString(\"{StringEscapeHelper.EscapeString(property.Format)}\")"
                    : ".ToString()";
                codeBuilder.AppendLine($"            __queryParams.Add(\"{escapedParamName}\", {property.Name}{formatExpression});");
            }
            else
            {
                // 可空值类型和引用类型（无专用重载）：使用 ?.ToString()，Add() 会跳过 null 值
                var formatExpression = !string.IsNullOrEmpty(property.Format)
                    ? $"?.ToString(\"{StringEscapeHelper.EscapeString(property.Format)}\")"
                    : "?.ToString()";
                codeBuilder.AppendLine($"            __queryParams.Add(\"{escapedParamName}\", {property.Name}{formatExpression});");
            }
        }
    }

    /// <summary>
    /// 生成接口级 Header 属性的请求头添加代码。
    /// 属性级 Header 的值在运行时动态设置，与方法参数 Header 和接口级静态 Header 区分。
    /// 遵循 HeaderMergeMode 规则，并处理 Authorization 与 Token 注入的冲突。
    /// </summary>
    public void GenerateInterfaceHeaderProperties(StringBuilder codeBuilder, MethodAnalysisResult methodInfo, bool hasTokenManager)
    {
        var headerProperties = methodInfo.InterfaceProperties
            .Where(p => p.AttributeType == "Header")
            .ToList();

        if (headerProperties.Count == 0)
            return;

        var headerMergeMode = methodInfo.HeaderMergeMode;
        var shouldIgnore = headerMergeMode == "Ignore";

        foreach (var property in headerProperties)
        {
            if (shouldIgnore)
                continue;

            var headerName = property.ParameterName ?? property.Name;
            if (string.IsNullOrEmpty(headerName))
                continue;

            // 当 TokenManager 存在且 Header 名为 Authorization 时跳过（由 Token 注入机制处理）
            if (hasTokenManager && headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                continue;

            var escapedHeaderName = StringEscapeHelper.EscapeString(headerName);
            var shouldReplace = property.Replace || headerMergeMode == "Replace";
            var isStringType = TypeDetectionHelper.IsStringType(property.Type);

            if (isStringType)
            {
                // string 类型：null/空白时跳过
                codeBuilder.AppendLine($"            if (!string.IsNullOrWhiteSpace({property.Name}))");
                if (shouldReplace)
                {
                    codeBuilder.AppendLine($"            {{");
                    codeBuilder.AppendLine($"                __httpRequest.Headers.Remove(\"{escapedHeaderName}\");");
                    codeBuilder.AppendLine($"                __httpRequest.Headers.Add(\"{escapedHeaderName}\", {property.Name});");
                    codeBuilder.AppendLine($"            }}");
                }
                else
                {
                    codeBuilder.AppendLine($"                if (!__httpRequest.Headers.Contains(\"{escapedHeaderName}\"))");
                    codeBuilder.AppendLine($"                    __httpRequest.Headers.Add(\"{escapedHeaderName}\", {property.Name});");
                }
            }
            else
            {
                // 非 string 类型：使用格式化表达式
                var formatExpression = !string.IsNullOrEmpty(property.Format)
                    ? $"string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{{0:{property.Format}}}\", {property.Name})"
                    : $"{property.Name}.ToString()";

                // 值类型不会为 null，直接添加
                if (TypeDetectionHelper.IsValueType(property.Type) && !TypeDetectionHelper.IsNullableType(property.Type))
                {
                    if (shouldReplace)
                    {
                        codeBuilder.AppendLine($"            __httpRequest.Headers.Remove(\"{escapedHeaderName}\");");
                        codeBuilder.AppendLine($"            __httpRequest.Headers.Add(\"{escapedHeaderName}\", {formatExpression});");
                    }
                    else
                    {
                        codeBuilder.AppendLine($"            if (!__httpRequest.Headers.Contains(\"{escapedHeaderName}\"))");
                        codeBuilder.AppendLine($"                __httpRequest.Headers.Add(\"{escapedHeaderName}\", {formatExpression});");
                    }
                }
                else
                {
                    // 可空类型：null 时跳过
                    codeBuilder.AppendLine($"            if ({property.Name} != null)");
                    if (shouldReplace)
                    {
                        codeBuilder.AppendLine($"            {{");
                        codeBuilder.AppendLine($"                __httpRequest.Headers.Remove(\"{escapedHeaderName}\");");
                        codeBuilder.AppendLine($"                __httpRequest.Headers.Add(\"{escapedHeaderName}\", {formatExpression});");
                        codeBuilder.AppendLine($"            }}");
                    }
                    else
                    {
                        codeBuilder.AppendLine($"            {{");
                        codeBuilder.AppendLine($"                if (!__httpRequest.Headers.Contains(\"{escapedHeaderName}\"))");
                        codeBuilder.AppendLine($"                    __httpRequest.Headers.Add(\"{escapedHeaderName}\", {formatExpression});");
                        codeBuilder.AppendLine($"            }}");
                    }
                }
            }
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
            .Replace("]", "_")
            .Replace("+", "_")    // 嵌套类型分隔符（Parent+Child）
            .Replace("`", "_");   // 泛型 arity 标记（Dictionary`2）
        return $"_xmlSerializer_{safeName}";
    }

    #endregion
}
