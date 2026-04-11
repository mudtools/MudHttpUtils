// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.CodeGenerator;

internal static class ClassHierarchyAnalyzer
{
    /// <summary>
    /// 分析类的完整继承层次结构
    /// </summary>
    public static IReadOnlyList<ClassHierarchyInfo> AnalyzeClassHierarchy(
        ClassDeclarationSyntax classDeclaration,
        Compilation compilation)
    {
        if (classDeclaration == null || compilation == null)
            return [];
        var hierarchy = new List<ClassHierarchyInfo>();

        // 获取语义模型
        var semanticModel = compilation.GetSemanticModel(classDeclaration.SyntaxTree);

        // 获取当前类的符号
        var currentClassSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);

        if (currentClassSymbol == null)
            return hierarchy;

        // 添加当前类信息
        hierarchy.Add(CreateClassInfo(currentClassSymbol, classDeclaration));

        // 递归分析基类
        AnalyzeBaseTypes(currentClassSymbol, hierarchy, compilation);

        return hierarchy;
    }

    /// <summary>
    /// 获取基类中的所有公共属性的语法节点（包括继承链中的所有基类）
    /// </summary>
    public static IReadOnlyList<PropertyDeclarationSyntax> GetBaseClassPublicPropertyDeclarations(
        ClassDeclarationSyntax classDeclaration,
        Compilation compilation,
        bool includeCurrentClass = false)
    {
        if (classDeclaration == null || compilation == null)
            return [];
        var propertyDeclarations = new List<PropertyDeclarationSyntax>();

        // 获取语义模型
        var semanticModel = compilation.GetSemanticModel(classDeclaration.SyntaxTree);

        // 获取当前类的符号
        var currentClassSymbol = semanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

        if (currentClassSymbol == null)
            return propertyDeclarations;

        // 如果需要包含当前类的属性
        if (includeCurrentClass)
        {
            AddPublicPropertyDeclarationsFromType(currentClassSymbol, propertyDeclarations, currentClassSymbol);
        }

        // 递归获取基类的公共属性语法节点，传递当前类符号用于类型解析
        CollectBaseClassPropertyDeclarations(currentClassSymbol.BaseType, propertyDeclarations, compilation, currentClassSymbol);

        return propertyDeclarations;
    }

    /// <summary>
    /// 获取基类公共属性语法节点的分组信息（按声明类型分组）
    /// </summary>
    public static IReadOnlyDictionary<string, List<PropertyDeclarationSyntax>> GetBaseClassPublicPropertyDeclarationsGrouped(
        ClassDeclarationSyntax classDeclaration,
        Compilation compilation,
        bool includeCurrentClass = false)
    {
        if (classDeclaration == null || compilation == null)
            return new Dictionary<string, List<PropertyDeclarationSyntax>>();
        var properties = GetBaseClassPublicPropertyDeclarations(classDeclaration, compilation, includeCurrentClass);

        // 需要创建一个字典来按声明类型分组
        var groupedProperties = new Dictionary<string, List<PropertyDeclarationSyntax>>();

        foreach (var property in properties)
        {
            // 获取属性的声明类型名称 - 使用语义模型获取准确的类型信息
            var semanticModel = compilation.GetSemanticModel(property.SyntaxTree);
            var propertySymbol = semanticModel.GetDeclaredSymbol(property);

            if (propertySymbol != null)
            {
                var declaringTypeName = propertySymbol.ContainingType.ToDisplayString();
                if (!groupedProperties.ContainsKey(declaringTypeName))
                {
                    groupedProperties[declaringTypeName] = new List<PropertyDeclarationSyntax>();
                }
                groupedProperties[declaringTypeName].Add(property);
            }
            else
            {
                // 回退方案：使用语法分析
                var parentClass = property.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                if (parentClass != null)
                {
                    var className = parentClass.Identifier.ValueText;
                    if (!groupedProperties.ContainsKey(className))
                    {
                        groupedProperties[className] = new List<PropertyDeclarationSyntax>();
                    }
                    groupedProperties[className].Add(property);
                }
            }
        }

        return groupedProperties;
    }

    /// <summary>
    /// 检查基类中是否存在特定名称的属性语法节点
    /// </summary>
    public static bool BaseClassHasPropertyDeclaration(
        ClassDeclarationSyntax classDeclaration,
        Compilation compilation,
        string propertyName,
        bool includeCurrentClass = false)
    {
        if (classDeclaration == null || compilation == null)
            return false;
        var properties = GetBaseClassPublicPropertyDeclarations(classDeclaration, compilation, includeCurrentClass);
        return properties.Any(p => p.Identifier.ValueText == propertyName);
    }

    /// <summary>
    /// 获取基类中特定类型的属性语法节点
    /// </summary>
    public static IReadOnlyList<PropertyDeclarationSyntax> GetBaseClassPropertyDeclarationsByType(
        ClassDeclarationSyntax classDeclaration,
        Compilation compilation,
        string typeName,
        bool includeCurrentClass = false)
    {
        if (classDeclaration == null || compilation == null)
            return [];
        var properties = GetBaseClassPublicPropertyDeclarations(classDeclaration, compilation, includeCurrentClass);
        return properties.Where(p =>
            p.Type is IdentifierNameSyntax identifierName &&
            identifierName.Identifier.ValueText == typeName).ToList();
    }

    /// <summary>
    /// 获取继承深度（从当前类到Object的层级数）
    /// </summary>
    public static int GetInheritanceDepth(
        ClassDeclarationSyntax classDeclaration,
        Compilation compilation)
    {
        if (classDeclaration == null || compilation == null)
            return 0;
        var hierarchy = AnalyzeClassHierarchy(classDeclaration, compilation);
        return hierarchy.Count;
    }

    /// <summary>
    /// 检查类是否继承自特定类型
    /// </summary>
    public static bool InheritsFrom(
        ClassDeclarationSyntax classDeclaration,
        Compilation compilation,
        string baseTypeFullName)
    {
        if (classDeclaration == null || compilation == null)
            return false;
        var hierarchy = AnalyzeClassHierarchy(classDeclaration, compilation);
        return hierarchy.Any(info => info.FullName == baseTypeFullName);
    }

    /// <summary>
    /// 获取类的所有实现的接口（包括继承的）
    /// </summary>
    public static IReadOnlyList<string> GetAllImplementedInterfaces(
        ClassDeclarationSyntax classDeclaration,
        Compilation compilation)
    {
        if (classDeclaration == null || compilation == null)
            return [];
        var semanticModel = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

        if (classSymbol == null)
            return [];

        // 获取所有接口（包括基类实现的）
        return classSymbol.AllInterfaces
            .Select(i => i.ToDisplayString())
            .Distinct()
            .ToList();
    }

    #region Private Methods

    private static void AnalyzeBaseTypes(
        INamedTypeSymbol currentSymbol,
        List<ClassHierarchyInfo> hierarchy,
        Compilation compilation)
    {
        // 获取直接基类
        var baseType = currentSymbol.BaseType;

        // 基类为 null 或 System.Object 时停止递归
        if (baseType == null || baseType.SpecialType == SpecialType.System_Object)
            return;

        // 创建基类信息
        var baseClassInfo = CreateClassInfo(baseType);
        hierarchy.Add(baseClassInfo);

        // 递归分析上一级基类
        AnalyzeBaseTypes(baseType, hierarchy, compilation);
    }


    private static void CollectBaseClassPropertyDeclarations(
        INamedTypeSymbol baseType,
        List<PropertyDeclarationSyntax> propertyDeclarations,
        Compilation compilation,
        INamedTypeSymbol contextClassSymbol)
    {
        // 基类为 null 或 System.Object 时停止递归
        if (baseType == null || baseType.SpecialType == SpecialType.System_Object)
            return;

        // 添加当前基类的公共属性语法节点，传递上下文类符号用于类型解析
        AddPublicPropertyDeclarationsFromType(baseType, propertyDeclarations, contextClassSymbol);

        // 递归处理上一级基类
        CollectBaseClassPropertyDeclarations(baseType.BaseType, propertyDeclarations, compilation, contextClassSymbol);
    }

    private static void AddPublicPropertyDeclarationsFromType(
        INamedTypeSymbol typeSymbol,
        List<PropertyDeclarationSyntax> propertyDeclarations,
        INamedTypeSymbol contextClassSymbol)
    {
        var publicProperties = TypeSymbolHelper.GetPublicProperties(typeSymbol).Where(property => !property.IsStatic);

        // 使用HashSet来跟踪已添加的属性名，提高查找性能
        var existingPropertyNames = new HashSet<string>(propertyDeclarations.Select(p => p.Identifier.ValueText));

        foreach (var propertySymbol in publicProperties)
        {
            // 如果属性名已存在，跳过处理
            if (existingPropertyNames.Contains(propertySymbol.Name))
                continue;

            // 获取属性的语法节点
            var propertySyntax = GetPropertyDeclarationSyntax(propertySymbol);
            if (propertySyntax == null)
                continue;

            // 对于泛型类型参数，创建一个修改后的语法节点来反映具体类型
            var resolvedPropertySyntax = ResolvePropertyTypeInSyntax(propertySyntax, propertySymbol, contextClassSymbol);

            // 创建新的属性声明语法节点，确保它属于当前语法树
            var newPropertySyntax = CreateNewPropertyDeclarationSyntax(resolvedPropertySyntax);

            // 添加属性并更新已存在属性名集合
            propertyDeclarations.Add(newPropertySyntax);
            existingPropertyNames.Add(newPropertySyntax.Identifier.ValueText);
        }
    }

    /// <summary>
    /// 创建新的属性声明语法节点，确保它属于当前语法树
    /// </summary>
    private static PropertyDeclarationSyntax CreateNewPropertyDeclarationSyntax(PropertyDeclarationSyntax original)
    {
        // 先创建基本属性结构
        var newProperty = SyntaxFactory.PropertyDeclaration(
                original.Type,
                original.Identifier)
            .WithModifiers(original.Modifiers)
            .WithAccessorList(original.AccessorList)
            .WithInitializer(original.Initializer);

        // 使用 NormalizeWhitespace 创建新的语法节点
        newProperty = newProperty.NormalizeWhitespace()
            .WithLeadingTrivia(original.GetLeadingTrivia())
            .WithTrailingTrivia(original.GetTrailingTrivia());

        // 重新创建特性列表并添加到属性中
        var newAttributeLists = CreateNewAttributeLists(original.AttributeLists);
        newProperty = newProperty.WithAttributeLists(newAttributeLists);

        return newProperty;
    }

    /// <summary>
    /// 创建新的特性列表，确保所有节点都属于当前语法树
    /// </summary>
    private static SyntaxList<AttributeListSyntax> CreateNewAttributeLists(SyntaxList<AttributeListSyntax> originalAttributeLists)
    {
        if (!originalAttributeLists.Any())
            return originalAttributeLists;

        var newAttributeLists = new List<AttributeListSyntax>();

        foreach (var attributeList in originalAttributeLists)
        {
            var newAttributes = new List<AttributeSyntax>();

            foreach (var attribute in attributeList.Attributes)
            {
                // 重新创建特性节点
                var newAttribute = SyntaxFactory.Attribute(attribute.Name);

                // 复制特性参数列表
                if (attribute.ArgumentList != null)
                {
                    var newArguments = new List<AttributeArgumentSyntax>();

                    foreach (var argument in attribute.ArgumentList.Arguments)
                    {
                        var newArgument = SyntaxFactory.AttributeArgument(argument.Expression)
                            .WithNameEquals(argument.NameEquals)
                            .WithNameColon(argument.NameColon);

                        newArguments.Add(newArgument);
                    }

                    newAttribute = newAttribute.WithArgumentList(
                        SyntaxFactory.AttributeArgumentList(
                            SyntaxFactory.SeparatedList(newArguments)));
                }

                newAttributes.Add(newAttribute);
            }

            var newAttributeList = SyntaxFactory.AttributeList(
                SyntaxFactory.SeparatedList(newAttributes))
                .WithTarget(attributeList.Target)
                .WithLeadingTrivia(attributeList.GetLeadingTrivia())
                .WithTrailingTrivia(attributeList.GetTrailingTrivia());

            newAttributeLists.Add(newAttributeList);
        }

        return SyntaxFactory.List(newAttributeLists);
    }

    /// <summary>
    /// 解析属性语法节点中的类型，将泛型类型参数替换为具体类型
    /// </summary>
    private static PropertyDeclarationSyntax ResolvePropertyTypeInSyntax(
        PropertyDeclarationSyntax propertySyntax,
        IPropertySymbol propertySymbol,
        INamedTypeSymbol contextClassSymbol)
    {
        // 获取属性声明所在类型的原始定义（泛型定义）
        var declaringType = propertySymbol.ContainingType;
        var originalDeclaringType = declaringType.OriginalDefinition;

        // 如果声明类型是泛型类型，我们需要检查是否需要解析类型
        if (!originalDeclaringType.IsGenericType)
        {
            return propertySyntax;
        }

        // 解析具体的类型名称
        var resolvedTypeName = ResolvePropertyType(propertySymbol, contextClassSymbol);
        if (resolvedTypeName == null || string.IsNullOrWhiteSpace(resolvedTypeName))
        {
            return propertySyntax;
        }

        // 创建新的类型语法节点
        var newTypeSyntax = SyntaxFactory.ParseTypeName(resolvedTypeName)
            .WithLeadingTrivia(propertySyntax.Type.GetLeadingTrivia())
            .WithTrailingTrivia(propertySyntax.Type.GetTrailingTrivia());

        // 替换类型节点
        return propertySyntax.WithType(newTypeSyntax);
    }

    /// <summary>
    /// 从属性符号获取属性声明语法节点
    /// </summary>
    private static PropertyDeclarationSyntax GetPropertyDeclarationSyntax(IPropertySymbol propertySymbol)
    {
        // 获取属性的语法引用
        var syntaxReferences = propertySymbol.DeclaringSyntaxReferences;
        if (syntaxReferences.Length == 0)
        {
            // 如果没有语法引用，手动创建一个属性声明
            var propertyDeclaration = SyntaxFactory.PropertyDeclaration(
                SyntaxFactory.ParseTypeName(TypeSymbolHelper.GetTypeFullName(propertySymbol.Type)),
                propertySymbol.Name)
                .WithModifiers(SyntaxFactory.TokenList(
                    propertySymbol.IsReadOnly ? SyntaxFactory.Token(SyntaxKind.PublicKeyword) : SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithAccessorList(
                    SyntaxFactory.AccessorList(
                        SyntaxFactory.List(new[] {
                            SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                            propertySymbol.IsReadOnly ? null : SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                        }.Where(x => x != null))
                    )
                );

            // 添加特性（如果有的话）
            var attributes = new List<AttributeListSyntax>();
            foreach (var attributeData in propertySymbol.GetAttributes())
            {
                var attributeArguments = new List<AttributeArgumentSyntax>();
                foreach (var namedArgument in attributeData.NamedArguments)
                {
                    var argumentExpression = SyntaxFactory.ParseExpression(namedArgument.Value.ToCSharpString());
                    var attributeArgument = SyntaxFactory.AttributeArgument(argumentExpression)
                        .WithNameEquals(SyntaxFactory.NameEquals(namedArgument.Key));
                    attributeArguments.Add(attributeArgument);
                }

                var attribute = SyntaxFactory.Attribute(SyntaxFactory.ParseName(attributeData.AttributeClass.Name));
                if (attributeArguments.Any())
                {
                    attribute = attribute.WithArgumentList(SyntaxFactory.AttributeArgumentList(SyntaxFactory.SeparatedList(attributeArguments)));
                }

                var attributeList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute));
                attributes.Add(attributeList);
            }

            if (attributes.Any())
            {
                propertyDeclaration = propertyDeclaration.WithAttributeLists(SyntaxFactory.List(attributes));
            }

            return propertyDeclaration;
        }

        // 获取第一个语法节点（对于部分类可能有多个）
        var syntaxNode = syntaxReferences[0].GetSyntax();

        // 返回属性声明语法节点
        return syntaxNode as PropertyDeclarationSyntax;
    }

    private static string ResolvePropertyType(IPropertySymbol propertySymbol, INamedTypeSymbol contextClassSymbol)
    {
        // 如果属性类型是类型参数（泛型），尝试解析具体类型
        if (propertySymbol.Type is ITypeParameterSymbol typeParameter)
        {
            // 在继承链中查找类型参数的具体化
            return ResolveTypeParameter(typeParameter, contextClassSymbol) ?? TypeSymbolHelper.GetTypeFullName(propertySymbol.Type);
        }

        // 如果属性类型是构造的泛型类型，递归解析类型参数
        if (propertySymbol.Type is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            return ResolveConstructedGenericType(namedType, contextClassSymbol);
        }

        // 普通类型直接返回
        return TypeSymbolHelper.GetTypeFullName(propertySymbol.Type);
    }

    private static string ResolveTypeParameter(ITypeParameterSymbol typeParameter, INamedTypeSymbol contextClassSymbol)
    {
        var currentType = contextClassSymbol;

        while (currentType != null && currentType.SpecialType != SpecialType.System_Object)
        {
            // 检查当前类型是否实现了包含该类型参数的泛型基类
            var resolvedType = ResolveTypeParameterFromBaseChain(typeParameter, currentType);
            if (resolvedType != null)
            {
                return resolvedType;
            }

            currentType = currentType.BaseType;
        }

        return null;
    }

    private static string ResolveTypeParameterFromBaseChain(ITypeParameterSymbol typeParameter, INamedTypeSymbol currentType)
    {
        if (currentType == null || currentType.SpecialType == SpecialType.System_Object)
            return null;

        // 检查当前类型的所有基类
        var baseType = currentType.BaseType;
        while (baseType != null && baseType.SpecialType != SpecialType.System_Object)
        {
            // 如果基类是泛型类型，检查类型参数映射
            if (baseType.IsGenericType)
            {
                var originalDefinition = baseType.OriginalDefinition;
                var typeParameters = originalDefinition.TypeParameters;

                // 在基类的类型参数中查找匹配
                for (int i = 0; i < typeParameters.Length; i++)
                {
                    if (typeParameters[i].Name == typeParameter.Name)
                    {
                        return baseType.TypeArguments[i].ToDisplayString();
                    }
                }

                // 递归检查基类的类型参数中是否包含我们要找的类型参数
                for (int i = 0; i < baseType.TypeArguments.Length; i++)
                {
                    if (baseType.TypeArguments[i] is ITypeParameterSymbol baseTypeParam)
                    {
                        // 如果基类类型参数本身也是类型参数，需要继续解析
                        if (baseTypeParam.Name == typeParameter.Name)
                        {
                            // 在当前类型的类型参数中查找对应的具体类型
                            if (currentType.IsGenericType && i < currentType.TypeArguments.Length)
                            {
                                return currentType.TypeArguments[i].ToDisplayString();
                            }
                        }
                    }
                }
            }

            baseType = baseType.BaseType;
        }

        return null;
    }

    private static string ResolveConstructedGenericType(INamedTypeSymbol constructedType, INamedTypeSymbol contextClassSymbol)
    {
        var typeName = constructedType.OriginalDefinition.ToDisplayString();
        var typeArguments = constructedType.TypeArguments.Select(arg =>
        {
            if (arg is ITypeParameterSymbol typeParam)
            {
                return ResolveTypeParameter(typeParam, contextClassSymbol) ?? arg.ToDisplayString();
            }
            else if (arg is INamedTypeSymbol namedTypeArg && namedTypeArg.IsGenericType)
            {
                // 递归处理嵌套的泛型类型
                return ResolveConstructedGenericType(namedTypeArg, contextClassSymbol);
            }
            return arg.ToDisplayString();
        }).ToArray();

        return $"{typeName}<{string.Join(", ", typeArguments)}>";
    }

    private static ClassHierarchyInfo CreateClassInfo(INamedTypeSymbol classSymbol, ClassDeclarationSyntax syntax = null)
    {
        return new ClassHierarchyInfo
        {
            ClassName = classSymbol.Name,
            FullName = classSymbol.ToDisplayString(),
            Accessibility = classSymbol.DeclaredAccessibility,
            IsAbstract = classSymbol.IsAbstract,
            IsSealed = classSymbol.IsSealed,
            Kind = classSymbol.TypeKind,
            Location = syntax?.GetLocation(),
            BaseTypeName = classSymbol.BaseType?.ToDisplayString() ?? "System.Object",
            Interfaces = classSymbol.Interfaces.Select(i => i.ToDisplayString()).ToList(),
            AssemblyName = classSymbol.ContainingAssembly?.Name ?? "Unknown",
            Namespace = classSymbol.ContainingNamespace?.ToDisplayString() ?? "Global"
        };
    }
    #endregion
}