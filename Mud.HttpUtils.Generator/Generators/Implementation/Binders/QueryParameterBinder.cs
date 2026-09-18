using Mud.HttpUtils;
using Mud.HttpUtils.Models;

// [Phase4 修复 5.1] CA1308（建议把 ToLowerInvariant 换成 ToUpperInvariant）在本文件内属**误报**：
// 生成产物需要小写字面量（C# 字面量、camelCase 的 JSON/查询参数名、内联类型名），
// 大写形式会产出语义错误或不可编译的代码（详见各调用点的参数名与命名约定）。
// 故按方案 5.1 的「显式 #pragma + 理由」方式就地抑制，而非全局 NoWarn。
#pragma warning disable CA1308

namespace Mud.HttpUtils.Generators.Implementation;

internal class QueryParameterBinder : IParameterBinder
{
    /// <summary>
    /// 判断参数是否会引用 __queryParams 变量。
    /// </summary>
    public static bool UsesQueryParams(ParameterInfo parameter)
    {
        // [RawQueryString] 直接修改 __url，不使用 __queryParams
        if (parameter.Attributes.Any(a => a.Name == HttpClientGeneratorConstants.RawQueryStringAttribute))
            return false;

        // [QueryMap] 字典类型在 UrlEncode = false 时仅使用 __rawQueryPairs
        var queryMapAttr = parameter.Attributes.FirstOrDefault(a => a.Name == HttpClientGeneratorConstants.QueryMapAttribute);
        if (queryMapAttr != null && IsDictionaryType(parameter.Type))
        {
            var urlEncode = !(queryMapAttr.NamedArguments.TryGetValue("UrlEncode", out var urlEnc) && urlEnc is false);
            return urlEncode;
        }

        // 其余类型（简单 [Query]、[ArrayQuery]、复杂 [Query]、对象 [QueryMap]）均使用 __queryParams
        return true;
    }

    /// <summary>
    /// 判断参数是否会引用 __rawQueryPairs 变量。
    /// </summary>
    public static bool UsesRawQueryPairs(ParameterInfo parameter)
    {
        // 复杂 [Query] 类型（非简单类型、非数组）通过 FlattenObjectToQueryParams 引用 __rawQueryPairs
        var queryAttr = parameter.Attributes.FirstOrDefault(a => a.Name == HttpClientGeneratorConstants.QueryAttribute);
        if (queryAttr != null)
            return !TypeDetectionHelper.IsSimpleType(parameter.Type) && !TypeDetectionHelper.IsArrayType(parameter.Type);

        // [QueryMap] 对象类型通过 FlattenObjectToQueryParams 引用 __rawQueryPairs
        var queryMapAttr = parameter.Attributes.FirstOrDefault(a => a.Name == HttpClientGeneratorConstants.QueryMapAttribute);
        if (queryMapAttr != null)
        {
            if (IsDictionaryType(parameter.Type))
            {
                // [QueryMap] 字典类型仅在 UrlEncode = false 时引用 __rawQueryPairs
                return queryMapAttr.NamedArguments.TryGetValue("UrlEncode", out var urlEnc) && urlEnc is false;
            }
            return true;
        }

        // [ArrayQuery]、[RawQueryString]、简单 [Query] 均不使用 __rawQueryPairs
        return false;
    }

    public bool CanBind(ParameterInfo parameter)
    {
        return parameter.Attributes.Any(attr =>
            attr.Name == HttpClientGeneratorConstants.QueryAttribute ||
            attr.Name == HttpClientGeneratorConstants.ArrayQueryAttribute ||
            attr.Name == HttpClientGeneratorConstants.QueryMapAttribute ||
            attr.Name == HttpClientGeneratorConstants.RawQueryStringAttribute);
    }

    public void GenerateBindingCode(StringBuilder codeBuilder, ParameterInfo parameter, MethodAnalysisResult methodInfo, string indent)
    {
        var queryAttr = parameter.Attributes.FirstOrDefault(a => a.Name == HttpClientGeneratorConstants.QueryAttribute);
        if (queryAttr != null)
        {
            GenerateQueryParameter(codeBuilder, parameter, queryAttr, indent);
            return;
        }

        var arrayQueryAttr = parameter.Attributes.FirstOrDefault(a => a.Name == HttpClientGeneratorConstants.ArrayQueryAttribute);
        if (arrayQueryAttr != null)
        {
            GenerateArrayQueryParameter(codeBuilder, parameter, arrayQueryAttr, indent);
            return;
        }

        var queryMapAttr = parameter.Attributes.FirstOrDefault(a => a.Name == HttpClientGeneratorConstants.QueryMapAttribute);
        if (queryMapAttr != null)
        {
            GenerateQueryMapParameter(codeBuilder, parameter, queryMapAttr, indent);
            return;
        }

        var rawQueryAttr = parameter.Attributes.FirstOrDefault(a => a.Name == HttpClientGeneratorConstants.RawQueryStringAttribute);
        if (rawQueryAttr != null)
        {
            GenerateRawQueryStringParameter(codeBuilder, parameter, rawQueryAttr, indent);
        }
    }

    private static void GenerateQueryParameter(StringBuilder codeBuilder, ParameterInfo param, ParameterAttributeInfo attr, string indent)
    {
        var paramName = GetQueryParameterName(attr, param.Name);
        var formatString = GetFormatString(attr);

        // CFG-04：消费 [Query] 的 Prefix / TreatAsString / SerializeNull（此前全仓无读取点）。
        var prefix = attr.NamedArguments.TryGetValue("Prefix", out var p) && p is string ps && ps.Length > 0 ? ps : null;
        var treatAsString = attr.NamedArguments.TryGetValue("TreatAsString", out var t) && t is true;
        var serializeNull = attr.NamedArguments.TryGetValue("SerializeNull", out var n) && n is true;

        if (TypeDetectionHelper.IsSimpleType(param.Type))
        {
            // 简单类型：SerializeNull 需要「允许 null 落键」的构造入口（AddAllowNull）。
            GenerateSimpleQueryParameter(codeBuilder, param, ApplyPrefix(paramName, prefix), formatString, attr, indent, allowNull: serializeNull);
        }
        else
        {
            GenerateComplexQueryParameter(codeBuilder, param, indent, prefix, treatAsString, serializeNull);
        }
    }

    /// <summary>
    /// CFG-04：契约 —— <c>Prefix="filter" + Name="keyword" =&gt; "filter.keyword"</c>。
    /// 顶层前缀用 <c>'.'</c> 与属性名连接；深层嵌套继续使用展平 separator。
    /// </summary>
    private static string ApplyPrefix(string name, string? prefix)
        => string.IsNullOrEmpty(prefix) ? name : prefix + "." + name;

    private static void GenerateSimpleQueryParameter(StringBuilder codeBuilder, ParameterInfo param, string paramName, string? formatString, ParameterAttributeInfo attr, string indent, bool allowNull = false)
    {
        var escapedName = StringEscapeHelper.EscapeString(paramName);

        if (TypeDetectionHelper.IsArrayType(param.Type))
        {
            // [Query] 数组默认使用重复参数模式（与 Separator = null 行为一致）
            GenerateArrayQueryUrl(codeBuilder, param, paramName, attr, indent, defaultSeparator: null);
        }
        else if (TypeDetectionHelper.IsStringType(param.Type))
        {
            // Add() 仅过滤 null（M2-#16A：空串/空白是显式值，按 "key=" 保留）；allowNull 时改用 AddAllowNull 保留 null 空值（?q=）。
            var method = allowNull ? "AddAllowNull" : "Add";
            codeBuilder.AppendLine($"{indent}__queryParams.{method}(\"{escapedName}\", {param.Name});");
        }
        else
        {
            // 检查是否有类型专用的 Add 重载（如 int?, Guid?, DateTime?, bool? 等）
            var overloadKind = TypeDetectionHelper.GetQueryAddOverloadKind(param.Type);

            if (overloadKind == TypeDetectionHelper.QueryAddOverloadKind.WithFormat)
            {
                // 带格式化参数的重载：Add(name, value, formatString)
                // 非可空和可空值类型均可直接传入，QueryParameterBuilder 内部处理 null
                var formatArg = !string.IsNullOrEmpty(formatString)
                    ? $"\"{StringEscapeHelper.EscapeString(formatString)}\""
                    : "null";

                if (allowNull && TypeDetectionHelper.IsNullableType(param.Type))
                {
                    // SerializeNull：null 时补发空键（?key=），保留既有格式化语义。
                    codeBuilder.AppendLine($"{indent}if ({param.Name}.HasValue)");
                    codeBuilder.AppendLine($"{indent}    __queryParams.Add(\"{escapedName}\", {param.Name}, {formatArg});");
                    codeBuilder.AppendLine($"{indent}else");
                    codeBuilder.AppendLine($"{indent}    __queryParams.AddAllowNull(\"{escapedName}\", string.Empty);");
                }
                else
                {
                    codeBuilder.AppendLine($"{indent}__queryParams.Add(\"{escapedName}\", {param.Name}, {formatArg});");
                }
            }
            else
            {
                // 无专用重载的类型（如 byte, char, DateTimeOffset, TimeSpan 等）：回退到 ToString()
                // GEN-06（B-2）：回退分支显式 Invariant，避免按 CurrentCulture 生成区域敏感串
                // （DateTimeOffset/TimeSpan/DateOnly/TimeOnly 已被 IsSimpleType 收录但不在 WithFormat 白名单）。
                // 仅对「可格式化值类型」追加 provider 参数；object 等非 IFormattable 引用类型保持 .ToString()，
                // 避免 object.ToString(IFormatProvider) 不存在导致 CS0117。
                var isFormattableValueType = TypeDetectionHelper.IsValueType(param.Type);
                if (TypeDetectionHelper.IsNullableType(param.Type))
                {
                    // 使用 ?. 运算符，Add() 会跳过 null 值；allowNull 时用 AddAllowNull 保留空键。
                    var formatExpression = !string.IsNullOrEmpty(formatString)
                        ? $"?.ToString(\"{StringEscapeHelper.EscapeString(formatString)}\", global::System.Globalization.CultureInfo.InvariantCulture)"
                        : (isFormattableValueType ? "?.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture)" : "?.ToString()");
                    var method = allowNull ? "AddAllowNull" : "Add";
                    codeBuilder.AppendLine($"{indent}__queryParams.{method}(\"{escapedName}\", {param.Name}{formatExpression});");
                }
                else
                {
                    // 非可空值类型永远有值，allowNull 无意义
                    var formatExpression = !string.IsNullOrEmpty(formatString)
                        ? $".ToString(\"{StringEscapeHelper.EscapeString(formatString)}\", global::System.Globalization.CultureInfo.InvariantCulture)"
                        : (isFormattableValueType ? ".ToString(null, global::System.Globalization.CultureInfo.InvariantCulture)" : ".ToString()");
                    codeBuilder.AppendLine($"{indent}__queryParams.Add(\"{escapedName}\", {param.Name}{formatExpression});");
                }
            }
        }
    }

    private static void GenerateComplexQueryParameter(StringBuilder codeBuilder, ParameterInfo param, string indent, string? prefix, bool treatAsString, bool serializeNull)
    {
        // AOT 改造（Phase 4）：当 TypeSymbol 可用时，编译期枚举属性并生成直接属性访问代码，
        // 消除运行时反射。若 TypeSymbol 不可用（测试/模拟场景），回退到 FlattenObjectToQueryParams。
        // CFG-04：prefix / TreatAsString / SerializeNull 透传。
        if (TryGenerateInlineQueryFlattening(codeBuilder, param, indent, ",",
                includeNullValues: serializeNull,
                useJsonSerialization: !treatAsString,
                urlEncode: true,
                prefix: prefix))
            return;

        // 反射回退路径（仅在 TypeSymbol 不可用时进入；真实编译恒走 AOT 内联路径）：
        // CFG-04 —— 设置 Prefix 时以 '.' 作为层级分隔符，使键满足 "filter.keyword" 契约；
        // 未设置 Prefix 时保持既有 "," 分隔（与旧行为逐字节一致）。
        var hasPrefix = !string.IsNullOrEmpty(prefix);
        var prefixLiteral = hasPrefix
            ? $"\"{StringEscapeHelper.EscapeString(prefix!)}\""
            : "string.Empty";
        var separatorLiteral = hasPrefix ? "\".\"" : "\",\"";
        var includeNullLiteral = serializeNull.ToString().ToLowerInvariant();
        var useJsonLiteral = (!treatAsString).ToString().ToLowerInvariant();
        codeBuilder.AppendLine($"{indent}if ({param.Name} != null)");
        codeBuilder.AppendLine($"{indent}{{");
        codeBuilder.AppendLine($"{indent}    FlattenObjectToQueryParams({param.Name}, {prefixLiteral}, {separatorLiteral}, __queryParams, {includeNullLiteral}, {useJsonLiteral}, true, __rawQueryPairs, 0, _contentSerializer);");
        codeBuilder.AppendLine($"{indent}}}");
    }

    private static void GenerateArrayQueryParameter(StringBuilder codeBuilder, ParameterInfo param, ParameterAttributeInfo attr, string indent)
    {
        var paramName = attr.Arguments.FirstOrDefault()?.ToString() ?? param.Name;
        GenerateArrayQueryUrl(codeBuilder, param, paramName, attr, indent, defaultSeparator: ",");
    }

    /// <summary>
    /// 生成数组查询参数的 URL 代码，支持分隔符模式和重复参数模式。
    /// </summary>
    /// <param name="codeBuilder">代码构建器。</param>
    /// <param name="param">参数信息。</param>
    /// <param name="paramName">查询参数名称。</param>
    /// <param name="attr">参数特性信息。</param>
    /// <param name="indent">缩进字符串。</param>
    /// <param name="defaultSeparator">当 Separator 未显式设置时的默认分隔符。null 表示默认使用重复参数模式（[Query] 的默认行为），非 null 表示使用分隔符模式（[ArrayQuery] 默认 ","）。</param>
    private static void GenerateArrayQueryUrl(StringBuilder codeBuilder, ParameterInfo param, string paramName, ParameterAttributeInfo attr, string indent, string? defaultSeparator)
    {
        // 从构造函数参数或命名参数中解析 Separator
        // ArrayQueryAttribute 构造函数: (string name, string? separator)
        // QueryAttribute 通过命名参数 Separator 设置
        string? separator = null;
        bool separatorExplicitlySet = false;

        // 检查构造函数参数（ArrayQueryAttribute 的第二个参数）
        if (attr.Arguments.Length > 1)
        {
            separator = attr.Arguments[1]?.ToString();
            separatorExplicitlySet = true;
        }

        // 检查命名参数（两种特性都支持）
        if (!separatorExplicitlySet && attr.NamedArguments.TryGetValue("Separator", out var sepVal))
        {
            separator = sepVal as string;
            separatorExplicitlySet = true;
        }

        // [v2.4 §2.3] 消费 CollectionFormat 命名参数
        // CollectionFormat 为非 Multi 时覆盖 Separator（除非 Separator 被显式设置且 CollectionFormat 未显式设置）
        if (attr.NamedArguments.TryGetValue("CollectionFormat", out var cfVal) && cfVal is int cfInt && cfInt != 0)
        {
            // CollectionFormat 被显式设置为非 Multi，推导分隔符
            separator = cfInt switch
            {
                1 => ",",    // Csv
                2 => " ",    // Ssv
                3 => "\t",   // Tsv
                4 => "|",    // Pipes
                _ => null
            };
            separatorExplicitlySet = true;
        }

        // 计算最终生效的分隔符：显式设置则使用设置的值，未设置则使用默认值
        var effectiveSeparator = separatorExplicitlySet ? separator : defaultSeparator;

        codeBuilder.AppendLine($"{indent}if ({param.Name} != null && {param.Name}.Any())");
        codeBuilder.AppendLine($"{indent}{{");

        // 数组元素类型（两种模式共用）
        var elementType = GetArrayElementType(param.Type);

        // [CS0472 修复] 元素为**非可空值类型**时（如 int[]），`__item != null` 恒为 true，
        // 生成代码会触发 CS0472（“int 类型的值永不等于 int? 类型的 null，该表达式的结果始终为 true”）。
        // 该过滤对值类型元素本就没有意义 —— 仅对**引用类型 / 可空值类型**元素保留非空过滤，
        // 运行期语义不变（非可空值类型元素不可能为 null）。
        // 优先用 Roslyn 符号精确判定（自定义 struct[] 亦能识别），符号缺失时回退字符串启发式。
        // 两条发射路径（重复参数 / 分隔符）共用同一过滤片段。
        var whereClause = ArrayElementsMayBeNull(param, elementType)
            ? ".Where(__item => __item != null)"
            : string.Empty;

        if (effectiveSeparator == null)
        {
            // 重复参数模式: query1=val1&query1=val2&query1=val3
            // 尝试使用类型专用的 Add 重载
            var elementOverloadKind = TypeDetectionHelper.GetQueryAddOverloadKind(elementType);

            codeBuilder.AppendLine($"{indent}    foreach (var __item in {param.Name}{whereClause})");
            codeBuilder.AppendLine($"{indent}    {{");

            if (elementOverloadKind == TypeDetectionHelper.QueryAddOverloadKind.WithFormat)
            {
                // 带格式化参数的重载：Add(name, value, null)
                codeBuilder.AppendLine($"{indent}        __queryParams.Add(\"{StringEscapeHelper.EscapeString(paramName)}\", __item, null);");
            }
            else
            {
                // 无专用重载：回退到 ToString()
                // GEN-06（B-2）：数组元素路径同样显式 Invariant（TimeSpan[]、DateTimeOffset[] 等），
                // 避免按 CurrentCulture 生成区域敏感串；非可格式化值类型保持 .ToString()。
                var elementIsFormattable = TypeDetectionHelper.IsValueType(elementType)
                                           && !TypeDetectionHelper.IsNullableType(elementType);
                var itemToString = elementIsFormattable
                    ? "__item.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture)"
                    : "__item.ToString()";
                codeBuilder.AppendLine($"{indent}        __queryParams.Add(\"{StringEscapeHelper.EscapeString(paramName)}\", {itemToString});");
            }

            codeBuilder.AppendLine($"{indent}    }}");
        }
        else
        {
            // 分隔符模式: query1=val1;val2;val3
            codeBuilder.AppendLine($"{indent}    var __joinedValues = string.Join(\"{StringEscapeHelper.EscapeString(effectiveSeparator)}\", {param.Name}{whereClause}.Select(__item => __item.ToString()));");
            codeBuilder.AppendLine($"{indent}    __queryParams.Add(\"{StringEscapeHelper.EscapeString(paramName)}\", __joinedValues);");
        }

        codeBuilder.AppendLine($"{indent}}}");
    }

    private static void GenerateQueryMapParameter(StringBuilder codeBuilder, ParameterInfo param, ParameterAttributeInfo attr, string indent)
    {
        var isDictionaryType = IsDictionaryType(param.Type);

        if (isDictionaryType)
        {
            GenerateDictionaryQueryMapParameter(codeBuilder, param, attr, indent);
        }
        else
        {
            GenerateObjectQueryMapParameter(codeBuilder, param, attr, indent);
        }
    }

    private static void GenerateDictionaryQueryMapParameter(StringBuilder codeBuilder, ParameterInfo param, ParameterAttributeInfo attr, string indent)
    {
        var urlEncode = !(attr.NamedArguments.TryGetValue("UrlEncode", out var urlEnc) && urlEnc is false);

        codeBuilder.AppendLine($"{indent}if ({param.Name} != null)");
        codeBuilder.AppendLine($"{indent}{{");
        codeBuilder.AppendLine($"{indent}    foreach (var __kvp in {param.Name})");
        codeBuilder.AppendLine($"{indent}    {{");
        if (urlEncode)
        {
            codeBuilder.AppendLine($"{indent}        __queryParams.Add(__kvp.Key, __kvp.Value?.ToString());");
        }
        else
        {
            codeBuilder.AppendLine($"{indent}        __rawQueryPairs.Add(__kvp.Key + \"=\" + __kvp.Value?.ToString());");
        }
        codeBuilder.AppendLine($"{indent}    }}");
        codeBuilder.AppendLine($"{indent}}}");
    }

    private static void GenerateObjectQueryMapParameter(StringBuilder codeBuilder, ParameterInfo param, ParameterAttributeInfo attr, string indent)
    {
        var separator = attr.NamedArguments.TryGetValue("PropertySeparator", out var sep) && sep is string s && !string.IsNullOrEmpty(s) ? s : "_";
        var includeNull = attr.NamedArguments.TryGetValue("IncludeNullValues", out var incNull) && incNull is true;
        var useJson = attr.NamedArguments.TryGetValue("SerializationMethod", out var serMethod)
            && serMethod is int enumVal && enumVal != 0;
        var urlEncode = !(attr.NamedArguments.TryGetValue("UrlEncode", out var urlEnc) && urlEnc is false);

        // AOT 改造（Phase 4）：当 TypeSymbol 可用时，编译期枚举属性并生成直接属性访问代码，
        // 消除运行时反射。若 TypeSymbol 不可用（测试/模拟场景），回退到 FlattenObjectToQueryParams。
        if (TryGenerateInlineQueryFlattening(codeBuilder, param, indent, separator, includeNull, useJson, urlEncode))
            return;

        codeBuilder.AppendLine($"{indent}if ({param.Name} != null)");
        codeBuilder.AppendLine($"{indent}{{");
        codeBuilder.AppendLine($"{indent}    FlattenObjectToQueryParams({param.Name}, string.Empty, \"{StringEscapeHelper.EscapeString(separator)}\", __queryParams, {includeNull.ToString().ToLowerInvariant()}, {useJson.ToString().ToLowerInvariant()}, {urlEncode.ToString().ToLowerInvariant()}, __rawQueryPairs, 0, _contentSerializer);");
        codeBuilder.AppendLine($"{indent}}}");
    }

    // ============ AOT 改造（Phase 4）：内联查询参数展平 ============

    /// <summary>
    /// 尝试生成 AOT 安全的内联查询参数展平代码。
    /// 当 <see cref="ParameterInfo.TypeSymbol"/> 可用时，在编译期枚举属性并生成直接属性访问代码，
    /// 消除运行时反射。若 TypeSymbol 不可用（测试/模拟场景），返回 false 以回退到
    /// <c>FlattenObjectToQueryParams</c>。
    /// </summary>
    private static bool TryGenerateInlineQueryFlattening(
        StringBuilder codeBuilder, ParameterInfo param, string indent,
        string separator, bool includeNullValues,
        bool useJsonSerialization, bool urlEncode,
        string? prefix = null)
    {
        if (param.TypeSymbol == null)
            return false;

        var typeSymbol = param.TypeSymbol;

        // 检查类型是否实现 IQueryParameter
        var implementsIQueryParameter = typeSymbol.AllInterfaces
            .Any(i => i.Name == "IQueryParameter" &&
                       i.ContainingNamespace?.ToDisplayString() == "Mud.HttpUtils");

        // CFG-04：顶层前缀以「原始前缀 + '.'」形式向下传递；未设置 Prefix 时为空串（生成结果与旧版逐字节一致）。
        var topPrefix = string.IsNullOrEmpty(prefix) ? string.Empty : prefix + ".";

        codeBuilder.AppendLine($"{indent}if ({param.Name} != null)");
        codeBuilder.AppendLine($"{indent}{{");

        if (implementsIQueryParameter)
        {
            GenerateIQueryParameterInline(codeBuilder, param.Name, indent + "    ",
                topPrefix, separator, includeNullValues, urlEncode);
        }
        else
        {
            // 枚举继承链上的所有公共可读属性（与运行时 GetProperties() 行为一致）
            var properties = CollectPublicProperties(typeSymbol);
            foreach (var prop in properties)
            {
                GeneratePropertyFlatteningInline(codeBuilder, param.Name, prop, indent + "    ",
                    topPrefix, separator, includeNullValues, useJsonSerialization, urlEncode);
            }
        }

        codeBuilder.AppendLine($"{indent}}}");
        return true;
    }

    /// <summary>
    /// 收集类型继承链上的所有公共可读属性（与运行时 GetProperties() 行为一致）。
    /// </summary>
    private static List<IPropertySymbol> CollectPublicProperties(ITypeSymbol typeSymbol)
    {
        var properties = new List<IPropertySymbol>();
        var currentType = typeSymbol;
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
                if (!properties.Any(p => p.Name == prop.Name))
                    properties.Add(prop);
            }
            currentType = currentType.BaseType;
        }
        return properties;
    }

    /// <summary>
    /// 检查类型符号是否实现 IQueryParameter。
    /// </summary>
    private static bool ImplementsIQueryParameter(ITypeSymbol typeSymbol)
    {
        return typeSymbol.AllInterfaces
            .Any(i => i.Name == "IQueryParameter" &&
                       i.ContainingNamespace?.ToDisplayString() == "Mud.HttpUtils");
    }

    /// <summary>
    /// 最大递归深度，防止循环引用导致无限递归。
    /// </summary>
    private const int MaxFlatteningDepth = 5;

    /// <summary>
    /// 为单个属性生成展平代码（AOT 安全路径）。
    /// 当 depth 超过 MaxFlatteningDepth 时回退到 FlattenObjectToQueryParams 反射路径。
    /// </summary>
    private static void GeneratePropertyFlatteningInline(
        StringBuilder codeBuilder, string objName, IPropertySymbol prop, string indent,
        string prefix, string separator, bool includeNullValues,
        bool useJsonSerialization, bool urlEncode, int depth = 0)
    {
        var propName = prop.Name;
        // CFG-04：prefix 为「原始前缀」（已含尾部分隔符）；未设置 Prefix 时 prefix 为空串，key = propName（与旧版一致）。
        var key = prefix + propName;
        var escapedKey = StringEscapeHelper.EscapeString(key);
        var propTypeDisplay = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes));

        // 检查属性类型是否实现 IQueryParameter
        if (ImplementsIQueryParameter(prop.Type))
        {
            GenerateIQueryParameterInline(codeBuilder, objName + "." + propName, indent,
                key + separator, separator, includeNullValues, urlEncode);
            return;
        }

        // 判断是否为简单类型（含枚举）
        var isEnum = prop.Type.TypeKind == TypeKind.Enum;
        var isSimpleType = TypeDetectionHelper.IsSimpleType(propTypeDisplay) || isEnum;

        if (isSimpleType)
        {
            GenerateSimplePropertyFlattening(codeBuilder, objName, propName, prop,
                indent, escapedKey, includeNullValues, useJsonSerialization, urlEncode, propTypeDisplay);
        }
        else
        {
            // [v2.4 §1.2 D-01 修复] 复杂类型：递归内联展平，不再回退到反射路径 FlattenObjectToQueryParams。
            // 当深度未超限且属性类型符号可用时，递归枚举嵌套属性生成直接属性访问代码（AOT 安全）。
            // 仅当深度超限或类型符号不可用时，回退到 FlattenObjectToQueryParams 反射路径。
            if (depth < MaxFlatteningDepth && prop.Type is INamedTypeSymbol nestedTypeSymbol)
            {
                // 递归枚举嵌套类型的公共属性（与运行时 GetProperties() 行为一致）
                var nestedProps = CollectPublicProperties(nestedTypeSymbol);
                if (nestedProps.Count > 0)
                {
                    codeBuilder.AppendLine($"{indent}if ({objName}.{propName} != null)");
                    codeBuilder.AppendLine($"{indent}{{");
                    foreach (var nestedProp in nestedProps)
                    {
                        GeneratePropertyFlatteningInline(codeBuilder, objName + "." + propName, nestedProp,
                            indent + "    ", key + separator, separator, includeNullValues,
                            useJsonSerialization, urlEncode, depth + 1);
                    }
                    codeBuilder.AppendLine($"{indent}}}");
                    return;
                }
            }

            // 深度超限或无公共属性：回退到 FlattenObjectToQueryParams（反射路径，非 AOT 安全）
            codeBuilder.AppendLine($"{indent}if ({objName}.{propName} != null)");
            codeBuilder.AppendLine($"{indent}{{");
            codeBuilder.AppendLine($"{indent}    FlattenObjectToQueryParams({objName}.{propName}, \"{escapedKey}\", \"{StringEscapeHelper.EscapeString(separator)}\", __queryParams, {includeNullValues.ToString().ToLowerInvariant()}, {useJsonSerialization.ToString().ToLowerInvariant()}, {urlEncode.ToString().ToLowerInvariant()}, __rawQueryPairs, 0, _contentSerializer);");
            codeBuilder.AppendLine($"{indent}}}");
        }
    }

    /// <summary>
    /// 为简单类型属性生成展平代码。
    /// </summary>
    /// <remarks>
    /// AOT 修复（JsonAotSourceGeneratorPlan §3.6）：JSON 序列化使用泛型重载
    /// <c>_contentSerializer.Serialize&lt;T&gt;(value)</c>，
    /// 而非非泛型 <c>JsonSerializer.Serialize(object?)</c>。非泛型重载因运行时
    /// <c>Type</c> 分发不被 trim/AOT 分析器视作安全，即使传入含 Context 的 options。
    /// [D-05 设计说明] 此处使用内联 ToString() 格式化（AOT 安全），不调用
    /// <c>IUrlParameterFormatter.Format</c>。IUrlParameterFormatter 的默认实现
    /// <c>DefaultUrlParameterFormatter</c> 使用反射（已标注 [RequiresUnreferencedCode]），
    /// 非 AOT 安全。IUrlParameterFormatter 作为非 AOT 场景的可选运行时覆盖存在，
    /// AOT 路径下通过编译期内联格式化保证零反射。未来可由源生成器在编译期
    /// 生成枚举/特性映射查找表作为 AOT 友好的格式化路径。
    /// </remarks>
    private static void GenerateSimplePropertyFlattening(
        StringBuilder codeBuilder, string objName, string propName, IPropertySymbol prop,
        string indent, string escapedKey, bool includeNullValues,
        bool useJsonSerialization, bool urlEncode, string propTypeDisplay)
    {
        var fullAccess = objName + "." + propName;
        var isValueType = prop.Type.IsValueType;
        var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated
                         || (isValueType && prop.Type.OriginalDefinition?.SpecialType == SpecialType.System_Nullable_T);

        if (isValueType && !isNullable)
        {
            // 非可空值类型：始终有值
            // AOT 安全：使用 _contentSerializer.Serialize<T>(value)
            var valueExpr = useJsonSerialization
                ? $"_contentSerializer.Serialize<{propTypeDisplay}>({fullAccess})"
                : $"{fullAccess}.ToString() ?? \"\"";

            if (urlEncode)
                codeBuilder.AppendLine($"{indent}__queryParams.Add(\"{escapedKey}\", {valueExpr});");
            else
                codeBuilder.AppendLine($"{indent}__rawQueryPairs.Add(System.Uri.EscapeDataString(\"{escapedKey}\") + \"=\" + {valueExpr});");
        }
        else
        {
            // 可空值类型或引用类型：需 null 检查
            var valVar = $"__val_{propName}";
            codeBuilder.AppendLine($"{indent}var {valVar} = {fullAccess};");
            codeBuilder.AppendLine($"{indent}if ({valVar} != null)");
            codeBuilder.AppendLine($"{indent}{{");

            // AOT 安全：使用 _contentSerializer.Serialize<T>(value)
            var valueExpr = useJsonSerialization
                ? $"_contentSerializer.Serialize<{propTypeDisplay}>({valVar})"
                : $"{valVar}.ToString() ?? \"\"";

            if (urlEncode)
                codeBuilder.AppendLine($"{indent}    __queryParams.Add(\"{escapedKey}\", {valueExpr});");
            else
                codeBuilder.AppendLine($"{indent}    __rawQueryPairs.Add(System.Uri.EscapeDataString(\"{escapedKey}\") + \"=\" + {valueExpr});");

            codeBuilder.AppendLine($"{indent}}}");

            if (includeNullValues)
            {
                codeBuilder.AppendLine($"{indent}else");
                codeBuilder.AppendLine($"{indent}{{");
                if (urlEncode)
                    codeBuilder.AppendLine($"{indent}    __queryParams.Add(\"{escapedKey}\", string.Empty);");
                else
                    codeBuilder.AppendLine($"{indent}    __rawQueryPairs.Add(System.Uri.EscapeDataString(\"{escapedKey}\") + \"=\");");
                codeBuilder.AppendLine($"{indent}}}");
            }
        }
    }

    /// <summary>
    /// 生成 IQueryParameter.ToQueryParameters() 的内联遍历代码。
    /// </summary>
    private static void GenerateIQueryParameterInline(
        StringBuilder codeBuilder, string objExpr, string indent,
        string keyPrefix, string separator, bool includeNullValues, bool urlEncode)
    {
        var escapedPrefix = StringEscapeHelper.EscapeString(keyPrefix);

        codeBuilder.AppendLine($"{indent}foreach (var __kvp in {objExpr}.ToQueryParameters())");
        codeBuilder.AppendLine($"{indent}{{");
        // CFG-04：keyPrefix 为「原始前缀」（已含尾部分隔符）；无前缀时直接使用 kvp.Key。
        if (string.IsNullOrEmpty(keyPrefix))
        {
            codeBuilder.AppendLine($"{indent}    var __subKey = __kvp.Key;");
        }
        else
        {
            codeBuilder.AppendLine($"{indent}    var __subKey = \"{escapedPrefix}\" + __kvp.Key;");
        }

        var condition = includeNullValues ? "true" : "!string.IsNullOrEmpty(__kvp.Value)";
        codeBuilder.AppendLine($"{indent}    if ({condition})");
        codeBuilder.AppendLine($"{indent}    {{");
        if (urlEncode)
            codeBuilder.AppendLine($"{indent}        __queryParams.Add(__subKey, __kvp.Value ?? string.Empty);");
        else
            codeBuilder.AppendLine($"{indent}        __rawQueryPairs.Add(System.Uri.EscapeDataString(__subKey) + \"=\" + (__kvp.Value ?? string.Empty));");
        codeBuilder.AppendLine($"{indent}    }}");
        codeBuilder.AppendLine($"{indent}}}");
    }

    private static bool IsDictionaryType(string type)
    {
        return type.StartsWith("System.Collections.Generic.IDictionary", StringComparison.Ordinal) ||
               type.StartsWith("System.Collections.Generic.Dictionary", StringComparison.Ordinal) ||
               type.StartsWith("IDictionary<", StringComparison.Ordinal) ||
               type.StartsWith("Dictionary<", StringComparison.Ordinal);
    }

    /// <summary>
    /// 从数组类型名称中提取元素类型名称。
    /// 例如 "int[]" 返回 "int"，"string[]?" 返回 "string"。
    /// </summary>
    private static string GetArrayElementType(string arrayType)
    {
        // 去除可空后缀 (如 "int[]?" → "int[]")
        var type = arrayType.TrimEnd('?');
        // 去除数组后缀
        if (type.EndsWith("[]", StringComparison.OrdinalIgnoreCase))
            return type.Substring(0, type.Length - 2);
        return type;
    }

    /// <summary>
    /// 判断数组/集合参数的元素是否可能为 null（决定是否发射 <c>.Where(__item => __item != null)</c> 过滤）。
    /// 优先使用 Roslyn 符号精确判定；符号缺失时回退为字符串启发式（已知非空值类型别名/常见 System 值类型）。
    /// </summary>
    private static bool ArrayElementsMayBeNull(ParameterInfo param, string elementType)
    {
        if (param.TypeSymbol is IArrayTypeSymbol arraySymbol)
        {
            var element = arraySymbol.ElementType;
            if (element.IsValueType)
                return element.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            return true;
        }

        // 可空后缀（string[]? 是数组可空，元素可空性未知；int?[] 元素可空）→ 保守过滤
        if (elementType.EndsWith("?", StringComparison.Ordinal))
            return true;

        return !TypeDetectionHelper.IsValueType(elementType);
    }

    private static void GenerateRawQueryStringParameter(StringBuilder codeBuilder, ParameterInfo param, ParameterAttributeInfo attr, string indent)
    {
        var rawQsVar = $"__rawQS_{param.Name}";
        if (param.IsValidated)
        {
            codeBuilder.AppendLine($"{indent}var {rawQsVar} = {param.Name}.TrimStart('?', '&').TrimEnd('&');");
            codeBuilder.AppendLine($"{indent}if (!string.IsNullOrWhiteSpace({rawQsVar}))");
            codeBuilder.AppendLine($"{indent}{{");
            codeBuilder.AppendLine($"{indent}    var __separator = __url.Contains('?') ? \"&\" : \"?\";");
            codeBuilder.AppendLine($"{indent}    __url += __separator + {rawQsVar};");
            codeBuilder.AppendLine($"{indent}}}");
        }
        else
        {
            codeBuilder.AppendLine($"{indent}if (!string.IsNullOrWhiteSpace({param.Name}))");
            codeBuilder.AppendLine($"{indent}{{");
            codeBuilder.AppendLine($"{indent}    var {rawQsVar} = {param.Name}.TrimStart('?', '&').TrimEnd('&');");
            codeBuilder.AppendLine($"{indent}    if (!string.IsNullOrWhiteSpace({rawQsVar}))");
            codeBuilder.AppendLine($"{indent}    {{");
            codeBuilder.AppendLine($"{indent}        var __separator = __url.Contains('?') ? \"&\" : \"?\";");
            codeBuilder.AppendLine($"{indent}        __url += __separator + {rawQsVar};");
            codeBuilder.AppendLine($"{indent}    }}");
            codeBuilder.AppendLine($"{indent}}}");
        }
    }

    private static string GetQueryParameterName(ParameterAttributeInfo attr, string defaultName)
    {
        // CFG-04 / B-3 / GEN-05：优先级 Name（命名参数或位置 0）> AliasAs > 参数名。
        // Name 与首参（[Query("name")]）一致，经 AttributeArgumentReader 统一读取，避免此前 QueryParameterBinder.cs:671-684
        // 与 MethodAnalyzer 接口属性路径的口径分叉。
        var name = AttributeArgumentReader.GetString(attr, AttributeArgumentReader.ResolveNamePosition(attr.Name), "Name");
        if (!string.IsNullOrEmpty(name))
            return name!;

        // GEN-05：参数级 [Query(AliasAs = "keyword")] 生效。
        var alias = AttributeArgumentReader.GetString(attr, null, "AliasAs");
        if (!string.IsNullOrEmpty(alias))
            return alias!;

        return defaultName;
    }

    private static string? GetFormatString(ParameterAttributeInfo attr)
    {
        // GEN-04：[Query("bth", "yyyy-MM-dd")] 的位置 format（ResolveFormatPosition=1）需生效，
        // 此前只读 NamedArguments["Format"] 导致位置 format 被丢弃。
        return AttributeArgumentReader.GetString(attr, AttributeArgumentReader.ResolveFormatPosition(attr.Name), "Format");
    }
}
