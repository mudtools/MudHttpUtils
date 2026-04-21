using System.Text.Json;

namespace Mud.HttpUtils;

public interface IBaseHttpClient
{
    Task<TResult?> SendAsync<TResult>(HttpRequestMessage request, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default);

    Task<byte[]?> DownloadAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);

    Task<FileInfo> DownloadLargeAsync(HttpRequestMessage request, string filePath, bool overwrite = true, CancellationToken cancellationToken = default);
}
