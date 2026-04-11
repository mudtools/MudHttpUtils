// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.CodeGenerator;

internal static class TypeSymbolHelper
{
    // 可空类型后缀
    private const string NullableSuffix = "?";

    #region 类型信息获取

    /// <summary>
    /// 获取类型或接口的完整命名空间
    /// </summary>
    /// <param name="compilation">编译对象</param>
    /// <param name="typeName">类型或接口名称</param>
    /// <returns>类型或接口的完整命名空间</returns>
    public static string GetTypeAllDisplayString(Compilation compilation, string typeName)
    {
        if (compilation != null)
        {
            var tokenManagerSymbol = compilation.GetTypeByMetadataName(typeName);
            if (tokenManagerSymbol != null)
            {
                return tokenManagerSymbol.ToDisplayString();
            }
        }

        // 如果没有找到，返回原始名称
        return typeName;
    }

    #endregion

    #region 方法信息处理

    /// <summary>
    /// 获取方法参数列表字符串（包含默认值、命名空间和可为空修饰符）
    /// </summary>
    public static string GetParameterList(IMethodSymbol methodSymbol)
    {
        if (methodSymbol == null)
            return string.Empty;

        // 基于FullyQualifiedFormat进行自定义
        var format = SymbolDisplayFormat.FullyQualifiedFormat
            .WithParameterOptions(
                SymbolDisplayParameterOptions.IncludeName |
                SymbolDisplayParameterOptions.IncludeType |
                SymbolDisplayParameterOptions.IncludeDefaultValue |
                SymbolDisplayParameterOptions.IncludeParamsRefOut)
            .WithMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
                SymbolDisplayMiscellaneousOptions.AllowDefaultLiteral);

        return string.Join(", ", methodSymbol.Parameters.Select(p =>
            p.ToDisplayString(format)));
    }


    /// <summary>
    /// 递归获取接口及其所有父接口的所有方法（去重）
    /// </summary>
    /// <param name="interfaceSymbol">接口符号</param>
    /// <param name="includeParentInterfaces">是否包含父接口的方法</param>
    /// <param name="excludedInterfaces">要排除的接口名称列表（可选）</param>
    public static IEnumerable<IMethodSymbol> GetAllMethods(
        INamedTypeSymbol interfaceSymbol,
        bool includeParentInterfaces = true,
        IEnumerable<string> excludedInterfaces = null)
    {
        if (interfaceSymbol == null)
            return [];

        var visitedInterfaces = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var excludedSet = excludedInterfaces?.ToHashSet() ?? new HashSet<string>();
        return GetAllRecursive(interfaceSymbol, visitedInterfaces, includeParentInterfaces, excludedSet);
    }

    private static IEnumerable<IMethodSymbol> GetAllRecursive(
        INamedTypeSymbol interfaceSymbol,
        HashSet<INamedTypeSymbol> visitedInterfaces,
        bool includeParentInterfaces,
        HashSet<string> excludedInterfaces)
    {
        // 避免循环引用
        if (visitedInterfaces.Contains(interfaceSymbol))
            yield break;

        visitedInterfaces.Add(interfaceSymbol);

        // 检查当前接口是否在排除列表中
        if (excludedInterfaces != null && excludedInterfaces.Count > 0)
        {
            // 检查简单名称或完整名称是否在排除列表中
            if (ShouldExcludeInterface(interfaceSymbol, excludedInterfaces))
            {
                yield break; // 跳过整个接口及其方法
            }
        }

        // 首先处理当前接口的方法
        foreach (var method in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            yield return method;
        }

        // 如果不需要父接口的方法，则直接返回
        if (!includeParentInterfaces)
            yield break;

        // 然后递归处理所有父接口
        // 使用 AllInterfaces 替代 Interfaces，确保获取所有基接口（包括跨程序集的）
        var baseInterfaces = SafeGetAllInterfaces(interfaceSymbol);
        if (baseInterfaces == null)
            yield break;

        foreach (var baseInterface in baseInterfaces)
        {
            foreach (var baseMethod in GetAllRecursive(
                baseInterface,
                visitedInterfaces,
                includeParentInterfaces,
                excludedInterfaces))
            {
                yield return baseMethod;
            }
        }
    }

    /// <summary>
    /// 安全地获取接口的所有基接口（处理设计时可能抛出的异常）
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> SafeGetAllInterfaces(INamedTypeSymbol interfaceSymbol)
    {
        try
        {
            return interfaceSymbol.AllInterfaces;
        }
        catch
        {
            // 如果无法访问AllInterfaces属性（例如符号未完全解析），返回null
            // 在设计时这是安全的，因为编译时会重新检查
            return null;
        }
    }

    #endregion

    #region 属性特性处理

    /// <summary>
    /// 检查接口是否具有指定的特性
    /// </summary>
    /// <param name="interfaceSymbol">接口符号</param>
    /// <param name="attributeType">特性类型</param>
    /// <param name="attributeValue">特性值</param>
    /// <returns>如果接口具有指定特性返回true，否则返回false</returns>
    public static bool HasPropertyAttribute(INamedTypeSymbol interfaceSymbol, string attributeType, string attributeValue)
    {
        if (interfaceSymbol == null)
            return false;

        var attributeName = attributeType + "Attribute";

        return interfaceSymbol.GetAttributes()
                              .Any(attr =>
                                  (attr.AttributeClass?.Name == attributeName || attr.AttributeClass?.Name == attributeType) &&
                                  attr.ConstructorArguments.Length > 0 &&
                                  attr.ConstructorArguments[0].Value?.ToString() == attributeValue);
    }

    /// <summary>
    /// 检查接口是否具有指定的特性（不检查特性值）
    /// </summary>
    /// <param name="interfaceSymbol">接口符号</param>
    /// <param name="attributeType">特性类型</param>
    /// <returns>如果接口具有指定特性返回true，否则返回false</returns>
    public static bool HasPropertyAttribute(INamedTypeSymbol interfaceSymbol, string attributeType)
    {
        if (interfaceSymbol == null)
            return false;

        var attributeName = attributeType.EndsWith("Attribute", StringComparison.Ordinal)
            ? attributeType
            : attributeType + "Attribute";

        return interfaceSymbol.GetAttributes()
                              .Any(attr =>
                                  attr.AttributeClass?.Name == attributeName ||
                                  attr.AttributeClass?.Name == attributeType);
    }


    /// <summary>
    /// 判断属性是否具有特定特性
    /// </summary>
    public static bool HasPropertyAttribute(IPropertySymbol propertySymbol, string attributeName)
    {
        if (propertySymbol == null || string.IsNullOrEmpty(attributeName))
            return false;

        var fullAttributeName = attributeName.EndsWith("Attribute", StringComparison.Ordinal)
            ? attributeName
            : attributeName + "Attribute";

        return propertySymbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.Name == fullAttributeName ||
                         attr.AttributeClass?.Name == attributeName);
    }
    #endregion

    #region 类名称生成

    /// <summary>
    /// 根据接口名称获取实现类名称
    /// </summary>
    /// <param name="interfaceName">接口名称</param>
    /// <returns>实现类名称</returns>
    /// <remarks>
    /// 如果接口名称以"I"开头且第二个字符为大写，则移除"I"前缀；否则添加"Impl"后缀
    /// </remarks>
    public static string GetImplementationClassName(string interfaceName)
    {
        if (string.IsNullOrEmpty(interfaceName))
            return "NullOrEmptyInterfaceName";

        return interfaceName.StartsWith("I", StringComparison.Ordinal) && interfaceName.Length > 1 && char.IsUpper(interfaceName[1])
            ? interfaceName.Substring(1)
            : interfaceName + "Impl";
    }

    /// <summary>
    /// 获取包装类名称
    /// </summary>
    /// <param name="wrapInterfaceName">包装接口名称</param>
    /// <returns>包装类名称</returns>
    public static string GetWrapClassName(string wrapInterfaceName)
    {
        if (string.IsNullOrEmpty(wrapInterfaceName))
            return "NullOrEmptyWrapInterfaceName";

        if (wrapInterfaceName.StartsWith("I", StringComparison.Ordinal) && wrapInterfaceName.Length > 1)
        {
            return wrapInterfaceName.Substring(1);
        }
        return wrapInterfaceName + HttpClientGeneratorConstants.DefaultWrapSuffix;
    }

    #endregion

    #region 获取所有属性列表
    /// <summary>
    /// 递归获取接口及其所有父接口的所有属性（去重）
    /// </summary>
    /// <param name="interfaceSymbol">接口符号</param>
    /// <param name="includeParentInterfaces">是否包含父接口的属性</param>
    /// <param name="excludedInterfaces">要排除的接口名称列表（可选）</param>
    public static IEnumerable<IPropertySymbol> GetAllProperties(
        INamedTypeSymbol interfaceSymbol,
        bool includeParentInterfaces = true,
        IEnumerable<string> excludedInterfaces = null)
    {
        if (interfaceSymbol == null)
            return [];

        var visitedInterfaces = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var excludedSet = excludedInterfaces?.ToHashSet() ?? new HashSet<string>();
        return GetAllPropertiesRecursive(interfaceSymbol, visitedInterfaces, includeParentInterfaces, excludedSet);
    }

    private static IEnumerable<IPropertySymbol> GetAllPropertiesRecursive(
        INamedTypeSymbol interfaceSymbol,
        HashSet<INamedTypeSymbol> visitedInterfaces,
        bool includeParentInterfaces,
        HashSet<string> excludedInterfaces)
    {
        // 避免循环引用
        if (visitedInterfaces.Contains(interfaceSymbol))
            yield break;

        visitedInterfaces.Add(interfaceSymbol);

        // 检查当前接口是否在排除列表中
        if (excludedInterfaces != null && excludedInterfaces.Count > 0)
        {
            if (ShouldExcludeInterface(interfaceSymbol, excludedInterfaces))
            {
                yield break; // 跳过整个接口及其属性
            }
        }

        // 首先处理当前接口的属性
        foreach (var property in interfaceSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            yield return property;
        }

        // 如果不需要父接口的属性，则直接返回
        if (!includeParentInterfaces)
            yield break;

        // 然后递归处理所有父接口
        var baseInterfaces = SafeGetAllInterfaces(interfaceSymbol);
        if (baseInterfaces == null)
            yield break;

        foreach (var baseInterface in baseInterfaces)
        {
            foreach (var baseProperty in GetAllPropertiesRecursive(
                baseInterface,
                visitedInterfaces,
                includeParentInterfaces,
                excludedInterfaces))
            {
                yield return baseProperty;
            }
        }
    }

    /// <summary>
    /// 检查接口是否应该被排除（支持泛型）
    /// </summary>
    private static bool ShouldExcludeInterface(
        INamedTypeSymbol interfaceSymbol,
        HashSet<string> excludedInterfaces)
    {
        if (excludedInterfaces == null || excludedInterfaces.Count == 0)
            return false;

        try
        {
            // 检查各种可能的名称格式
            var namesToCheck = new List<string>();

            // 安全添加名称，避免在设计时抛出异常
            try { namesToCheck.Add(interfaceSymbol.Name); } catch { }
            try { namesToCheck.Add(interfaceSymbol.ToDisplayString()); } catch { }
            try { namesToCheck.Add(interfaceSymbol.MetadataName); } catch { }
            try { namesToCheck.Add(interfaceSymbol.ToString()); } catch { }

            // 添加命名空间和名称的组合
            try
            {
                if (!string.IsNullOrEmpty(interfaceSymbol.ContainingNamespace?.Name))
                {
                    namesToCheck.Add($"{interfaceSymbol.ContainingNamespace}.{interfaceSymbol.Name}");
                }
            } catch { }

            // 检查泛型接口
            try
            {
                if (interfaceSymbol.IsGenericType)
                {
                    // 添加泛型定义
                    var originalDefinition = interfaceSymbol.OriginalDefinition;
                    try { namesToCheck.Add(originalDefinition.ToDisplayString()); } catch { }
                    try { namesToCheck.Add(originalDefinition.MetadataName); } catch { }

                    // 添加无参数版本的泛型名称
                    var genericNameWithoutArity = interfaceSymbol.Name;
                    if (genericNameWithoutArity.Contains('`'))
                    {
                        namesToCheck.Add(genericNameWithoutArity.Substring(0, genericNameWithoutArity.IndexOf('`')));
                    }
                }
            } catch { }

            // 检查是否有匹配的排除项
            foreach (var name in namesToCheck)
            {
                if (excludedInterfaces.Contains(name))
                    return true;
            }
        }
        catch
        {
            // 如果在设计时无法解析接口符号，返回false以继续处理
            // 编译时会重新检查
        }

        return false;
    }
    #endregion

    #region Type Display Helpers

    /// <summary>
    /// 获取类型的显示全名（含全名空间），正确处理多维数组、交错数组和泛型类型
    /// </summary>
    /// <param name="typeSymbol">类型符号</param>
    /// <returns>正确的类型显示字符串</returns>
    public static string GetTypeFullName(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null)
            return string.Empty;

        // 处理数组类型（包括多维数组和交错数组）
        if (typeSymbol is IArrayTypeSymbol arrayTypeSymbol)
        {
            return GetArrayTypeDisplayString(arrayTypeSymbol);
        }

        // 处理指针类型
        if (typeSymbol is IPointerTypeSymbol pointerTypeSymbol)
        {
            return GetTypeFullName(pointerTypeSymbol.PointedAtType) + "*";
        }

        // 处理可为null的值类型（Nullable<T>）
        if (typeSymbol.IsValueType && typeSymbol.NullableAnnotation == NullableAnnotation.Annotated)
        {
            var underlyingType = ((INamedTypeSymbol)typeSymbol).TypeArguments[0];
            return GetTypeFullName(underlyingType) + "?";
        }

        // 对于非数组类型，使用适当的显示格式
        var displayFormat = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                               SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier,
            parameterOptions: SymbolDisplayParameterOptions.IncludeType,
            propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
            localOptions: SymbolDisplayLocalOptions.IncludeType,
            memberOptions: SymbolDisplayMemberOptions.IncludeType |
                         SymbolDisplayMemberOptions.IncludeParameters |
                         SymbolDisplayMemberOptions.IncludeContainingType,
            delegateStyle: SymbolDisplayDelegateStyle.NameAndSignature,
            extensionMethodStyle: SymbolDisplayExtensionMethodStyle.StaticMethod);

        return typeSymbol.ToDisplayString(displayFormat);
    }

    /// <summary>
    /// 专门处理数组类型的显示字符串
    /// </summary>
    private static string GetArrayTypeDisplayString(IArrayTypeSymbol arrayTypeSymbol)
    {
        // 递归获取元素类型的显示字符串
        var elementTypeDisplay = GetTypeFullName(arrayTypeSymbol.ElementType);

        // 处理多维数组
        if (arrayTypeSymbol.Rank > 1)
        {
            // 对于多维数组，使用逗号表示维度，例如 int[,] 或 string[,,]
            var commas = new string(',', arrayTypeSymbol.Rank - 1);
            return $"{elementTypeDisplay}[{commas}]";
        }
        // 处理一维数组（包括交错数组）
        else
        {
            // 对于一维数组，检查元素类型是否也是数组（交错数组）
            if (arrayTypeSymbol.ElementType is IArrayTypeSymbol)
            {
                // 交错数组：int[][], string[][][] 等
                // 元素类型已经包含了自己的[]，所以这里不需要额外处理
                // 但需要确保格式正确，例如 int[][] 而不是 int[] []
                return elementTypeDisplay + "[]";
            }
            else
            {
                // 普通一维数组
                return $"{elementTypeDisplay}[]";
            }
        }
    }

    #endregion

    #region 对象类型判断

    // 定义基本类型的完整名称集合（包含别名）
    private static readonly HashSet<string> BasicTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // 基本值类型
        "System.Boolean", "bool",
        "System.Byte", "byte",
        "System.SByte", "sbyte",
        "System.Char", "char",
        "System.Decimal", "decimal",
        "System.Double", "double",
        "System.Single", "float",
        "System.Int32", "int",
        "System.UInt32", "uint",
        "System.Int64", "long",
        "System.UInt64", "ulong",
        "System.Int16", "short",
        "System.UInt16", "ushort",
        "System.DateTime","DateTime",
        
        // 特殊类型
        "System.IntPtr", "nint",
        "System.UIntPtr", "nuint",
        "System.Half",
        
        // 引用类型
        "System.String", "string",
        "System.Object", "object"
    };


    /// <summary>
    /// 检查是否为.net基本数据类型
    /// </summary>
    /// <param name="typeName">类型名称</param>
    /// <returns>如果是基本类型返回true，否则返回false</returns>
    public static bool IsBasicType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        // 处理可空类型
        string normalizedName = typeName.Trim();
        if (normalizedName.EndsWith(NullableSuffix, StringComparison.Ordinal))
        {
            normalizedName = normalizedName.Substring(0, normalizedName.Length - NullableSuffix.Length).Trim();
        }

        // 检查是否为基本类型
        return BasicTypeNames.Contains(normalizedName);
    }

    /// <summary>
    /// 检查是否为 .NET 基本数据类型（通过类型符号）
    /// </summary>
    /// <param name="typeSymbol">类型符号</param>
    /// <returns>如果是基本类型返回 true，否则返回 false</returns>
    public static bool IsBasicType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null)
            return false;

        // 处理可空类型
        var typeToCheck = typeSymbol;
        if (typeSymbol is INamedTypeSymbol { IsValueType: true, ConstructedFrom.SpecialType: SpecialType.System_Nullable_T })
        {
            typeToCheck = typeSymbol.GetNullableUnderlyingType();
        }

        // 检查是否为特殊类型（内置类型）
        var specialType = typeToCheck.SpecialType;
        bool isBasic = specialType switch
        {
            // 值类型
            SpecialType.System_Boolean => true,
            SpecialType.System_Byte => true,
            SpecialType.System_SByte => true,
            SpecialType.System_Char => true,
            SpecialType.System_Decimal => true,
            SpecialType.System_Double => true,
            SpecialType.System_Single => true,
            SpecialType.System_Int32 => true,
            SpecialType.System_UInt32 => true,
            SpecialType.System_Int64 => true,
            SpecialType.System_UInt64 => true,
            SpecialType.System_Int16 => true,
            SpecialType.System_UInt16 => true,
            SpecialType.System_IntPtr => true,
            SpecialType.System_UIntPtr => true,
            SpecialType.System_DateTime => true,
            SpecialType.System_Array => true,

            // 引用类型
            SpecialType.System_String => true,
            SpecialType.System_Object => true,

            _ => false
        };
        if (isBasic)
            return true;

        // 检查是否为 System.Drawing.Color 类型
        if (IsSystemDrawingColor(typeToCheck))
            return true;

        if (IsObjectArray(typeToCheck))
            return true;

        return false;
    }

    /// <summary>
    /// 检查是否为 System.Drawing.Color 类型
    /// </summary>
    private static bool IsSystemDrawingColor(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null)
            return false;

        // 方法1：检查完整类型名称
        var fullName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (fullName == "System.Drawing.Color" || fullName == "global::System.Drawing.Color")
            return true;

        // 方法2：检查命名空间和类型名称
        if (typeSymbol.ContainingNamespace?.ToString() == "System.Drawing" && typeSymbol.Name == "Color")
            return true;

        // 方法3：检查元数据名称（最可靠）
        if (typeSymbol is INamedTypeSymbol namedType)
        {
            // 检查完全限定名称
            var metadataName = namedType.GetFullMetadataName();
            if (metadataName == "System.Drawing.Color")
                return true;
        }

        return false;
    }

    /// <summary>
    /// 检查是否为 object 数组（任意维度）
    /// </summary>
    public static bool IsObjectArray(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is not IArrayTypeSymbol arrayType)
            return false;

        // 检查元素类型是否为 object
        var elementType = arrayType.ElementType;
        return elementType.SpecialType == SpecialType.System_Object;
    }

    /// <summary>
    /// 扩展方法：获取类型的完整元数据名称
    /// </summary>
    private static string GetFullMetadataName(this ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null)
            return string.Empty;

        var namespaceName = typeSymbol.ContainingNamespace?.ToString();
        var typeName = typeSymbol.Name;

        if (!string.IsNullOrEmpty(namespaceName))
            return $"{namespaceName}.{typeName}";

        return typeName;
    }

    /// <summary>
    /// 获取可空类型的底层类型（扩展方法）
    /// </summary>
    private static ITypeSymbol GetNullableUnderlyingType(this ITypeSymbol typeSymbol)
    {
        if (typeSymbol is INamedTypeSymbol { ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } nullableType)
        {
            return nullableType.TypeArguments.FirstOrDefault();
        }
        return typeSymbol;
    }

    /// <summary>
    /// 通过语义分析判断类型是否为 .NET 枚举类型
    /// </summary>
    /// <param name="typeSymbol">类型符号</param>
    /// <returns>如果是枚举类型返回 true，否则返回 false</returns>
    public static bool IsEnumType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null)
            return false;

        // 使用模式匹配简化代码
        return typeSymbol switch
        {
            { TypeKind: TypeKind.Enum } => true,
            INamedTypeSymbol { ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } nullableType
                => nullableType.TypeArguments.FirstOrDefault()?.TypeKind == TypeKind.Enum,
            _ => false
        };
    }

    /// <summary>
    /// 通过语义分析判断类型是否为复杂对象类型
    /// （复杂对象：类、结构体、记录等非基本类型）
    /// </summary>
    /// <param name="typeSymbol">类型符号</param>
    /// <returns>如果是复杂对象类型返回 true，否则返回 false</returns>
    public static bool IsComplexObjectType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null)
            return false;

        // 首先排除基本类型
        if (IsBasicType(typeSymbol))
            return false;

        // 排除枚举类型
        if (IsEnumType(typeSymbol))
            return false;

        // 排除委托和指针等特殊类型
        if (typeSymbol.TypeKind is TypeKind.Delegate or TypeKind.Pointer or TypeKind.Dynamic)
            return false;

        // 处理可空类型
        var typeToCheck = typeSymbol;
        if (typeSymbol is INamedTypeSymbol { ConstructedFrom.SpecialType: SpecialType.System_Nullable_T })
        {
            typeToCheck = typeSymbol.GetNullableUnderlyingType();

            // 可空的基本类型已经排除，这里只需要检查可空的复杂类型
            if (IsBasicType(typeToCheck) || IsEnumType(typeToCheck))
                return false;
        }

        // 复杂对象类型包括：类、结构体、记录、接口、数组等
        return typeToCheck.TypeKind switch
        {
            TypeKind.Class => true,
            TypeKind.Struct => !IsBasicType(typeToCheck), // 再次确认不是基本结构体
            TypeKind.Interface => true,
            _ => false
        };
    }

    /// <summary>
    /// 判断类型是否为异步返回类型（Task 或 ValueTask）
    /// </summary>
    /// <param name="typeSymbol">类型符号</param>
    /// <returns>如果是异步类型返回 true，否则返回 false</returns>
    public static bool IsAsyncType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is INamedTypeSymbol namedType)
        {
            return namedType.Name == "Task" && (namedType.TypeArguments.Length == 0 || namedType.TypeArguments.Length == 1) ||
                   namedType.Name == "ValueTask" && (namedType.TypeArguments.Length == 0 || namedType.TypeArguments.Length == 1);
        }
        return false;
    }

    /// <summary>
    /// 提取异步方法的内部返回类型（从 Task<T> 或 ValueTask<T> 中提取 T）
    /// </summary>
    /// <param name="asyncType">异步类型符号（Task 或 ValueTask）</param>
    /// <returns>内部返回类型字符串</returns>
    public static string ExtractAsyncInnerType(ITypeSymbol asyncType)
    {
        if (asyncType is not INamedTypeSymbol namedType || !namedType.IsGenericType)
        {
            // 如果是 Task 或 ValueTask 而不是 Task<T> 或 ValueTask<T>，返回 void
            if (asyncType is INamedTypeSymbol simpleType &&
                (simpleType.Name == "Task" || simpleType.Name == "ValueTask") &&
                simpleType.TypeArguments.Length == 0)
            {
                return "void";
            }
            return GetTypeFullName(asyncType);
        }

        // 提取类型参数
        var genericType = namedType.TypeArguments[0];

        // 检查是否为可空类型（如 Task<int?>）
        if (genericType is INamedTypeSymbol genericNamedType &&
            genericNamedType.IsGenericType &&
            genericNamedType.Name == "Nullable" &&
            genericNamedType.TypeArguments.Length > 0)
        {
            return GetTypeFullName(genericNamedType.TypeArguments[0]) + "?";
        }

        return GetTypeFullName(genericType);
    }

    /// <summary>
    /// 获取枚举值的字面量表示（包含命名空间）
    /// </summary>
    /// <param name="enumType">枚举类型符号</param>
    /// <param name="value">枚举值</param>
    /// <returns>枚举值的字面量表示，如 "MyEnum.Value" 或 "(MyEnum)0"</returns>
    public static string GetEnumValueLiteral(ITypeSymbol enumType, object value)
    {
        if (enumType == null)
            return value?.ToString() ?? "null";

        var enumTypeName = GetTypeFullName(enumType);

        // 尝试根据值找到对应的枚举成员
        var matchingMember = enumType.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => f.IsConst && f.HasConstantValue && Equals(f.ConstantValue, value))
            .Select(f => f.Name)
            .FirstOrDefault();

        return matchingMember != null
            ? $"{enumTypeName}.{matchingMember}"
            : $"({enumTypeName}){value}";
    }

    /// <summary>
    /// 获取类型的公共属性列表
    /// </summary>
    /// <param name="typeSymbol">类型符号</param>
    /// <returns>公共属性列表</returns>
    public static IEnumerable<IPropertySymbol> GetPublicProperties(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null)
            return [];

        return typeSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property => property.DeclaredAccessibility == Accessibility.Public);
    }

    /// <summary>
    /// 获取类型的公共方法列表
    /// </summary>
    /// <param name="typeSymbol">类型符号</param>
    /// <returns>公共方法列表</returns>
    public static IEnumerable<IMethodSymbol> GetPublicMethods(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null)
            return [];

        return typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => method.DeclaredAccessibility == Accessibility.Public);
    }

    /// <summary>
    /// 获取基本类型的转换代码字符串（用于代码生成）
    /// </summary>
    /// <param name="typeSymbol">类型符号</param>
    /// <param name="fieldName">字段名</param>
    /// <returns>类型转换代码字符串</returns>
    public static string GetBasicTypeConvertCode(ITypeSymbol typeSymbol, string fieldName)
    {
        if (typeSymbol == null || string.IsNullOrEmpty(fieldName))
            return fieldName;

        // 检查是否为 System.Drawing.Color 类型
        if (IsSystemDrawingColor(typeSymbol))
            return $"ColorHelper.ConvertToColor({fieldName})";

        // 处理可空类型
        var typeToCheck = typeSymbol;
        if (typeSymbol is INamedTypeSymbol { ConstructedFrom.SpecialType: SpecialType.System_Nullable_T })
        {
            typeToCheck = typeSymbol.GetNullableUnderlyingType();
        }

        // 根据特殊类型返回转换代码
        return typeToCheck.SpecialType switch
        {
            SpecialType.System_Boolean => $"{fieldName}.ConvertToBool()",
            SpecialType.System_SByte => $"Convert.ToSByte({fieldName})",
            SpecialType.System_Byte => $"Convert.ToByte({fieldName})",
            SpecialType.System_Int16 => $"Convert.ToInt16({fieldName})",
            SpecialType.System_UInt16 => $"Convert.ToUInt16({fieldName})",
            SpecialType.System_Int32 => $"Convert.ToInt32({fieldName})",
            SpecialType.System_UInt32 => $"Convert.ToUInt32({fieldName})",
            SpecialType.System_Int64 => $"Convert.ToInt64({fieldName})",
            SpecialType.System_UInt64 => $"Convert.ToUInt64({fieldName})",
            SpecialType.System_Single => $"Convert.ToSingle({fieldName})",
            SpecialType.System_Double => $"Convert.ToDouble({fieldName})",
            SpecialType.System_Decimal => $"Convert.ToDecimal({fieldName})",
            SpecialType.System_Char => $"Convert.ToChar({fieldName})",
            SpecialType.System_String => $"{fieldName}.ToString()",
            SpecialType.System_DateTime => $"ObjectExtensions.ConvertToDateTime({fieldName})",
            _ => fieldName
        };
    }
    #endregion
}

/// <summary>
/// ISymbol 扩展方法
/// </summary>
internal static class SymbolExtensions
{
    /// <summary>
    /// 获取符号的完整限定名称（包含命名空间）
    /// </summary>
    /// <param name="symbol">符号</param>
    /// <returns>完整限定名称</returns>
    public static string GetFullyQualifiedName(this ISymbol symbol)
    {
        if (symbol == null)
            return string.Empty;

        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }
}
