using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Mud.HttpUtils.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class HttpClientBenchmarks
{
    private TestServer _server = null!;
    private IEnhancedHttpClient _httpClient = null!;
    private HttpClient _rawHttpClient = null!;
    private JsonSerializerOptions _jsonOptions = null!;
    private System.Xml.Serialization.XmlSerializer _cachedXmlSerializer = null!;
    private TestModels.SampleUser _sampleUser = null!;
    private string _sampleJson = null!;
    private string _sampleXml = null!;

    [GlobalSetup]
    public void Setup()
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
                    endpoints.MapGet("/api/users/{id:int}", async context =>
                    {
                        var id = context.Request.RouteValues["id"]?.ToString();
                        var user = new { Id = int.Parse(id!), Name = $"User {id}", Email = $"user{id}@example.com" };
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(user));
                    });

                    endpoints.MapPost("/api/users", async context =>
                    {
                        var user = await context.Request.ReadFromJsonAsync<TestModels.SampleUser>();
                        context.Response.StatusCode = 201;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(user));
                    });

                    endpoints.MapGet("/api/users", async context =>
                    {
                        var users = Enumerable.Range(1, 10).Select(i => new { Id = i, Name = $"User {i}", Email = $"user{i}@example.com" });
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(users));
                    });

                    endpoints.MapPost("/api/users/xml", async context =>
                    {
                        using var reader = new StreamReader(context.Request.Body);
                        var xml = await reader.ReadToEndAsync();
                        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(TestModels.SampleUser));
                        using var stringReader = new StringReader(xml);
                        var user = (TestModels.SampleUser?)serializer.Deserialize(stringReader);
                        context.Response.StatusCode = 201;
                        context.Response.ContentType = "application/xml";
                        var ns = new System.Xml.Serialization.XmlSerializerNamespaces();
                        ns.Add("", "");
                        serializer.Serialize(context.Response.Body, user, ns);
                    });
                });
            }));

        _rawHttpClient = _server.CreateClient();
        _httpClient = new DirectEnhancedHttpClient(_rawHttpClient);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        _cachedXmlSerializer = new System.Xml.Serialization.XmlSerializer(typeof(TestModels.SampleUser));

        _sampleUser = new TestModels.SampleUser { Id = 1, Name = "Benchmark User", Email = "benchmark@example.com" };
        _sampleJson = JsonSerializer.Serialize(_sampleUser, _jsonOptions);

        var xmlSerializer = new System.Xml.Serialization.XmlSerializer(typeof(TestModels.SampleUser));
        using var stringWriter = new StringWriter();
        var ns = new System.Xml.Serialization.XmlSerializerNamespaces();
        ns.Add("", "");
        xmlSerializer.Serialize(stringWriter, _sampleUser, ns);
        _sampleXml = stringWriter.ToString();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _rawHttpClient.Dispose();
        _server.Dispose();
    }

    [Benchmark(Description = "JSON 反序列化 - 单对象")]
    public TestModels.SampleUser? JsonDeserializeSingle()
    {
        return JsonSerializer.Deserialize<TestModels.SampleUser>(_sampleJson, _jsonOptions);
    }

    [Benchmark(Description = "JSON 序列化 - 单对象")]
    public string JsonSerializeSingle()
    {
        return JsonSerializer.Serialize(_sampleUser, _jsonOptions);
    }

    [Benchmark(Description = "XML 反序列化 - 缓存序列化器")]
    public TestModels.SampleUser? XmlDeserializeCached()
    {
        using var reader = new StringReader(_sampleXml);
        return (TestModels.SampleUser?)_cachedXmlSerializer.Deserialize(reader);
    }

    [Benchmark(Description = "XML 反序列化 - 每次新建序列化器")]
    public TestModels.SampleUser? XmlDeserializeUncached()
    {
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(TestModels.SampleUser));
        using var reader = new StringReader(_sampleXml);
        return (TestModels.SampleUser?)serializer.Deserialize(reader);
    }

    [Benchmark(Description = "HTTP GET - 通过 TestServer")]
    public async Task<string> HttpGetAsync()
    {
        var response = await _rawHttpClient.GetAsync("/api/users/1");
        return await response.Content.ReadAsStringAsync();
    }

    [Benchmark(Description = "HTTP POST JSON - 通过 TestServer")]
    public async Task<string> HttpPostJsonAsync()
    {
        var content = new StringContent(_sampleJson, System.Text.Encoding.UTF8, "application/json");
        var response = await _rawHttpClient.PostAsync("/api/users", content);
        return await response.Content.ReadAsStringAsync();
    }

    [Benchmark(Description = "HTTP GET - 通过 EnhancedHttpClient")]
    public async Task<TestModels.SampleUser?> EnhancedHttpGetAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/1");
        var response = await _httpClient.SendRawAsync(request);
        var rawContent = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TestModels.SampleUser>(rawContent, _jsonOptions);
    }

    [Benchmark(Description = "HTTP GET 列表 - 通过 TestServer")]
    public async Task<string> HttpGetListAsync()
    {
        var response = await _rawHttpClient.GetAsync("/api/users");
        return await response.Content.ReadAsStringAsync();
    }
}

public class TestModels
{
    public class SampleUser
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
    }
}
