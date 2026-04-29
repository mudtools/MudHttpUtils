using Mud.HttpUtils;

namespace Mud.HttpUtils.Client.Tests;

public class ResponseTests
{
    [Fact]
    public void Response_SuccessResponse_SetsPropertiesCorrectly()
    {
        var headers = new Dictionary<string, List<string>>
        {
            { "Content-Type", new List<string> { "application/json" } }
        };

        var response = new Response<string>(HttpStatusCode.OK, "hello", "{\"message\":\"hello\"}", headers);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Should().Be("hello");
        response.RawContent.Should().Be("{\"message\":\"hello\"}");
        response.ErrorContent.Should().BeNull();
        response.ResponseHeaders.Should().BeSameAs(headers);
    }

    [Fact]
    public void Response_SuccessResponse_IsSuccessStatusCodeTrue()
    {
        var response = new Response<string>(HttpStatusCode.OK, "content", null, null);
        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public void Response_CreatedResponse_IsSuccessStatusCodeTrue()
    {
        var response = new Response<string>(HttpStatusCode.Created, "content", null, null);
        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public void Response_BadRequestResponse_IsSuccessStatusCodeFalse()
    {
        var response = new Response<string>(HttpStatusCode.BadRequest, "error content", (Dictionary<string, List<string>>?)null);
        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public void Response_NotFoundResponse_IsSuccessStatusCodeFalse()
    {
        var response = new Response<string>(HttpStatusCode.NotFound, "not found", null);
        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public void Response_InternalServerErrorResponse_IsSuccessStatusCodeFalse()
    {
        var response = new Response<string>(HttpStatusCode.InternalServerError, "server error", null);
        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public void Response_ErrorResponse_SetsErrorContent()
    {
        var response = new Response<string>(HttpStatusCode.BadRequest, "error message", null);

        response.ErrorContent.Should().Be("error message");
        response.Content.Should().BeNull();
        response.RawContent.Should().Be("error message");
    }

    [Fact]
    public void Response_SuccessResponse_NullContentAllowed()
    {
        var response = new Response<string>(HttpStatusCode.OK, null, null, null);

        response.Content.Should().BeNull();
        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public void Response_GetContentOrThrow_SuccessResponse_ReturnsContent()
    {
        var response = new Response<string>(HttpStatusCode.OK, "hello", null, null);

        var result = response.GetContentOrThrow();

        result.Should().Be("hello");
    }

    [Fact]
    public void Response_GetContentOrThrow_ErrorResponse_ThrowsApiException()
    {
        var response = new Response<string>(HttpStatusCode.BadRequest, "error content", null);

        var act = () => response.GetContentOrThrow();

        act.Should().Throw<ApiException>()
            .Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void Response_ImplicitConversion_ReturnsContent()
    {
        var response = new Response<string>(HttpStatusCode.OK, "hello", null, null);

        string? result = response;

        result.Should().Be("hello");
    }

    [Fact]
    public void Response_ImplicitConversion_ErrorResponse_ReturnsDefault()
    {
        var response = new Response<string>(HttpStatusCode.BadRequest, "error", null);

        string? result = response;

        result.Should().BeNull();
    }

    [Fact]
    public void Response_WithComplexType_StoresContent()
    {
        var user = new TestUser { Name = "Alice", Age = 30 };
        var response = new Response<TestUser>(HttpStatusCode.OK, user, null, null);

        response.Content.Should().NotBeNull();
        response.Content!.Name.Should().Be("Alice");
        response.Content.Age.Should().Be(30);
    }

    private class TestUser
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }
}
