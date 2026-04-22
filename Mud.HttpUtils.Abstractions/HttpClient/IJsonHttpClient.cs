namespace Mud.HttpUtils;

public interface IJsonHttpClient : IBaseHttpClient
{
    Task<TResult?> GetAsync<TResult>(string requestUri, CancellationToken cancellationToken = default);

    Task<TResult?> PostAsJsonAsync<TRequest, TResult>(string requestUri, TRequest requestData, CancellationToken cancellationToken = default);

    Task<TResult?> PutAsJsonAsync<TRequest, TResult>(string requestUri, TRequest requestData, CancellationToken cancellationToken = default);
}
