using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Mud.HttpUtils;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// Phase 3.2 验证：IApiResponse&lt;T&gt; 协变接口 + Response&lt;T&gt; 接口实现。
/// </summary>
public class IApiResponseTests
{
    // ─────────────────────────────────────────────────────────
    // 协变验证：Response<Cat> 可赋值给 IApiResponse<Animal>
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Response_CanBeAssignedTo_IApiResponse_Covariant()
    {
        var response = new Response<Cat>(HttpStatusCode.OK, new Cat { Name = "Tom" }, null, null);

        // 协变：Response<Cat> → IApiResponse<Animal>（Cat 是 Animal 的子类）
        IApiResponse<Animal> apiResponse = response;

        apiResponse.Content.Should().NotBeNull();
        apiResponse.Content!.Name.Should().Be("Tom");
    }

    [Fact]
    public void Response_CanBeAssignedTo_IApiResponse_NonGeneric()
    {
        var headers = new Dictionary<string, List<string>>
        {
            { "Content-Type", new List<string> { "application/json" } }
        };
        var response = new Response<string>(HttpStatusCode.OK, "hello", null, headers);

        IApiResponse apiResponse = response;

        apiResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        apiResponse.IsSuccessStatusCode.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────
    // IApiResponse.Headers 显式接口实现
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void IApiResponseHeaders_WithHeaders_ReturnsCachedView()
    {
        var headers = new Dictionary<string, List<string>>
        {
            { "X-Custom", new List<string> { "value1", "value2" } }
        };
        var response = new Response<string>(HttpStatusCode.OK, "content", null, headers);

        IApiResponse apiResponse = response;
        var ifaceHeaders = apiResponse.Headers;

        ifaceHeaders.Should().NotBeNull();
        ifaceHeaders.Should().ContainKey("X-Custom");
        ifaceHeaders["X-Custom"].Should().BeEquivalentTo(new[] { "value1", "value2" });
    }

    [Fact]
    public void IApiResponseHeaders_WithNullHeaders_ReturnsEmptyDictionary()
    {
        var response = new Response<string>(HttpStatusCode.OK, "content", null, null);

        IApiResponse apiResponse = response;
        var ifaceHeaders = apiResponse.Headers;

        ifaceHeaders.Should().NotBeNull();
        ifaceHeaders.Should().BeEmpty();
    }

    [Fact]
    public void IApiResponseHeaders_MultipleAccess_ReturnsSameInstance()
    {
        var headers = new Dictionary<string, List<string>>
        {
            { "X-Test", new List<string> { "v" } }
        };
        var response = new Response<string>(HttpStatusCode.OK, "content", null, headers);

        IApiResponse apiResponse = response;

        // 验证缓存：多次访问返回同一实例（零分配）
        var first = apiResponse.Headers;
        var second = apiResponse.Headers;

        first.Should().BeSameAs(second);
    }

    // ─────────────────────────────────────────────────────────
    // HasContent / ResponseMessage
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void HasContent_WithNonNullContent_ReturnsTrue()
    {
        // 使用自定义类型而非 string,避免 Response<T> 在 T=string 时
        // 成功构造函数 (HttpStatusCode, T?, string?, Dictionary?, ...) 与
        // 错误构造函数 (HttpStatusCode, string?, Dictionary?, ...) 重载歧义
        var response = new Response<TestDto>(HttpStatusCode.OK, new TestDto { Value = 42 }, null, null);
        IApiResponse<TestDto> apiResponse = response;

        apiResponse.HasContent.Should().BeTrue();
        apiResponse.Content!.Value.Should().Be(42);
    }

    [Fact]
    public void HasContent_WithNullContent_ReturnsFalse()
    {
        var response = new Response<string>(HttpStatusCode.NotFound, (string?)null, null);
        IApiResponse<string> apiResponse = response;

        apiResponse.HasContent.Should().BeFalse();
    }

    [Fact]
    public void ResponseMessage_IsAccessibleViaInterface()
    {
        var msg = new HttpResponseMessage(HttpStatusCode.OK);
        var response = new Response<string>(HttpStatusCode.OK, "hello", null, null, msg);

        IApiResponse apiResponse = response;
        apiResponse.ResponseMessage.Should().BeSameAs(msg);
    }

    [Fact]
    public void ResponseMessage_DefaultNull_WhenNotProvided()
    {
        var response = new Response<string>(HttpStatusCode.OK, "hello", null, null);

        IApiResponse apiResponse = response;
        apiResponse.ResponseMessage.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────
    // IsSuccessStatusCode via interface
    // ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.Created, true)]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public void IsSuccessStatusCode_ViaInterface_ReturnsCorrectValue(HttpStatusCode statusCode, bool expected)
    {
        var response = new Response<string>(statusCode, "content", null, null);
        IApiResponse apiResponse = response;

        apiResponse.IsSuccessStatusCode.Should().Be(expected);
    }

    // ─────────────────────────────────────────────────────────
    // 辅助类型
    // ─────────────────────────────────────────────────────────

    private class Animal
    {
        public string? Name { get; set; }
    }

    private class Cat : Animal
    {
    }

    private class TestDto
    {
        public int Value { get; set; }
    }
}
