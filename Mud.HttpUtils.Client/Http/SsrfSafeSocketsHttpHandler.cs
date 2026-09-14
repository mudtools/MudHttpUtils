// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯用户合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

#if NET6_0_OR_GREATER
using System.Net.Sockets;

namespace Mud.HttpUtils;

/// <summary>
/// M2-#8.2：连接期 SSRF 校验处理器（仅 net6.0+；<see cref="SocketsHttpHandler.ConnectCallback"/> 与
/// <c>DnsEndPoint</c> 在 netstandard2.0 不可用）。
/// </summary>
/// <remarks>
/// <para><b>根治 DNS rebinding TOCTOU</b>：<see cref="UrlValidator.ValidateUrl"/> 在 URL 校验期解析一次 IP，
/// HttpClient 建连时可能再次解析得到不同结果。本处理器经由 <see cref="SocketsHttpHandler.ConnectCallback"/>
/// 对<b>实际建连的 IP</b> 执行 <see cref="IIpAddressPolicy.IsAllowed"/> 准入校验，校验通过后才建立 TCP 连接。</para>
/// <para>实现说明：.NET 的 <see cref="SocketsHttpHandler"/> 为 sealed，无法继承 —— 此处采用组合模式
/// （继承 <see cref="HttpMessageHandler"/>，内部持有配置了 ConnectCallback 的 SocketsHttpHandler 实例）。</para>
/// <para>注意：设置 ConnectCallback 后，DNS 解析由回调负责 —— <c>DnsEndPoint</c> 仅提供
/// <c>Host</c>/<c>Port</c>（无已解析的 <c>Address</c>）。处理器在回调内解析主机名
/// （Host 为 IP 字面量时直接使用），逐个候选 IP 校验，连接到第一个被允许的地址。</para>
/// <para>用法：</para>
/// <code>
/// services.AddMudHttpClientSsrfProtection();          // 注册 IIpAddressPolicy（默认 fail-closed）
/// services.AddMudHttpClient("api")
///         .AddMudHttpClientSsrfProtection();          // 启用连接期校验
/// </code>
/// </remarks>
public sealed class SsrfSafeSocketsHttpHandler : HttpMessageHandler
{
    private readonly IIpAddressPolicy _policy;
    private readonly SocketsHttpHandler _inner;
    private readonly HttpMessageInvoker _invoker;

    /// <summary>
    /// 创建连接期 SSRF 校验处理器。
    /// </summary>
    /// <param name="policy">IP 准入策略（如 <see cref="DefaultIpAddressPolicy"/>）。</param>
    public SsrfSafeSocketsHttpHandler(IIpAddressPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));

        _inner = new SocketsHttpHandler
        {
            ConnectCallback = ConnectAsyncCallback,
        };
        // .NET 5+ 的 HttpMessageHandler.SendAsync 为 protected —— 公开发送入口是 HttpMessageInvoker
        _invoker = new HttpMessageInvoker(_inner, disposeHandler: false);
    }

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => _invoker.SendAsync(request, cancellationToken);

    /// <inheritdoc/>
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        => throw new NotSupportedException("SsrfSafeSocketsHttpHandler 仅支持异步发送（SendAsync），不支持同步 HttpClient.Send。");

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }

    private async ValueTask<Stream> ConnectAsyncCallback(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endPoint = context.DnsEndPoint;

        // 注意：DnsEndPoint 无 Address 属性（仅 Host/Port/AddressFamily）——
        // Host 为 IP 字面量时直接使用；否则视为未解析的主机名，在回调内自行 DNS 解析（见类备注）。
        var host = endPoint.Host;
        var candidates = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

        var target = candidates.FirstOrDefault(_policy.IsAllowed);
        if (target is null)
        {
            throw new InvalidOperationException(
                $"不允许连接到目标地址: {host}（解析结果 {string.Join<IPAddress>(", ", candidates)} 均被 IP 准入策略拒绝）");
        }

        var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(target, endPoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
#endif
