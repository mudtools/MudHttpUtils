// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  AOT 改造属性标注断言测试：验证 EncryptContent / DefaultSensitiveDataMasker
//  上的 [RequiresDynamicCode] / [RequiresUnreferencedCode] 标注存在且消息正确
// -----------------------------------------------------------------------

using System.Reflection;
using System.Diagnostics.CodeAnalysis;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// AOT 属性标注断言测试：验证关键反射路径上的 AOT 警告标注存在。
/// </summary>
/// <remarks>
/// 这些测试确保 AOT 分析器能向消费方发出明确警告，避免静默失败。
/// 如果标注被意外移除，这些测试会立即失败。
/// </remarks>
public class AotAttributeAnnotationTests
{
    #region EncryptContent — [RequiresDynamicCode] 标注

#if NET8_0_OR_GREATER
    [Fact]
    public void EncryptContent_HasRequiresDynamicCodeAttribute()
    {
        // Arrange — 精确定位 object 重载（具有 [RequiresDynamicCode]）
        var method = typeof(EnhancedHttpClient).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == nameof(EnhancedHttpClient.EncryptContent)
                && !m.IsGenericMethod
                && m.GetParameters().Length == 3
                && m.GetParameters()[0].ParameterType == typeof(object));

        method.Should().NotBeNull("EncryptContent(object, ...) 方法应存在");

        // Act
        var attr = method!.GetCustomAttribute<RequiresDynamicCodeAttribute>();

        // Assert: EncryptContent 使用 object/Dictionary 反射式序列化，AOT 不安全
        attr.Should().NotBeNull("EncryptContent(object, ...) 应标注 [RequiresDynamicCode]");
        attr!.Message.Should().Contain("AOT");
    }

    [Fact]
    public void EncryptContent_RequiresDynamicCodeMessage_ExplainsAotIncompatibility()
    {
        var method = typeof(EnhancedHttpClient).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == nameof(EnhancedHttpClient.EncryptContent)
                && !m.IsGenericMethod
                && m.GetParameters().Length == 3
                && m.GetParameters()[0].ParameterType == typeof(object));

        method.Should().NotBeNull();
        var attr = method!.GetCustomAttribute<RequiresDynamicCodeAttribute>();
        attr.Should().NotBeNull();

        // 消息应包含关键引导信息
        attr!.Message.Should().Contain("强类型");
    }

    [Fact]
    public void EncryptContent_GenericOverload_DoesNotHaveRequiresDynamicCodeAttribute()
    {
        // 泛型重载 EncryptContent<T> 是 AOT 安全的，不应标注 [RequiresDynamicCode]
        var method = typeof(EnhancedHttpClient).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == nameof(EnhancedHttpClient.EncryptContent)
                && m.IsGenericMethod
                && m.GetParameters().Length == 2);

        method.Should().NotBeNull("EncryptContent<T> 泛型重载应存在");

        var attr = method!.GetCustomAttribute<RequiresDynamicCodeAttribute>();
        attr.Should().BeNull("EncryptContent<T> 是 AOT 安全泛型重载，不应标注 [RequiresDynamicCode]");
    }
#endif

    #endregion

    #region DecryptContent — 无 AOT 标注（AOT 安全）

    [Fact]
    public void DecryptContent_DoesNotHaveRequiresDynamicCodeAttribute()
    {
        // DecryptContent 仅使用 JsonDocument.Parse，AOT 安全，不应标注
        var method = typeof(EnhancedHttpClient).GetMethod(
            nameof(EnhancedHttpClient.DecryptContent),
            BindingFlags.Public | BindingFlags.Instance);

        method.Should().NotBeNull();

        var attr = method!.GetCustomAttribute<RequiresDynamicCodeAttribute>();
        attr.Should().BeNull("DecryptContent 使用 JsonDocument.Parse，AOT 安全，不应标注 [RequiresDynamicCode]");
    }

    #endregion

    #region DefaultSensitiveDataMasker.MaskObject — [RequiresDynamicCode] + [RequiresUnreferencedCode]

#if NET7_0_OR_GREATER
    [Fact]
    public void DefaultSensitiveDataMasker_MaskObject_HasRequiresDynamicCodeAttribute()
    {
        var method = typeof(DefaultSensitiveDataMasker).GetMethod(
            nameof(DefaultSensitiveDataMasker.MaskObject),
            BindingFlags.Public | BindingFlags.Instance);

        method.Should().NotBeNull("MaskObject 方法应存在");

        var attr = method!.GetCustomAttribute<RequiresDynamicCodeAttribute>();
        attr.Should().NotBeNull("MaskObject 应标注 [RequiresDynamicCode]");
        attr!.Message.Should().Contain("AOT");
    }

    [Fact]
    public void DefaultSensitiveDataMasker_MaskObject_HasRequiresUnreferencedCodeAttribute()
    {
        var method = typeof(DefaultSensitiveDataMasker).GetMethod(
            nameof(DefaultSensitiveDataMasker.MaskObject),
            BindingFlags.Public | BindingFlags.Instance);

        var attr = method!.GetCustomAttribute<RequiresUnreferencedCodeAttribute>();
        attr.Should().NotBeNull("MaskObject 应标注 [RequiresUnreferencedCode]");
        attr!.Message.Should().Contain("AOT");
    }

    [Fact]
    public void DefaultSensitiveDataMasker_MaskObject_AttributeMessages_GuideToAlternativeImplementation()
    {
        var method = typeof(DefaultSensitiveDataMasker).GetMethod(
            nameof(DefaultSensitiveDataMasker.MaskObject),
            BindingFlags.Public | BindingFlags.Instance);

        var rdcAttr = method!.GetCustomAttribute<RequiresDynamicCodeAttribute>();
        var rucAttr = method.GetCustomAttribute<RequiresUnreferencedCodeAttribute>();

        rdcAttr.Should().NotBeNull();
        rucAttr.Should().NotBeNull();

        // 消息应引导用户使用编译期安全的替代实现
        rdcAttr!.Message.Should().Contain("ISensitiveDataMasker");
        rucAttr!.Message.Should().Contain("ISensitiveDataMasker");
    }
#endif

    #endregion

    #region QueryMapHelper — [RequiresUnreferencedCode] 标注（无 [RequiresDynamicCode]）

#if NET6_0_OR_GREATER
    [Fact]
    public void QueryMapHelper_FlattenObjectToQueryParams_HasRequiresUnreferencedCodeAttribute()
    {
        var method = typeof(QueryMapHelper).GetMethod(
            "FlattenObjectToQueryParams",
            BindingFlags.Public | BindingFlags.Static);

        method.Should().NotBeNull();

        var attr = method!.GetCustomAttribute<RequiresUnreferencedCodeAttribute>();
        attr.Should().NotBeNull("FlattenObjectToQueryParams 应标注 [RequiresUnreferencedCode]");
        attr!.Message.Should().Contain("AOT");
    }
#endif

    // 库侧 [RequiresDynamicCode] 的 guard 锚点是 NET7_0_OR_GREATER（RequiresDynamicCodeAttribute
    // 自 .NET 7 起 in-box；net6.0 由 Abstractions 的 public polyfill 承接但库刻意不标注），
    // 故本断言必须对齐 NET7+，否则 net6.0 下必然失败。
#if NET7_0_OR_GREATER
    [Fact]
    public void QueryMapHelper_FlattenObjectToQueryParams_HasRequiresDynamicCodeAttribute()
    {
        // P0 修正：该方法内部调用 IHttpContentSerializer.Serialize(object, Type)（运行时类型分派，已标注 RDC），
        // 因此自身也必须标注 RDC，否则调用方会产生 IL3050。
        var method = typeof(QueryMapHelper).GetMethod(
            "FlattenObjectToQueryParams",
            BindingFlags.Public | BindingFlags.Static);

        method.Should().NotBeNull();

        var attr = method!.GetCustomAttribute<RequiresDynamicCodeAttribute>();
        attr.Should().NotBeNull("FlattenObjectToQueryParams 调用 Serialize(object, Type)，应标注 [RequiresDynamicCode]");
    }
#endif

    #endregion

    #region XmlSerialize — [RequiresDynamicCode] 标注

#if NET7_0_OR_GREATER
    [Theory]
    [InlineData(nameof(XmlSerialize.Serialize))]
    [InlineData(nameof(XmlSerialize.Deserialize))]
    public void XmlSerialize_Methods_HaveRequiresDynamicCodeAttribute(string methodName)
    {
        var methods = typeof(XmlSerialize).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == methodName)
            .ToList();

        methods.Should().NotBeEmpty($"应存在 {methodName} 方法");

        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<RequiresDynamicCodeAttribute>();
            attr.Should().NotBeNull($"{methodName} 应标注 [RequiresDynamicCode]");
            attr!.Message.Should().Contain("XmlSerializer");
            attr.Message.Should().Contain("AOT");
        }
    }

    [Theory]
    [InlineData(nameof(XmlSerialize.Serialize))]
    [InlineData(nameof(XmlSerialize.Deserialize))]
    public void XmlSerialize_Methods_HaveRequiresUnreferencedCodeAttribute(string methodName)
    {
        // P0 修正：XmlSerializer 在 BCL 中同时标注 RUC/RDC；仅标注 RDC 会让消费方拿不到 IL2026 提示。
        var methods = typeof(XmlSerialize).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == methodName)
            .ToList();

        methods.Should().NotBeEmpty($"应存在 {methodName} 方法");

        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<RequiresUnreferencedCodeAttribute>();
            attr.Should().NotBeNull($"{methodName} 应标注 [RequiresUnreferencedCode]");
            attr!.Message.Should().Contain("XmlSerializer");
        }
    }
#endif

    #endregion

    #region 接口侧 AOT 契约（P0-4 / P2-5）

    [Fact]
    public void IHttpContentSerializer_NonGenericSerialize_HasRequiresUnreferencedCodeAndDynamicCode()
    {
        var method = typeof(IHttpContentSerializer).GetMethod(
            "Serialize",
            new[] { typeof(object), typeof(Type), typeof(object) });

        method.Should().NotBeNull("IHttpContentSerializer.Serialize(object, Type, object?) 应存在");

        method!.GetCustomAttribute<RequiresUnreferencedCodeAttribute>()
            .Should().NotBeNull("非泛型 Serialize 使用运行时类型分派，应标注 [RequiresUnreferencedCode]");
        // 库侧 RDC 的 guard 锚点是 NET7_0_OR_GREATER（见类型级注释），net6.0 下该标注不存在。
#if NET7_0_OR_GREATER
        method.GetCustomAttribute<RequiresDynamicCodeAttribute>()
            .Should().NotBeNull("非泛型 Serialize 使用运行时类型分派，应标注 [RequiresDynamicCode]");
#endif
    }

    [Fact]
    public void IEncryptableHttpClient_ObjectOverload_HasRequiresUnreferencedCodeAndDynamicCode()
    {
        var method = typeof(IEncryptableHttpClient).GetMethod(
            "EncryptContent",
            new[] { typeof(object), typeof(string), typeof(SerializeType) });

        method.Should().NotBeNull("IEncryptableHttpClient.EncryptContent(object, ...) 应存在");

        method!.GetCustomAttribute<RequiresUnreferencedCodeAttribute>()
            .Should().NotBeNull("object 重载使用运行时类型分派与 XML 序列化，应标注 [RequiresUnreferencedCode]");
        // 库侧 RDC 的 guard 锚点是 NET7_0_OR_GREATER（见类型级注释），net6.0 下该标注不存在。
#if NET7_0_OR_GREATER
        method.GetCustomAttribute<RequiresDynamicCodeAttribute>()
            .Should().NotBeNull("object 重载使用运行时类型分派，应标注 [RequiresDynamicCode]");
#endif
    }

    [Fact]
    public void ISensitiveDataMasker_MaskObject_IsNotAnnotated_ToKeepAotSafeImplementationsClean()
    {
        // 设计决策：接口刻意不标注 RUC/RDC。.NET 分析器要求接口与实现标注"完全一致"（双向），
        // 若接口标注，则 AOT 安全的 AotSafeSensitiveDataMasker 也被迫带标注，
        // 使其调用方在 AOT 安全路径上收到误导性告警。非 AOT 实现自行标注并压制 IL2046/IL3051。
        var method = typeof(ISensitiveDataMasker).GetMethod("MaskObject", new[] { typeof(object) });

        method.Should().NotBeNull();
        method!.GetCustomAttribute<RequiresUnreferencedCodeAttribute>()
            .Should().BeNull("接口刻意不标注 RUC，以保持 AotSafeSensitiveDataMasker 的调用零告警");
        method.GetCustomAttribute<RequiresDynamicCodeAttribute>()
            .Should().BeNull("接口刻意不标注 RDC，以保持 AotSafeSensitiveDataMasker 的调用零告警");
    }

#if NET8_0_OR_GREATER
    [Fact]
    public void IAotJsonContentSerializer_Exists_OnNet8OrGreater()
    {
        var type = typeof(IAotJsonContentSerializer);
        type.IsInterface.Should().BeTrue();

        var typeInfoParameter = typeof(System.Text.Json.Serialization.Metadata.JsonTypeInfo<>);
        type.GetMethods().Should().OnlyContain(m =>
            m.GetParameters().Any(p =>
                p.ParameterType.IsGenericType &&
                p.ParameterType.GetGenericTypeDefinition() == typeInfoParameter),
            "IAotJsonContentSerializer 的每个方法都应接收 JsonTypeInfo<T> 参数（AOT 快车道）");
    }
#endif

    #endregion
}
