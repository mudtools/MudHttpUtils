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

    [Fact]
    public void QueryMapHelper_FlattenObjectToQueryParams_DoesNotHaveRequiresDynamicCodeAttribute()
    {
        // QueryMapHelper 仅使用反射（GetProperties/GetValue），不涉及动态代码生成
        var method = typeof(QueryMapHelper).GetMethod(
            "FlattenObjectToQueryParams",
            BindingFlags.Public | BindingFlags.Static);

        method.Should().NotBeNull();

        var attr = method!.GetCustomAttribute<RequiresDynamicCodeAttribute>();
        attr.Should().BeNull("QueryMapHelper 是纯反射，不应标注 [RequiresDynamicCode]（会误导调用方）");
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
#endif

    #endregion
}
