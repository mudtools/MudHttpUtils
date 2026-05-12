using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;
using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Integration.Tests;

[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface ITokenTestApi
{
    [Get("/api/data")]
    Task<string?> GetDataAsync(CancellationToken cancellationToken = default);

    [Get("/api/data")]
    Task<Response<string?>> GetDataWithResponseAsync(CancellationToken cancellationToken = default);
}

public class TokenInjectionIntegrationTests : IDisposable
{
    private readonly TestServer _server;
    private readonly HttpClient _httpClient;
    private readonly IServiceProvider _services;

    public TokenInjectionIntegrationTests()
    {
        _server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/api/data", async context =>
                    {
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync("\"test-data\"");
                    });
                });
            }));

        _httpClient = _server.CreateClient();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<IEnhancedHttpClient>(new DirectEnhancedHttpClient(_httpClient));
        services.AddSingleton<ITokenManager, IntegrationTestTokenManager>();
        services.AddWebApiHttpClient();
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public async Task ApiCall_WithRegisteredClient_ShouldSucceed()
    {
        var api = _services.GetRequiredService<ITokenTestApi>();
        var result = await api.GetDataAsync();

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ApiCall_WithResponseReturnType_ShouldSucceed()
    {
        var api = _services.GetRequiredService<ITokenTestApi>();
        var response = await api.GetDataWithResponseAsync();

        response.Should().NotBeNull();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response.Content.Should().NotBeNull();
    }

    [Fact]
    public async Task TokenManager_IsRegisteredAndFunctional()
    {
        var manager = _services.GetRequiredService<ITokenManager>();
        var token = await manager.GetOrRefreshTokenAsync();

        token.Should().Be("valid-token");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _server.Dispose();
    }

    private class IntegrationTestTokenManager : ITokenManager
    {
        public Task<string> GetOrRefreshTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("valid-token");

        public Task<string> GetOrRefreshTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
            => Task.FromResult("valid-token");

        public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("valid-token");

        public Task<string> GetTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
            => Task.FromResult("valid-token");

        public Task<TokenResult> InvalidateTokenAsync(string[]? scopes = null, CancellationToken cancellationToken = default)
            => Task.FromResult(TokenResult.Empty);

        public void Dispose() { }
    }
}

public class ResilienceIntegrationTests : IDisposable
{
    private readonly TestServer _server;
    private readonly HttpClient _httpClient;
    private readonly IServiceProvider _services;
    private int _flakyRequestCount;

    public ResilienceIntegrationTests()
    {
        _flakyRequestCount = 0;

        _server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/api/data", async context =>
                    {
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync("\"test-data\"");
                    });

                    endpoints.MapGet("/api/flaky", async context =>
                    {
                        var count = Interlocked.Increment(ref _flakyRequestCount);
                        if (count <= 2)
                        {
                            context.Response.StatusCode = 500;
                            await context.Response.WriteAsync("Internal Server Error");
                            return;
                        }

                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync("\"success-after-retry\"");
                    });

                    endpoints.MapGet("/api/stable", async context =>
                    {
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync("\"stable-result\"");
                    });
                });
            }));

        _httpClient = _server.CreateClient();

        var resilienceOptions = new ResilienceOptions
        {
            Retry =
            {
                Enabled = true,
                MaxRetryAttempts = 3,
                DelayMilliseconds = 10
            }
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        var innerClient = new DirectEnhancedHttpClient(_httpClient);
        var policyProvider = new PollyResiliencePolicyProvider(resilienceOptions);
        services.AddSingleton<IEnhancedHttpClient>(new ResilientHttpClient(innerClient, policyProvider));
        services.AddWebApiHttpClient();
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Resilience_StableEndpoint_ShouldReturnResult()
    {
        var api = _services.GetRequiredService<ITokenTestApi>();
        var result = await api.GetDataAsync();

        result.Should().NotBeNull();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _server.Dispose();
    }
}
