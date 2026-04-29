using Mud.HttpUtils;

namespace Mud.HttpUtils.Client.Tests;

public class ApiExceptionTests
{
    [Fact]
    public void ApiException_WithStatusCodeAndContent_SetsProperties()
    {
        var exception = new ApiException(HttpStatusCode.BadRequest, "bad request");

        exception.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        exception.Content.Should().Be("bad request");
        exception.RequestUri.Should().BeNull();
    }

    [Fact]
    public void ApiException_WithStatusCodeContentAndUri_SetsProperties()
    {
        var exception = new ApiException(HttpStatusCode.NotFound, "not found", "https://api.example.com/users/1");

        exception.StatusCode.Should().Be(HttpStatusCode.NotFound);
        exception.Content.Should().Be("not found");
        exception.RequestUri.Should().Be("https://api.example.com/users/1");
    }

    [Fact]
    public void ApiException_WithInnerException_PreservesInnerException()
    {
        var inner = new InvalidOperationException("inner error");
        var exception = new ApiException(HttpStatusCode.InternalServerError, "server error", inner);

        exception.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        exception.Content.Should().Be("server error");
        exception.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void ApiException_WithAllParameters_SetsProperties()
    {
        var inner = new InvalidOperationException("inner");
        var exception = new ApiException(HttpStatusCode.BadGateway, "bad gateway", "https://api.example.com", inner);

        exception.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        exception.Content.Should().Be("bad gateway");
        exception.RequestUri.Should().Be("https://api.example.com");
        exception.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void ApiException_MessageContainsStatusCode()
    {
        var exception = new ApiException(HttpStatusCode.NotFound, "not found");

        exception.Message.Should().Contain("404");
        exception.Message.Should().Contain("NotFound");
    }

    [Fact]
    public void ApiException_MessageWithUri_ContainsUri()
    {
        var exception = new ApiException(HttpStatusCode.NotFound, "not found", "https://api.example.com/users/1");

        exception.Message.Should().Contain("https://api.example.com/users/1");
    }

    [Fact]
    public void ApiException_NullContent_Allowed()
    {
        var exception = new ApiException(HttpStatusCode.InternalServerError, (string?)null);

        exception.Content.Should().BeNull();
        exception.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public void ApiException_TryDeserializeContent_ValidJson_ReturnsTrue()
    {
        var exception = new ApiException(HttpStatusCode.BadRequest, "{\"error\":\"invalid\"}");

        var result = exception.TryDeserializeContent(
            json => System.Text.Json.JsonSerializer.Deserialize<JsonError>(json),
            out var deserialized);

        result.Should().BeTrue();
        deserialized.Should().NotBeNull();
        deserialized!.Error.Should().Be("invalid");
    }

    [Fact]
    public void ApiException_TryDeserializeContent_EmptyContent_ReturnsFalse()
    {
        var exception = new ApiException(HttpStatusCode.BadRequest, "");

        var result = exception.TryDeserializeContent(
            json => System.Text.Json.JsonSerializer.Deserialize<JsonError>(json),
            out var deserialized);

        result.Should().BeFalse();
        deserialized.Should().BeNull();
    }

    [Fact]
    public void ApiException_TryDeserializeContent_NullContent_ReturnsFalse()
    {
        var exception = new ApiException(HttpStatusCode.BadRequest, (string?)null);

        var result = exception.TryDeserializeContent(
            json => System.Text.Json.JsonSerializer.Deserialize<JsonError>(json),
            out var deserialized);

        result.Should().BeFalse();
        deserialized.Should().BeNull();
    }

    [Fact]
    public void ApiException_TryDeserializeContent_InvalidJson_ReturnsFalse()
    {
        var exception = new ApiException(HttpStatusCode.BadRequest, "not json");

        var result = exception.TryDeserializeContent(
            json => System.Text.Json.JsonSerializer.Deserialize<JsonError>(json),
            out var deserialized);

        result.Should().BeFalse();
        deserialized.Should().BeNull();
    }

    [Fact]
    public void ApiException_TryDeserializeContent_NullDeserialize_ReturnsFalse()
    {
        var exception = new ApiException(HttpStatusCode.BadRequest, "{\"error\":\"invalid\"}");

        var result = exception.TryDeserializeContent<JsonError>(null!, out var deserialized);

        result.Should().BeFalse();
        deserialized.Should().BeNull();
    }

    [Fact]
    public void ApiException_DeserializeContent_ValidJson_ReturnsDeserialized()
    {
        var exception = new ApiException(HttpStatusCode.BadRequest, "{\"error\":\"invalid\"}");

        var result = exception.DeserializeContent(
            json => System.Text.Json.JsonSerializer.Deserialize<JsonError>(json));

        result.Should().NotBeNull();
        result!.Error.Should().Be("invalid");
    }

    [Fact]
    public void ApiException_DeserializeContent_EmptyContent_ThrowsInvalidOperationException()
    {
        var exception = new ApiException(HttpStatusCode.BadRequest, "");

        var act = () => exception.DeserializeContent(
            json => System.Text.Json.JsonSerializer.Deserialize<JsonError>(json));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ApiException_DeserializeContent_NullDeserialize_ThrowsArgumentNullException()
    {
        var exception = new ApiException(HttpStatusCode.BadRequest, "content");

        var act = () => exception.DeserializeContent<JsonError>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private class JsonError
    {
        [System.Text.Json.Serialization.JsonPropertyName("error")]
        public string Error { get; set; } = "";
    }
}
