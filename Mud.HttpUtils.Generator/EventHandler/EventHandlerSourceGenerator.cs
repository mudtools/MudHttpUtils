// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Immutable;

namespace Mud.HttpUtils;

/// <summary>
/// 事件处理器源代码生成器，用于为带有EventHandler特性的类生成对应的事件处理器抽象类。
/// </summary>
[Generator(LanguageNames.CSharp)]
internal class EventHandlerSourceGenerator : TransitiveCodeGenerator
{
    private const string EventHandlerAttributeName = "GenerateEventHandlerAttribute";

    /// <inheritdoc/>
    protected override Collection<string> GetFileUsingNameSpaces()
    {
        return
        [
            "System",
            "System.Threading.Tasks",
            "Microsoft.Extensions.Logging"
        ];
    }

    /// <inheritdoc/>
    public override void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 查找标记了[EventHandler]的类声明
        var eventHandlerClasses = GetClassDeclarationProvider<ClassDeclarationSyntax>(context, [EventHandlerAttributeName]);

        // 获取编译信息和分析器配置选项
        var compilationWithOptions = context.CompilationProvider
            .Combine(context.AnalyzerConfigOptionsProvider);

        // 组合所有需要的数据：编译信息、类声明、配置选项
        var completeDataProvider = compilationWithOptions.Combine(eventHandlerClasses);

        // 注册源代码生成器
        context.RegisterSourceOutput(completeDataProvider,
            (ctx, provider) => ExecuteGenerator(
                compilation: provider.Left.Left,
                eventHandlerClasses: provider.Right.Where(c => c != null).ToImmutableArray()!,
                context: ctx));
    }

    /// <summary>
    /// 执行源代码生成逻辑
    /// </summary>
    /// <param name="compilation">编译信息</param>
    /// <param name="eventHandlerClasses">事件处理器类声明数组</param>
    /// <param name="context">源代码生成上下文</param>
    private void ExecuteGenerator(
        Compilation compilation,
        ImmutableArray<ClassDeclarationSyntax> eventHandlerClasses,
        SourceProductionContext context)
    {
        if (eventHandlerClasses.IsDefaultOrEmpty)
            return;

        foreach (var eventClass in eventHandlerClasses)
        {
            if (eventClass == null)
                continue;

            try
            {
                var semanticModel = HttpInvokeBaseSourceGenerator.GetOrCreateSemanticModel(compilation, eventClass.SyntaxTree);

                var classSymbol = semanticModel.GetDeclaredSymbol(eventClass);

                if (classSymbol == null)
                    continue;

                // 获取EventHandler特性数据，使用AttributeDataHelper的已有功能
                var eventHandlerAttribute = AttributeDataHelper.GetAttributeDataFromSymbol(classSymbol, [EventHandlerAttributeName]);

                if (eventHandlerAttribute == null)
                    continue;

                // 生成事件处理器代码
                var generatedCode = GenerateEventHandlerClass(eventClass, classSymbol, eventHandlerAttribute, context);

                if (!string.IsNullOrEmpty(generatedCode))
                {
                    var fileName = GenerateUniqueFileName(eventClass, classSymbol, eventHandlerAttribute);
                    context.AddSource(fileName, generatedCode);
                }
            }
            catch (Exception ex)
            {
                // 报告生成错误
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.EventHandlerGenerationError,
                    eventClass.GetLocation(),
                    eventClass.Identifier.Text,
                    ex.Message));
            }
        }
    }

    /// <summary>
    /// 生成事件处理器类代码
    /// </summary>
    /// <param name="eventClass">事件结果类声明</param>
    /// <param name="classSymbol">类符号</param>
    /// <param name="eventHandlerAttribute">EventHandler特性</param>
    /// <param name="context">源代码生成上下文</param>
    /// <returns>生成的事件处理器类代码</returns>
    private string GenerateEventHandlerClass(
        ClassDeclarationSyntax eventClass,
        INamedTypeSymbol classSymbol,
        AttributeData eventHandlerAttribute,
        SourceProductionContext context)
    {
        // 获取XML文档注释
        var xmlDocumentation = GetXmlDocumentation(eventClass);

        // 解析特性参数，使用AttributeDataHelper的已有功能
        var handlerNamespace = GetAttributeParameter(eventHandlerAttribute, "HandlerNamespace", "");
        var handlerClassName = GetAttributeParameter(eventHandlerAttribute, "HandlerClassName", "");
        var eventType = AttributeDataHelper.GetStringValueFromAttribute(eventHandlerAttribute, "EventType", "")
                   ?? AttributeDataHelper.GetStringValueFromAttributeConstructor(eventHandlerAttribute, "EventType")
                   ?? "";
        var inheritedFrom = GetAttributeParameter(eventHandlerAttribute, "InheritedFrom", "IdempotentFeishuEventHandler");
        var constructorParams = GetAttributeParameter(eventHandlerAttribute, "ConstructorParameters", "IFeishuEventDeduplicator businessDeduplicator, ILogger logger, IAppKeyAccessor? appKeyAccessor = null");
        var constructorBaseCall = GetAttributeParameter(eventHandlerAttribute, "ConstructorBaseCall", "businessDeduplicator, logger, appKeyAccessor");
        var headerType = GetAttributeParameter(eventHandlerAttribute, "HeaderType", "");

        // 验证基类名合法性
        if (!ValidateBaseClassName(inheritedFrom, context, eventClass.GetLocation()))
        {
            return string.Empty;
        }

        // 获取生成的类名
        var generatedClassName = !string.IsNullOrEmpty(handlerClassName) ? handlerClassName : GenerateDefaultHandlerClassName(eventClass.Identifier.Text);

        // 获取命名空间
        var targetNamespace = !string.IsNullOrEmpty(handlerNamespace) ? handlerNamespace : GetDefaultNamespace(classSymbol);

        // 构建生成的代码
        var sb = new StringBuilder();

        // 文件头部注释
        GenerateFileHeader(sb);

        // 添加自定义命名空间引用（如果需要的话）
        if (!string.IsNullOrEmpty(inheritedFrom))
        {
            var lastDotIndex = inheritedFrom.LastIndexOf('.');
            if (lastDotIndex > 0)
            {
                var inheritedNamespace = inheritedFrom.Substring(0, lastDotIndex);
                if (!GetFileUsingNameSpaces().Contains(inheritedNamespace))
                {
                    sb.AppendLine($"using {inheritedNamespace};");
                }
            }
        }

        // 添加原始类所在的命名空间引用（如果与目标命名空间不同）
        var originalNamespace = classSymbol.ContainingNamespace?.ToString();
        if (!string.IsNullOrEmpty(originalNamespace) && originalNamespace != targetNamespace)
        {
            sb.AppendLine($"using {originalNamespace};");
        }

        // 命名空间声明
        sb.AppendLine();
        sb.AppendLine($"namespace {targetNamespace}");
        sb.AppendLine("{");

        // XML文档注释（如果原始类有的话）
        if (!string.IsNullOrEmpty(xmlDocumentation))
        {
            sb.Append(xmlDocumentation.IndentLines(1));
        }

        // 类声明
        sb.AppendLine($"    {GeneratedCodeConsts.CompilerGeneratedAttribute}");
        sb.AppendLine($"    {GeneratedCodeConsts.HttpGeneratedCodeAttribute}");

        var baseClassGeneric = string.IsNullOrEmpty(headerType)
            ? $"{inheritedFrom}<{eventClass.Identifier.Text}>"
            : $"{inheritedFrom}<{eventClass.Identifier.Text}, {headerType}>";

        sb.AppendLine($"    public abstract partial class {generatedClassName}");
        sb.AppendLine($"        : {baseClassGeneric}");
        sb.AppendLine("    {");

        // 构造函数
        if (!string.IsNullOrEmpty(constructorParams))
        {
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 默认构造函数");
            sb.AppendLine("        /// </summary>");

            var paramParts = constructorParams.Split(',');
            foreach (var param in paramParts)
            {
                var trimmedParam = param.Trim();
                // 先去除默认值部分（如果存在 = 符号）
                var equalIndex = trimmedParam.IndexOf('=');
                if (equalIndex >= 0)
                {
                    trimmedParam = trimmedParam.Substring(0, equalIndex).Trim();
                }
                // 提取参数名（最后一个单词）
                var paramName = trimmedParam.Contains(' ') ? trimmedParam.Substring(trimmedParam.LastIndexOf(' ') + 1) : trimmedParam;
                sb.AppendLine($"        /// <param name=\"{paramName}\">参数</param>");
            }

            sb.AppendLine($"        {GeneratedCodeConsts.HttpGeneratedCodeAttribute}");
            sb.AppendLine($"        public {generatedClassName}({constructorParams.Trim()})");
            if (!string.IsNullOrEmpty(constructorBaseCall))
            {
                sb.AppendLine($"            : base({constructorBaseCall.Trim()})");
            }
            sb.AppendLine("        {");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // SupportedEventType属性
        if (!string.IsNullOrEmpty(eventType))
        {
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 支持的事件类型");
            sb.AppendLine("        /// </summary>");
            var eventTypeValue = NormalizeEventType(eventType);
            sb.AppendLine($"        {GeneratedCodeConsts.HttpGeneratedCodeAttribute}");
            sb.AppendLine($"        public override string SupportedEventType => {eventTypeValue};");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// 获取字符串类型的特性参数值
    /// </summary>
    /// <param name="attribute">特性数据</param>
    /// <param name="parameterName">参数名</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>参数值</returns>
    private string GetAttributeParameter(AttributeData attribute, string parameterName, string defaultValue = "")
    {
        // 优先使用AttributeDataHelper的命名参数方法
        var namedValue = AttributeDataHelper.GetStringValueFromAttribute(attribute, parameterName, defaultValue);
        if (!string.IsNullOrEmpty(namedValue))
        {
            return namedValue;
        }

        // 如果命名参数中没有找到，检查构造函数参数
        return AttributeDataHelper.GetStringValueFromAttributeConstructor(attribute, parameterName) ?? defaultValue;
    }

    /// <summary>
    /// 规范化事件类型字符串，确保输出为合法的字符串字面量
    /// </summary>
    /// <param name="eventType">原始事件类型值</param>
    /// <returns>规范化后的字符串字面量</returns>
    private string NormalizeEventType(string eventType)
    {
        return StringEscapeHelper.NormalizeEventType(eventType);
    }

    /// <summary>
    /// 生成默认的事件处理器类名
    /// </summary>
    /// <param name="originalClassName">原始类名</param>
    /// <returns>生成的事件处理器类名</returns>
    private string GenerateDefaultHandlerClassName(string originalClassName)
    {
        if (string.IsNullOrEmpty(originalClassName))
            return "DefaultEventHandler";

        // 如果类名以"Result"结尾，移除"Result"并添加"EventHandler"
        if (originalClassName.EndsWith("Result", StringComparison.Ordinal))
        {
            return originalClassName.Substring(0, originalClassName.Length - 6) + "EventHandler";
        }

        // 否则直接添加"EventHandler"后缀
        return originalClassName + "EventHandler";
    }

    /// <summary>
    /// 获取默认命名空间
    /// </summary>
    /// <param name="classSymbol">类符号</param>
    /// <returns>默认命名空间</returns>
    private string GetDefaultNamespace(INamedTypeSymbol classSymbol)
    {
        return classSymbol.ContainingNamespace?.ToString() ?? "Generated.EventHandlers";
    }

    /// <summary>
    /// 获取生成的事件处理器类名（用于文件名）
    /// </summary>
    /// <param name="eventClass">事件结果类声明</param>
    /// <param name="eventHandlerAttribute">EventHandler特性</param>
    /// <returns>生成的类名</returns>
    private string GetGeneratedClassName(
        ClassDeclarationSyntax eventClass,
        AttributeData eventHandlerAttribute)
    {
        var handlerClassName = GetAttributeParameter(eventHandlerAttribute, "HandlerClassName", "");

        if (!string.IsNullOrEmpty(handlerClassName))
        {
            // 处理nameof表达式，例如 nameof(EmployeeTypeEnumUpdateEventHandler)
            if (handlerClassName.StartsWith("nameof(", StringComparison.Ordinal) && handlerClassName.EndsWith(")", StringComparison.Ordinal))
            {
                // 提取nameof表达式中的内容
                var nameOfContent = handlerClassName.Substring(7, handlerClassName.Length - 8).Trim();
                return nameOfContent;
            }
            return handlerClassName;
        }

        return GenerateDefaultHandlerClassName(eventClass.Identifier.Text);
    }

    /// <summary>
    /// 验证基类名的合法性
    /// </summary>
    /// <param name="baseClassName">基类名</param>
    /// <param name="context">源代码生成上下文</param>
    /// <param name="location">诊断位置</param>
    /// <returns>是否合法</returns>
    private bool ValidateBaseClassName(string baseClassName, SourceProductionContext context, Location location)
    {
        if (string.IsNullOrWhiteSpace(baseClassName))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.EventHandlerGenerationError,
                location,
                baseClassName,
                "Base class name cannot be empty or whitespace"));
            return false;
        }

        // 检查是否包含C#关键字
        var keywords = new[] { "class", "interface", "struct", "enum", "delegate", "abstract", "sealed", "static" };
        var parts = baseClassName.Split(new[] { '.', '<', '>', ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (keywords.Contains(part, StringComparer.OrdinalIgnoreCase))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.EventHandlerGenerationError,
                    location,
                    baseClassName,
                    $"Base class name contains C# keyword '{part}'"));
                return false;
            }
        }

        // 检查泛型语法
        if (baseClassName.Contains("<") || baseClassName.Contains(">"))
        {
            var openCount = baseClassName.Count(c => c == '<');
            var closeCount = baseClassName.Count(c => c == '>');
            if (openCount != closeCount)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.EventHandlerGenerationError,
                    location,
                    baseClassName,
                    "Invalid generic type syntax: unmatched angle brackets"));
                return false;
            }
        }

        // 检查非法字符
        var invalidChars = new[] { '!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '-', '=', '+', '[', ']', '{', '}', '|', '\\', ';', ':', '"', '\'', '<', '>', '?', '/', ' ' };
        foreach (var part in parts)
        {
            if (part.IndexOfAny(invalidChars) >= 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.EventHandlerGenerationError,
                    location,
                    baseClassName,
                    $"Base class name contains invalid characters"));
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 生成唯一的文件名（包含命名空间信息）
    /// </summary>
    /// <param name="eventClass">事件类声明</param>
    /// <param name="classSymbol">类符号</param>
    /// <param name="eventHandlerAttribute">特性数据</param>
    /// <returns>唯一的文件名</returns>
    private string GenerateUniqueFileName(
        ClassDeclarationSyntax eventClass,
        INamedTypeSymbol classSymbol,
        AttributeData eventHandlerAttribute)
    {
        //var handlerNamespace = GetAttributeParameter(eventHandlerAttribute, "HandlerNamespace", "");
        //var targetNamespace = !string.IsNullOrEmpty(handlerNamespace)
        //    ? handlerNamespace
        //    : GetDefaultNamespace(classSymbol);

        var className = GetGeneratedClassName(eventClass, eventHandlerAttribute);

        // 使用命名空间和类名组合，替换点为下划线
        //var namespacePart = targetNamespace.Replace(".", "_");
        return $"{className}.g.cs";
    }

}