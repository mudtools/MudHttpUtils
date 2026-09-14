// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// <see cref="EnhancedHttpClientOptions"/> 的浅拷贝工具。
/// </summary>
/// <remarks>
/// <para>
/// CFG-01：DI 路径（<c>CreateEnhancedClient</c>）以 <see cref="Microsoft.Extensions.Options.IOptions{T}"/>
/// 中已注册的编程式配置为基线构造「每客户端独立实例」，
/// 避免就地修改共享的 <c>IOptions&lt;T&gt;.Value</c> 单例（多客户端串味）。
/// </para>
/// <para>
/// 仅拷贝「运行时消费属性」；接口/委托类属性（<c>Logger</c> / <c>RequestInterceptors</c> /
/// <c>ResponseInterceptors</c> / <c>SensitiveDataMasker</c>）由 DI 解析后覆盖，此处不拷贝。
/// </para>
/// <para>
/// <b>维护约束</b>：<see cref="EnhancedHttpClientOptions"/> 新增可写属性时，
/// 必须同步更新本方法，否则 <c>EnhancedHttpClientOptionsCloner_CoversAllWritableProperties</c> 测试将失败。</para>
/// </remarks>
internal static class EnhancedHttpClientOptionsCloner
{
    /// <summary>
    /// 克隆 <paramref name="source"/>；<paramref name="source"/> 为 <c>null</c> 时返回默认实例。
    /// </summary>
    public static EnhancedHttpClientOptions Clone(EnhancedHttpClientOptions? source)
    {
        if (source is null)
            return new EnhancedHttpClientOptions();

        return new EnhancedHttpClientOptions
        {
            // 运行时消费属性（EnhancedHttpClient 构造函数读取）
            RequestBodySerialization = source.RequestBodySerialization,
            ExceptionRedactor = source.ExceptionRedactor,
            MaxExceptionContentLength = source.MaxExceptionContentLength,
            CaptureRequestContent = source.CaptureRequestContent,
            UrlResolution = source.UrlResolution,
            MaxSuccessResponseBytes = source.MaxSuccessResponseBytes,
            HttpRequestMessageOptions = source.HttpRequestMessageOptions,
            AllowCustomBaseUrls = source.AllowCustomBaseUrls,
#if NET6_0_OR_GREATER
            HttpVersion = source.HttpVersion,
            HttpVersionPolicy = source.HttpVersionPolicy,
#endif
#if NET8_0_OR_GREATER
            JsonTypeInfoResolver = source.JsonTypeInfoResolver,
#endif
            // 接口/委托属性由 DI 覆盖，此处不拷贝：
            // Logger / RequestInterceptors / ResponseInterceptors / SensitiveDataMasker
        };
    }
}
