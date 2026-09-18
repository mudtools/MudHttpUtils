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
    /// GEN-04（B-1）：按特性语义解析 format 位置。<c>[Path("yyyy-MM-dd")]</c> 的首参即 formatString
    /// （<c>PathAttribute</c> 的构造参数），<c>[Query("name","format")]</c> 的第二个位置参数是 format。
    /// 移除了此前对 <see cref="HttpClientGeneratorConstants.PathAttributes"/> 的显式排除（源于「Path 首参是 name」的错误假设，
    /// 参见文档 §7.1 修复点 3 / PathAttribute.cs 构造签名）。
    /// </summary>
    private string GetFormatString(ParameterAttributeInfo attribute)
        => AttributeArgumentReader.GetString(attribute,
            AttributeArgumentReader.ResolveFormatPosition(attribute.Name),
            "FormatString", "Format")!;

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
        // [Phase4 修复 5.1] 显式 StringComparison.Ordinal（CA1310）：URL 前缀属标识符级比较。
        if (urlTemplate.StartsWith("/", StringComparison.Ordinal))
        {
            return BuildUrlWithPlaceholders(urlTemplate, pathParams, methodInfo);
        }

        // 规则3：正常情况，拼接 BasePath
        if (!string.IsNullOrEmpty(basePath))
        {
            var normalizedBasePath = basePath!.TrimEnd('/');
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
        // 先 C# 字面量转义，再插值转义（I-16 / GEN-01）：
        // 模板被发射进 $"...{}..." 插值字符串，单花括号会造成插值洞。
        // EscapeForInterpolation 将 { } 双写为 {{ }}，此后模板内不存在单花括号，
        // 未匹配的占位符将保留为 {{token}}（发射后运行期为 {token} 字面量）。
        var sb = new StringBuilder(EscapeForInterpolation(StringEscapeHelper.EscapeString(urlTemplate)));
        var hasPathParams = pathParams.Any();

        if (methodInfo != null)
        {
            var isTokenPathMode = methodInfo.InterfaceTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModePath;
            if (isTokenPathMode && !string.IsNullOrEmpty(methodInfo.InterfaceTokenName))
            {
                var tokenPlaceholder = $"{{{methodInfo.InterfaceTokenName}}}";
                ReplacePlaceholder(sb, tokenPlaceholder, "{access_token}");
            }

            foreach (var pathParam in methodInfo.InterfacePathParameters)
            {
                var placeholder = $"{{{pathParam.Name}}}";
                var escapedValue = Uri.EscapeDataString(pathParam.Value ?? "");
                ReplacePlaceholder(sb, placeholder, escapedValue);
            }

            foreach (var interfacePathProp in methodInfo.InterfaceProperties.Where(p => p.AttributeType == "Path"))
            {
                var placeholder = $"{{{interfacePathProp.ParameterName}}}";

                var formatExpression = !string.IsNullOrEmpty(interfacePathProp.Format)
                    ? $".ToString(\"{StringEscapeHelper.EscapeString(interfacePathProp.Format)}\")"
                    : ".ToString()";

                if (interfacePathProp.UrlEncode)
                {
                    ReplacePlaceholder(sb, placeholder, $"{{System.Uri.EscapeDataString({interfacePathProp.Name}{formatExpression})}}");
                }
                else
                {
                    ReplacePlaceholder(sb, placeholder, $"{{{interfacePathProp.Name}{formatExpression}}}");
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
    /// 获取路径参数的占位符名称。
    /// <para>单一事实源：审计 F3 要求 HTTPCLIENT013 占位符判定与生成阶段的替换使用同一名称解析，
    /// 避免 <c>[Path(Name = "userId")] int id</c> 被校验侧误判为占位符缺失。</para>
    /// </summary>
    private static string GetPathParameterName(ParameterAttributeInfo pathAttr, string paramName)
    {
        if (pathAttr.NamedArguments.TryGetValue("Name", out var nameValue) && nameValue is string name && !string.IsNullOrEmpty(name))
            return name;
        return paramName;
    }

    /// <summary>
    /// 供生成器/校验器共用的「有效占位符名」（= <see cref="GetPathParameterName"/> 的公开出口，F3）。
    /// </summary>
    internal static string GetEffectivePathName(ParameterAttributeInfo pathAttr, string paramName)
        => GetPathParameterName(pathAttr, paramName);

    /// <summary>
    /// 占位符检索键与实际替换值不变，仅检索键改为转义形态（I-16 / GEN-01）。
    /// 模板花括号已被 <see cref="EscapeForInterpolation"/> 双写为 {{…}}，故在此把传入的
    /// {name} 检索键也转义为 {{name}}，再交给 <see cref="ReplacePlaceholderCore"/> 做大小写不敏感替换。
    /// 替换值本身保持单花括号的插值片段（如 {access_token}、{Uri.EscapeDataString(id)}），
    /// 发射进 $“…” 后即成为真正的插值孔。
    /// </summary>
    private static void ReplacePlaceholder(StringBuilder sb, string placeholder, string replacement)
        => ReplacePlaceholderCore(sb, EscapeForInterpolation(placeholder), replacement);

    /// <summary>
    /// I-16：把将发起进插值字符串的文本转义为插值字面量，从构造上杜绝 { } 插值洞。
    /// 注意本方法只服务于花括号插值场景，不处理 C# 字符串字面量转义（那是
    /// <see cref="StringEscapeHelper.EscapeString"/> 的职责），二者保持单一职责、互不污染。
    /// </summary>
    internal static string EscapeForInterpolation(string text)
        => text.Replace("{", "{{").Replace("}", "}}");

    /// <summary>
    /// 大小写不敏感替换（F3）：URL 模板 <c>{id}</c> 与 <c>[Path]</c> 参数 <c>Id</c> 应视为同一占位符。
    /// 生成阶段统一改用本方法，避免「校验阶段 OrdinalIgnoreCase、生成阶段 Ordinal」的语义分叉。
    /// URL 模板规模很小（单条路径），此处以清晰为优先。
    /// 检索键为转义形态（{{id}}），直接对转义后的模板 sb 操作。
    /// </summary>
    private static void ReplacePlaceholderCore(StringBuilder sb, string escapedPlaceholder, string replacement)
    {
        var current = sb.ToString();
        if (current.IndexOf(escapedPlaceholder, StringComparison.OrdinalIgnoreCase) < 0)
            return;

        // netstandard2.0 无 string.Replace(string, string, StringComparison) 重载，
        // 用 IndexOf 循环逐处替换（OrdinalIgnoreCase）。
        // [Phase5 修复 3.4] 复用已有 sb 容量（Clear 保留 Capacity）并直接 Append(StringBuilder)，
        // 省掉 newText.ToString() 的中间字符串分配。
        var newText = new StringBuilder(current.Length);
        var index = 0;
        while (true)
        {
            var found = current.IndexOf(escapedPlaceholder, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                newText.Append(current, index, current.Length - index);
                break;
            }
            newText.Append(current, index, found - index);
            newText.Append(replacement);
            index = found + escapedPlaceholder.Length;
        }

        sb.Clear();
        sb.Append(newText);
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

        // GEN-03：方法级固定查询参数（仅支持常量值）
        var hasMethodQueryParams = methodInfo.MethodQueryParameters.Any();

        if (!queryParams.Any() && !hasTokenQuery && !interfaceQueryProperties.Any() && !hasInterfaceQueryParams && !hasMethodQueryParams)
            return;

        // 按需声明变量：仅当参数或接口配置实际引用时才生成
        var needsQueryParams = hasTokenQuery || hasInterfaceQueryParams || interfaceQueryProperties.Any() ||
            queryParams.Any(QueryParameterBinder.UsesQueryParams) || hasMethodQueryParams;
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

        // GEN-03：方法级固定查询参数（在参数级与接口级查询之后追加，仅支持常量值）
        foreach (var methodQuery in methodInfo.MethodQueryParameters)
        {
            if (!string.IsNullOrEmpty(methodQuery.Name) && methodQuery.Value != null)
            {
                codeBuilder.AppendLine($"            __queryParams.Add(\"{StringEscapeHelper.EscapeString(methodQuery.Name)}\", \"{StringEscapeHelper.EscapeString(methodQuery.Value)}\");");
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
            // B-3（GEN-07）：NETSTANDARD2_0 → NET5_0_OR_GREATER（反向；HttpMethod.PATCH 是 .NET 5+ 能力符号）。
            codeBuilder.AppendLine(GeneratedCodeGuards.Net5OrGreater);
            codeBuilder.AppendLine($"            using var __httpRequest = new HttpRequestMessage(HttpMethod.{methodInfo.HttpMethod}, __url);");
            codeBuilder.AppendLine(GeneratedCodeGuards.Else);
            codeBuilder.AppendLine("            var __httpMethod = new HttpMethod(\"PATCH\");");
            codeBuilder.AppendLine($"            using var __httpRequest = new HttpRequestMessage(__httpMethod, __url);");
            codeBuilder.AppendLine(GeneratedCodeGuards.EndIf);
        }
        else
        {
            codeBuilder.AppendLine($"            using var __httpRequest = new HttpRequestMessage(HttpMethod.{methodInfo.HttpMethod}, __url);");
        }

        // M2-#12：[Retry(AllowNonIdempotent = true)] → 向请求写入放行标记（重试决策层读取，
        // 全局路径 ResilientHttpClient 与方法级路径 ResiliencePolicyResolver 均识别）
        if (methodInfo.RetryEnabled && methodInfo.RetryAllowNonIdempotent)
        {
            // B-3（GEN-07）：NETSTANDARD2_0 → NET5_0_OR_GREATER（反向；Options.TryAdd 是 .NET 5+ 能力符号）。
            codeBuilder.AppendLine(GeneratedCodeGuards.Net5OrGreater);
            codeBuilder.AppendLine($"            __httpRequest.Options.TryAdd(HttpExecutionConstants.AllowNonIdempotentRetryPropertyKey, true);");
            codeBuilder.AppendLine(GeneratedCodeGuards.Else);
            codeBuilder.AppendLine($"            __httpRequest.Properties[HttpExecutionConstants.AllowNonIdempotentRetryPropertyKey] = true;");
            codeBuilder.AppendLine(GeneratedCodeGuards.EndIf);
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
            // [F13 修复] 在写入点转义 ContentType 字面量：ContentType 来自用户特性，含 " 或 \ 时
            // 直接拼接会产出非法 C# 字符串字面量。
            contentTypeExpression = $"\"{StringEscapeHelper.EscapeString(contentType)}\"";
            effectiveContentType = contentType;
        }
        else
        {
            effectiveContentType = methodInfo.GetEffectiveContentType();
            contentTypeExpression = !string.IsNullOrEmpty(effectiveContentType)
                ? $"\"{StringEscapeHelper.EscapeString(effectiveContentType)}\""
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
            var escapedPropertyName = StringEscapeHelper.EscapeString(propertyName);

            if (serializeType == "Xml")
            {
                // EncryptContent(object, string, SerializeType) 已标记 [Obsolete]（运行时反射，AOT 不安全），
                // 但泛型 AOT 安全重载仅支持 JSON，XML 加密无替代 API。故在调用点就地抑制消费方的 CS0618；
                // AOT 不安全事实由 XML 路径既有的 AOT 诊断（AOT007）与方法级 IL2026/IL3050 抑制承担。
                codeBuilder.AppendLine("#pragma warning disable CS0618 // XML 加密无 AOT 安全重载，只能沿用已过时的 object 重载");
                codeBuilder.AppendLine($"            var __encryptedContent = {httpClient}.EncryptContent({bodyParam.Name}, \"{escapedPropertyName}\", SerializeType.{serializeType});");
                codeBuilder.AppendLine("#pragma warning restore CS0618");
            }
            else
            {
                // [警告修复] 改用 AOT 安全的泛型重载 EncryptContent<T>(T, string)：原 object 重载在消费方编译时
                // 会产生 CS0618（已过时）及 IL2026/IL3050（反射/动态代码）告警，泛型重载语义等价且零反射。
                var encryptTypeName = bodyParam.Type.TrimEnd();
                if (encryptTypeName.EndsWith("?", StringComparison.Ordinal))
                    encryptTypeName = encryptTypeName.Substring(0, encryptTypeName.Length - 1).TrimEnd();

                codeBuilder.AppendLine($"            var __encryptedContent = {httpClient}.EncryptContent<{encryptTypeName}>({bodyParam.Name}, \"{escapedPropertyName}\");");
            }

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
            // [Phase 3.1 全量收敛] Body 序列化统一委托 IHttpContentSerializer.ToHttpContent，
            // 不再直接调用 JsonSerializer.Serialize。生成代码持有 _contentSerializer（由 DI 注入）。
            // ToHttpContent 内部使用泛型 JsonSerializer.Serialize<T>（AOT 安全），无需在生成代码中区分泛型/非泛型重载。
            codeBuilder.AppendLine($"            __httpRequest.Content = _contentSerializer.ToHttpContent({bodyParam.Name});");
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
    /// <remarks>
    /// [D-04 设计说明] 此路径与 <c>FormField&lt;TBody&gt;</c> 描述符模式功能等价，
    /// 但采用直接属性访问代码（AOT 安全）。FormField&lt;TBody&gt; 类型作为可选描述符存在，
    /// 供未来统一序列化入口使用。当前保留直接属性访问以避免不必要的间接层。
    /// </remarks>
    private void GenerateUrlEncodedFormParameter(StringBuilder codeBuilder, List<ParameterInfo> formParams)
    {
        // [F2 修复] 使用 global:: 限定 BCL 泛型，降低对 using 的隐式依赖（System.Collections.Generic）
        codeBuilder.AppendLine("            var __formParameters = new global::System.Collections.Generic.Dictionary<string, string>();");

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
    /// [D-04 设计说明] 此路径与 FormField&lt;TBody&gt; 描述符模式功能等价，
    /// 但采用编译期属性枚举代码（AOT 安全）。FormField&lt;TBody&gt; 类型作为可选描述符存在，
    /// 供未来统一序列化入口使用。当前保留编译期属性枚举以避免不必要的间接层。
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

            codeBuilder.AppendLine($"                var __bodyFormParams = new global::System.Collections.Generic.Dictionary<string, string>();");

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
            codeBuilder.AppendLine($"                var __bodyFormParams = new global::System.Collections.Generic.Dictionary<string, string>();");
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
        // GEN-09：方法级 Token(Name) > 接口级 > 默认。
        var tokenName = methodInfo.EffectiveTokenName;
        if (!string.IsNullOrEmpty(tokenName))
            return tokenName!;

        var queryAttr = methodInfo.InterfaceAttributes?.FirstOrDefault(attr => attr.StartsWith("Query:", StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(queryAttr))
            return queryAttr!.Substring(6);

        return "access_token";
    }

    internal string? GetTokenHeaderName(MethodAnalysisResult methodInfo)
    {
        // GEN-09：方法级 Token(Name) > 接口级。
        var tokenName = methodInfo.EffectiveTokenName;
        if (!string.IsNullOrEmpty(methodInfo.InterfaceTokenInjectionMode) &&
            methodInfo.InterfaceTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModeHeader &&
            !string.IsNullOrEmpty(tokenName))
            return tokenName;

        var headerAttr = methodInfo.InterfaceAttributes?.FirstOrDefault(attr => attr.StartsWith("Header:", StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(headerAttr))
            return headerAttr!.Substring(7);

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
                    // [CS8604 修复] 可空 string 路径参数（如 `string? task_id`）直接传入 EscapeDataString
                    // 会触发 CS8604（其形参 stringToEscape 声明为非空）。
                    // 按「保持既有运行期语义」处理：null 时仍然抛 ArgumentNullException，
                    // 只是把参数名指向真正的路径参数（原先由 EscapeDataString 内部抛出，ParamName 是 stringToEscape），
                    // 同时让流分析判定表达式非空。非可空 string 参数不受影响，生成文本保持简短。
                    var stringExpr = paramType.TrimEnd().EndsWith("?", StringComparison.Ordinal)
                        ? $"{paramName} ?? throw new System.ArgumentNullException(nameof({paramName}))"
                        : paramName;
                    ReplacePlaceholder(sb, placeholder, $"{{Uri.EscapeDataString({stringExpr})}}");
                }
                else
                {
                    ReplacePlaceholder(sb, placeholder, $"{{Uri.EscapeDataString(({paramName}).ToString() ?? string.Empty)}}");
                }
            }
            else
            {
                ReplacePlaceholder(sb, placeholder, $"{{{paramName}}}");
            }
            return;
        }

        if (formatString!.Contains("{0}"))
        {
            var escapedFormat = StringEscapeHelper.EscapeString(formatString);
            var formatExpr = $"string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{escapedFormat}\", {paramName})";
            if (urlEncode)
            {
                ReplacePlaceholder(sb, placeholder, $"{{Uri.EscapeDataString({formatExpr})}}");
            }
            else
            {
                ReplacePlaceholder(sb, placeholder, $"{{{formatExpr}}}");
            }
            return;
        }

        var escapedStandardFormat = StringEscapeHelper.EscapeString(formatString);
        var standardFormatExpr = $"string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{{0:{escapedStandardFormat}}}\", {paramName})";
        if (urlEncode)
        {
            ReplacePlaceholder(sb, placeholder, $"{{Uri.EscapeDataString({standardFormatExpr})}}");
        }
        else
        {
            ReplacePlaceholder(sb, placeholder, $"{{{standardFormatExpr}}}");
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

        // [F8 修复] Ignore 语义修正：HeaderMergeMode 控制的是「方法参数级」Header 与接口级 Header 的合并规则，
        // 接口属性级 Header（运行时动态值）在任何 mode 下都应生效（与文档 HeaderMergeAttribute.Ignore
        // = "方法级头部忽略，只使用接口级头部" 一致）。原实现在此短路跳过接口属性级 Header（语义颠倒），
        // 导致 Ignore 模式下接口 [Header] 属性被静默丢弃。方法参数级 Header 的 Ignore 跳过由
        // HeaderParameterBinder.GenerateBindingCode 处理（HeaderParameterBinder.cs:34-39）。
        var headerMergeMode = methodInfo.HeaderMergeMode;

        foreach (var property in headerProperties)
        {
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
                // [GEN-18][§8.6] 运行期守卫：net4x/netstandard2.0 编译的 HttpClient 不校验头值 CR/LF，
                // 显式拒绝含 CR/LF 的头值以阻止头部注入。
                codeBuilder.AppendLine($"            if (!global::Mud.HttpUtils.HttpHeaderValueValidator.IsValid({property.Name}))");
                codeBuilder.AppendLine($"                throw new global::System.ArgumentException(\"HTTP 头值包含非法字符（CR/LF）。\", nameof({property.Name}));");
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
                // FIX-05: property.Format 必须转义，否则含 " 或 \ 的格式串会导致生成代码语法错误
                var escapedPropertyFormat = StringEscapeHelper.EscapeString(property.Format);
                var formatExpression = !string.IsNullOrEmpty(property.Format)
                    ? $"string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{{0:{escapedPropertyFormat}}}\", {property.Name})"
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

    private string GetBodyContentType(ParameterAttributeInfo bodyAttr)
    {
        // 先检查构造函数参数（如 [Body("application/xml")]）
        if (bodyAttr.Arguments.Length > 0)
        {
            var ctorContentType = bodyAttr.Arguments[0]?.ToString();
            if (!string.IsNullOrEmpty(ctorContentType))
                return ctorContentType!;
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
