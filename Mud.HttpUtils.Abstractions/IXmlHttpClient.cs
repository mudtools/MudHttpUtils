namespace Mud.HttpUtils;

public interface IXmlHttpClient : IBaseHttpClient
{
    Task<TResult?> SendXmlAsync<TResult>(HttpRequestMessage request, Encoding? encoding = null, CancellationToken cancellationToken = default);

    Task<TResult?> PostAsXmlAsync<TRequest, TResult>(string requestUri, TRequest requestData, Encoding? encoding = null, CancellationToken cancellationToken = default);

    Task<TResult?> PutAsXmlAsync<TRequest, TResult>(string requestUri, TRequest requestData, Encoding? encoding = null, CancellationToken cancellationToken = default);

    Task<TResult?> GetXmlAsync<TResult>(string requestUri, Encoding? encoding = null, CancellationToken cancellationToken = default);
}
