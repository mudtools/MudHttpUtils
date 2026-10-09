// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
#if NET8_0_OR_GREATER
using System.Text.Json.Serialization.Metadata;
#endif

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// F1 回归护栏：<see cref="EnhancedHttpClientOptions.Clone"/> 必须做<b>全字段浅拷贝</b>
/// （含 <c>Logger</c> / 拦截器 / <c>SensitiveDataMasker</c> / <c>AppAccessAuthorizer</c> 等
/// "DI 解析面"属性 —— 这批属性恰是 internal <c>EnhancedHttpClientOptionsCloner</c> **刻意不拷贝**的）。
/// </summary>
/// <remarks>
/// 动因：消费方（下游 SDK 工厂）需要"包装既有客户端时复用整份配置"，此前只能手写 13+ 字段拷贝。
/// 若误复用 internal Cloner 会得到<b>字段更少</b>的克隆（丢配置）⇒ 本用例把"全字段"钉死。
/// </remarks>
public class EnhancedHttpClientOptionsCloneTests
{
    /// <summary>
    /// Clone 必须逐属性复制<b>全部</b>可写属性，且为新实例（集合/接口属性为浅拷贝 ⇒ 同引用）。
    /// 新增可写属性若未同步 <c>Clone</c>，本用例会因 <see cref="CreateValue"/> 抛出而失败。
    /// </summary>
    [Fact]
    public void Clone_ShouldCopyAllWritableProperties()
    {
        var writable = typeof(EnhancedHttpClientOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToList();

        writable.Should().NotBeEmpty();

        var source = new EnhancedHttpClientOptions();
        foreach (var property in writable)
        {
            property.SetValue(source, CreateValue(property.PropertyType, property.Name));
        }

        var clone = source.Clone();

        clone.Should().NotBeSameAs(source);
        clone.Should().BeOfType<EnhancedHttpClientOptions>();

        foreach (var property in writable)
        {
            var expected = property.GetValue(source);
            var actual = property.GetValue(clone);

            if (property.PropertyType.IsValueType || property.PropertyType == typeof(string))
            {
                actual.Should().Be(expected, $"Clone 必须复制 {property.Name}");
            }
            else
            {
                // 全字段**浅**拷贝：集合 / 接口属性共享同一引用（文档化契约）。
                actual.Should().BeSameAs(expected, $"Clone 必须复制 {property.Name}（浅拷贝 ⇒ 同引用）");
            }
        }
    }

    /// <summary>
    /// 明确钉死"DI 解析面"5 个属性也被拷贝（这是与 internal Cloner 的关键差异，也是本 API 的存在理由）。
    /// </summary>
    [Fact]
    public void Clone_ShouldCopyDiResolvedProperties()
    {
        var source = new EnhancedHttpClientOptions
        {
            Logger = NullLogger.Instance,
            RequestInterceptors = new[] { Mock.Of<IHttpRequestInterceptor>() },
            ResponseInterceptors = new[] { Mock.Of<IHttpResponseInterceptor>() },
            SensitiveDataMasker = Mock.Of<ISensitiveDataMasker>(),
            AppAccessAuthorizer = Mock.Of<IAppAccessAuthorizer>(),
        };

        var clone = source.Clone();

        clone.Logger.Should().BeSameAs(source.Logger);
        clone.RequestInterceptors.Should().BeSameAs(source.RequestInterceptors);
        clone.ResponseInterceptors.Should().BeSameAs(source.ResponseInterceptors);
        clone.SensitiveDataMasker.Should().BeSameAs(source.SensitiveDataMasker);
        clone.AppAccessAuthorizer.Should().BeSameAs(source.AppAccessAuthorizer);
    }

    /// <summary>
    /// 默认（未配置）实例的 Clone 应与新建实例默认值等价，且不受源实例后续修改影响（浅拷贝仅共享引用，不共享"容器"）。
    /// </summary>
    [Fact]
    public void Clone_OfDefaultInstance_ShouldKeepDefaults()
    {
        var source = new EnhancedHttpClientOptions();

        var clone = source.Clone();

        clone.AllowCustomBaseUrls.Should().BeFalse();
        clone.CaptureRequestContent.Should().BeFalse();
        clone.MaxSuccessResponseBytes.Should().Be(0);
        clone.RequestBodySerialization.Should().Be(RequestBodySerializationMode.Default);
        clone.UrlResolution.Should().Be(UrlResolutionMode.Default);

        // 值类型属性走拷贝语义：改源不影响克隆
        source.MaxSuccessResponseBytes = 1024;
        clone.MaxSuccessResponseBytes.Should().Be(0, "值类型属性不得因浅拷贝而联动");
    }

    /// <summary>
    /// 为"全部可写属性"提供测试值；<b>新增可写属性时必须同步本方法</b>（否则用例抛异常，形成编译期外的第二道守卫）。
    /// </summary>
    private static object? CreateValue(Type propertyType, string propertyName)
    {
        if (propertyType == typeof(bool)) return true;
        if (propertyType == typeof(int?)) return 1234;
        if (propertyType == typeof(long)) return 4321L;
        if (propertyType == typeof(RequestBodySerializationMode)) return RequestBodySerializationMode.Buffered;
        if (propertyType == typeof(UrlResolutionMode)) return UrlResolutionMode.Rfc3986;
        if (propertyType == typeof(Version)) return new Version(2, 0);
        if (propertyType == typeof(System.Net.Http.HttpVersionPolicy?))
            return System.Net.Http.HttpVersionPolicy.RequestVersionOrLower;
        if (propertyType == typeof(Dictionary<string, object?>))
            return new Dictionary<string, object?> { ["probe"] = 1 };
        if (propertyType == typeof(ILogger)) return NullLogger.Instance;
        if (propertyType == typeof(IEnumerable<IHttpRequestInterceptor>))
            return new[] { Mock.Of<IHttpRequestInterceptor>() };
        if (propertyType == typeof(IEnumerable<IHttpResponseInterceptor>))
            return new[] { Mock.Of<IHttpResponseInterceptor>() };
        if (propertyType == typeof(ISensitiveDataMasker)) return Mock.Of<ISensitiveDataMasker>();
        if (propertyType == typeof(IAppAccessAuthorizer)) return Mock.Of<IAppAccessAuthorizer>();
        if (propertyType == typeof(IExceptionRedactor)) return Mock.Of<IExceptionRedactor>();
        if (propertyType == typeof(JavaScriptEncoder)) return JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
#if NET8_0_OR_GREATER
        if (propertyType == typeof(IJsonTypeInfoResolver)) return new DefaultJsonTypeInfoResolver();
#endif

        throw new InvalidOperationException(
            $"属性 {propertyName}（{propertyType}）未在 CreateValue 中登记：新增可写属性时必须同步 EnhancedHttpClientOptions.Clone() 与本测试。");
    }
}
