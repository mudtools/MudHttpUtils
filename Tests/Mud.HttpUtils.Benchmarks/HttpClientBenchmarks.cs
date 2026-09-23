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

/// <summary>
/// M6 阶段五（§八 8.2）基准对照：为 P2 批次的三项性能改动提供「改动前 / 改动后」可比数据。
/// <list type="bullet">
///   <item>HC-16 缓存满载 <c>Set</c>：满载（触发淘汰）vs 未满载（无淘汰）基线。</item>
///   <item>HC-17 指标 tag 构造：<c>MudHttpMeter.HasListeners</c> 门控 vs 恒构造基线。</item>
///   <item>HC-15 上传进度吞吐：带进度回调（81920 新默认 / 4096 旧默认）vs 无进度快路径。</item>
/// </list>
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class M6Stage5Benchmarks
{
    private const int CacheCapacity = 1024;
    private const int UploadPayloadBytes = 4 * 1024 * 1024;

    private MemoryHttpResponseCache _fullCache = null!;
    private MemoryHttpResponseCache _warmCache = null!;
    private byte[] _uploadPayload = null!;
    private readonly ProgressRecorder _progress = new();
    private int _setCounter;

    [GlobalSetup]
    public void Setup()
    {
        _fullCache = new MemoryHttpResponseCache(CacheCapacity);
        for (var i = 0; i < CacheCapacity; i++)
        {
            _fullCache.Set($"key-{i}", i, TimeSpan.FromMinutes(10));
        }

        _warmCache = new MemoryHttpResponseCache(CacheCapacity);

        _uploadPayload = new byte[UploadPayloadBytes];
        new Random(42).NextBytes(_uploadPayload);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _fullCache.Dispose();
        _warmCache.Dispose();
    }

    /// <summary>HC-16：满载时每次 Set 都要选淘汰目标（现按 LastAccessTime 单次 O(N) 扫描）。</summary>
    [Benchmark(Description = "响应缓存 Set - 满载触发淘汰（HC-16）")]
    public void CacheSet_AtCapacity()
        => _fullCache.Set($"overflow-{++_setCounter}", _setCounter, TimeSpan.FromMinutes(10));

    /// <summary>HC-16 对照：未满载（无淘汰分支）时的 Set 基线。</summary>
    [Benchmark(Description = "响应缓存 Set - 未满载（HC-16 对照基线）")]
    public void CacheSet_UnderCapacity()
        => _warmCache.Set($"fresh-{++_setCounter}", _setCounter, TimeSpan.FromMinutes(10));

    /// <summary>HC-17：零监听时经 HasListeners 门控短路，跳过 tags 数组构造。</summary>
    [Benchmark(Description = "指标 tag 构造 - HasListeners 门控（HC-17）")]
    public int MetricTags_Gated()
    {
        if (!MudHttpMeter.HasListeners)
        {
            return 0;
        }

        return BuildTags().Length;
    }

    /// <summary>HC-17 对照：不门控，恒构造 tags 数组（HC-17 之前的写入路径行为）。</summary>
    [Benchmark(Description = "指标 tag 构造 - 恒构造（HC-17 对照基线）")]
    public int MetricTags_Ungated() => BuildTags().Length;

    /// <summary>HC-15：带进度回调的上传路径（节流复制 + 81920 新默认缓冲）。</summary>
    [Benchmark(Description = "上传吞吐 - 带进度回调 81920（HC-15）")]
    public async Task Upload_WithProgress_DefaultBuffer()
    {
        using var inner = new ByteArrayContent(_uploadPayload);
        using var content = new ProgressableStreamContent(inner, _progress);
        await content.CopyToAsync(Stream.Null);
    }

    /// <summary>HC-15 对照：带进度回调但沿用旧默认缓冲 4096。</summary>
    [Benchmark(Description = "上传吞吐 - 带进度回调 4096（HC-15 旧默认对照）")]
    public async Task Upload_WithProgress_LegacyBuffer()
    {
        using var inner = new ByteArrayContent(_uploadPayload);
        using var content = new ProgressableStreamContent(inner, _progress, 4096);
        await content.CopyToAsync(Stream.Null);
    }

    /// <summary>HC-15 对照：无进度回调走 <c>Stream.CopyToAsync</c> 快路径。</summary>
    [Benchmark(Description = "上传吞吐 - 无进度回调快路径（HC-15 对照）")]
    public async Task Upload_NoProgress_FastPath()
    {
        using var inner = new ByteArrayContent(_uploadPayload);
        using var content = new ProgressableStreamContent(inner, progress: null);
        await content.CopyToAsync(Stream.Null);
    }

    private static KeyValuePair<string, object?>[] BuildTags() =>
    [
        new("http.request.method", "GET"),
        new("url.scheme", "https"),
        new("server.address", "api.example.com"),
        new("http.response.status_code", 200),
    ];

    private sealed class ProgressRecorder : IProgress<long>
    {
        public long Last;

        public void Report(long value) => Last = value;
    }
}
