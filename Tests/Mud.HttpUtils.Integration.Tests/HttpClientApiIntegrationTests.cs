using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace Mud.HttpUtils.Integration.Tests;

public class TestModels
{
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
    }

    public class CreateUserRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
    }

    public class QueryResult
    {
        public string? Search { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}

[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IUserApi
{
    [Get("/api/users/{id}")]
    Task<TestModels.User?> GetUserAsync([Path] int id, CancellationToken cancellationToken = default);

    [Get("/api/users")]
    Task<List<TestModels.User>?> GetUsersAsync([Query] string? search = null, [Query] int page = 1, CancellationToken cancellationToken = default);

    [Post("/api/users")]
    Task<TestModels.User?> CreateUserAsync([Body] TestModels.CreateUserRequest request, CancellationToken cancellationToken = default);

    [Put("/api/users/{id}")]
    Task<TestModels.User?> UpdateUserAsync([Path] int id, [Body] TestModels.CreateUserRequest request, CancellationToken cancellationToken = default);

    [Delete("/api/users/{id}")]
    Task DeleteUserAsync([Path] int id, CancellationToken cancellationToken = default);

    [Get("/api/users/{id}")]
    Task<Response<TestModels.User?>> GetUserWithResponseAsync([Path] int id, CancellationToken cancellationToken = default);

    [Get("/api/query-test")]
    Task<TestModels.QueryResult?> QueryTestAsync(
        [Query] string? search = null,
        [Query] int page = 1,
        [Query] int pageSize = 10,
        CancellationToken cancellationToken = default);

    [Post("/api/users")]
    Task<TestModels.User?> CreateUserWithXmlAsync(
        [Body(ContentType = "application/xml")] TestModels.CreateUserRequest request,
        CancellationToken cancellationToken = default);
}

[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IMultiClientApi
{
    [Get("/api/status")]
    Task<string?> GetStatusAsync(CancellationToken cancellationToken = default);
}

public class HttpClientApiIntegrationTests : IDisposable
{
    private readonly TestServer _server;
    private readonly HttpClient _httpClient;
    private readonly IServiceProvider _services;

    public HttpClientApiIntegrationTests()
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
                        var user = new TestModels.User
                        {
                            Id = int.Parse(id!),
                            Name = $"User {id}",
                            Email = $"user{id}@example.com"
                        };
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(user));
                    });

                    endpoints.MapGet("/api/users", async context =>
                    {
                        var search = context.Request.Query["search"].FirstOrDefault();
                        var users = new List<TestModels.User>
                        {
                            new() { Id = 1, Name = "Alice", Email = "alice@example.com" },
                            new() { Id = 2, Name = "Bob", Email = "bob@example.com" }
                        };
                        if (!string.IsNullOrEmpty(search))
                        {
                            users = users.Where(u =>
                                u.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                (u.Email?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                            ).ToList();
                        }
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(users));
                    });

                    endpoints.MapPost("/api/users", async context =>
                    {
                        var contentType = context.Request.ContentType ?? "";
                        TestModels.CreateUserRequest? request;

                        if (contentType.Contains("xml"))
                        {
                            using var reader = new StreamReader(context.Request.Body);
                            var xml = await reader.ReadToEndAsync();
                            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(TestModels.CreateUserRequest));
                            using var stringReader = new StringReader(xml);
                            request = (TestModels.CreateUserRequest?)serializer.Deserialize(stringReader);
                        }
                        else
                        {
                            request = await context.Request.ReadFromJsonAsync<TestModels.CreateUserRequest>();
                        }

                        var user = new TestModels.User
                        {
                            Id = 3,
                            Name = request?.Name ?? "Unknown",
                            Email = request?.Email
                        };
                        context.Response.StatusCode = 201;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(user));
                    });

                    endpoints.MapPut("/api/users/{id:int}", async context =>
                    {
                        var id = context.Request.RouteValues["id"]?.ToString();
                        var request = await context.Request.ReadFromJsonAsync<TestModels.CreateUserRequest>();
                        var user = new TestModels.User
                        {
                            Id = int.Parse(id!),
                            Name = request?.Name ?? "Unknown",
                            Email = request?.Email
                        };
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(user));
                    });

                    endpoints.MapDelete("/api/users/{id:int}", async context =>
                    {
                        context.Response.StatusCode = 204;
                    });

                    endpoints.MapGet("/api/query-test", async context =>
                    {
                        var search = context.Request.Query["search"].FirstOrDefault();
                        var page = int.TryParse(context.Request.Query["page"], out var p) ? p : 1;
                        var pageSize = int.TryParse(context.Request.Query["pageSize"], out var ps) ? ps : 10;
                        var result = new TestModels.QueryResult
                        {
                            Search = search,
                            Page = page,
                            PageSize = pageSize
                        };
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(result));
                    });

                    endpoints.MapGet("/api/status", async context =>
                    {
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync("OK");
                    });
                });
            }));

        _httpClient = _server.CreateClient();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<IEnhancedHttpClient>(new DirectEnhancedHttpClient(_httpClient));
        services.AddWebApiHttpClient();
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public async Task GetUser_WithValidId_ShouldReturnUser()
    {
        var api = _services.GetRequiredService<IUserApi>();
        var user = await api.GetUserAsync(1);

        user.Should().NotBeNull();
        user!.Id.Should().Be(1);
        user.Name.Should().Be("User 1");
        user.Email.Should().Be("user1@example.com");
    }

    [Fact]
    public async Task GetUsers_WithoutFilter_ShouldReturnAllUsers()
    {
        var api = _services.GetRequiredService<IUserApi>();
        var users = await api.GetUsersAsync();

        users.Should().NotBeNull();
        users!.Count.Should().Be(2);
    }

    [Fact]
    public async Task GetUsers_WithSearchFilter_ShouldReturnFilteredUsers()
    {
        var api = _services.GetRequiredService<IUserApi>();
        var users = await api.GetUsersAsync(search: "Alice");

        users.Should().NotBeNull();
        users!.Count.Should().Be(1);
        users[0].Name.Should().Be("Alice");
    }

    [Fact]
    public async Task CreateUser_WithValidData_ShouldReturnCreatedUser()
    {
        var api = _services.GetRequiredService<IUserApi>();
        var request = new TestModels.CreateUserRequest { Name = "Charlie", Email = "charlie@example.com" };
        var user = await api.CreateUserAsync(request);

        user.Should().NotBeNull();
        user!.Id.Should().Be(3);
        user.Name.Should().Be("Charlie");
        user.Email.Should().Be("charlie@example.com");
    }

    [Fact]
    public async Task UpdateUser_WithValidData_ShouldReturnUpdatedUser()
    {
        var api = _services.GetRequiredService<IUserApi>();
        var request = new TestModels.CreateUserRequest { Name = "Updated", Email = "updated@example.com" };
        var user = await api.UpdateUserAsync(1, request);

        user.Should().NotBeNull();
        user!.Id.Should().Be(1);
        user.Name.Should().Be("Updated");
    }

    [Fact]
    public async Task DeleteUser_ShouldCompleteWithoutError()
    {
        var api = _services.GetRequiredService<IUserApi>();
        await api.Invoking(a => a.DeleteUserAsync(1)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetUserWithResponse_ShouldReturnResponseWithMetadata()
    {
        var api = _services.GetRequiredService<IUserApi>();
        var response = await api.GetUserWithResponseAsync(1);

        response.Should().NotBeNull();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Should().NotBeNull();
        response.Content!.Id.Should().Be(1);
    }

    [Fact]
    public async Task QueryTest_WithAllParameters_ShouldReturnCorrectQueryInfo()
    {
        var api = _services.GetRequiredService<IUserApi>();
        var result = await api.QueryTestAsync(search: "test", page: 2, pageSize: 20);

        result.Should().NotBeNull();
        result!.Search.Should().Be("test");
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task QueryTest_WithDefaultParameters_ShouldUseDefaults()
    {
        var api = _services.GetRequiredService<IUserApi>();
        var result = await api.QueryTestAsync();

        result.Should().NotBeNull();
        result!.Page.Should().Be(1);
        result.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task CreateUserWithXml_ShouldSerializeAndDeserializeCorrectly()
    {
        var api = _services.GetRequiredService<IUserApi>();
        var request = new TestModels.CreateUserRequest { Name = "XmlUser", Email = "xml@example.com" };
        var user = await api.CreateUserWithXmlAsync(request);

        user.Should().NotBeNull();
        user!.Name.Should().Be("XmlUser");
    }

    [Fact]
    public async Task MultiClient_ShouldResolveCorrectly()
    {
        var api = _services.GetRequiredService<IMultiClientApi>();
        var status = await api.GetStatusAsync();

        status.Should().Be("OK");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _server.Dispose();
    }
}
