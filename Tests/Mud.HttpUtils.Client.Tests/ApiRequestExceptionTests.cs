using System.Net;
using FluentAssertions;
using Mud.HttpUtils;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// Phase 3.3 验证：ApiRequestException 运输层异常分离。
/// </summary>
/// <remarks>
/// v1.5 修正：ApiRequestException 继承 ApiException（非 ApiExceptionBase），
/// 因此现有 catch (ApiException) 仍能捕获运输层异常。
/// </remarks>
public class ApiRequestExceptionTests
{
    // ─────────────────────────────────────────────────────────
    // 构造函数与属性
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithMessage_SetsMessage()
    {
        var ex = new ApiRequestException("网络请求失败");

        ex.Message.Should().Be("网络请求失败");
    }

    [Fact]
    public void Constructor_WithTransportException_SetsInnerException()
    {
        var inner = new HttpRequestException("connection refused");
        var ex = new ApiRequestException("请求失败", inner);

        ex.TransportException.Should().BeSameAs(inner);
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void Constructor_WithIsTimeout_SetsIsTimeoutTrue()
    {
        var inner = new TaskCanceledException("timeout");
        var ex = new ApiRequestException("请求超时", inner, isTimeout: true);

        ex.IsTimeout.Should().BeTrue();
        ex.IsCancellation.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithIsCancellation_SetsIsCancellationTrue()
    {
        var inner = new OperationCanceledException("cancelled");
        var ex = new ApiRequestException("请求被取消", inner, isCancellation: true);

        ex.IsCancellation.Should().BeTrue();
        ex.IsTimeout.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithRequestUri_SetsRequestUri()
    {
        var ex = new ApiRequestException("失败", requestUri: "https://api.example.com/users");

        ex.RequestUri.Should().Be("https://api.example.com/users");
    }

    [Fact]
    public void Constructor_DefaultValues_IsTimeoutAndIsCancellationAreFalse()
    {
        var ex = new ApiRequestException("失败");

        ex.IsTimeout.Should().BeFalse();
        ex.IsCancellation.Should().BeFalse();
        ex.TransportException.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────
    // 继承关系验证（v1.5 关键：ApiRequestException : ApiException）
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void ApiRequestException_IsAssignableTo_ApiException()
    {
        var ex = new ApiRequestException("运输层失败");

        // v1.5 关键验证：ApiRequestException 继承 ApiException
        // 现有 catch (ApiException) 仍能捕获运输层异常
        ex.Should().BeAssignableTo<ApiException>();
    }

    [Fact]
    public void ApiRequestException_IsAssignableTo_Exception()
    {
        var ex = new ApiRequestException("运输层失败");

        ex.Should().BeAssignableTo<Exception>();
    }

    [Fact]
    public void CatchApiException_CanCatchApiRequestException()
    {
        // 模拟 EnhancedHttpClient.ExecuteHttpRequestCoreAsync 中超时场景
        // 抛出 ApiRequestException，验证 catch (ApiException) 能捕获
        ApiException? caught = null;
        try
        {
            throw new ApiRequestException("请求超时", new TaskCanceledException(), isTimeout: true);
        }
        catch (ApiException ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull();
        caught.Should().BeOfType<ApiRequestException>();
        ((ApiRequestException)caught!).IsTimeout.Should().BeTrue();
    }

    [Fact]
    public void CatchApiException_CanCatchApiRequestException_WithIsCancellation()
    {
        ApiException? caught = null;
        try
        {
            throw new ApiRequestException("请求被取消", new OperationCanceledException(), isCancellation: true);
        }
        catch (ApiException ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull();
        caught.Should().BeOfType<ApiRequestException>();
        ((ApiRequestException)caught!).IsCancellation.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────
    // 与 ApiException 的语义区分
    // ─────────────────────────────────────────────────────────

    [Fact]
    public void ApiException_HasStatusCode_ApiRequestException_DoesNotRequireStatusCode()
    {
        // ApiException（HTTP 错误响应）必须提供 StatusCode
        var apiEx = new ApiException(HttpStatusCode.InternalServerError, "server error");
        apiEx.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        // ApiRequestException（运输层失败）不要求 StatusCode
        // 通过受保护构造函数创建，StatusCode 为默认值 (default(HttpStatusCode) = 0)
        var requestEx = new ApiRequestException("network failure");
        requestEx.StatusCode.Should().Be(default(HttpStatusCode), "运输层失败无 HTTP 状态码,StatusCode 取默认值");
    }

    [Fact]
    public void ApiRequestException_TransportException_PreservesOriginalException()
    {
        var original = new HttpRequestException("DNS resolution failed");
        var ex = new ApiRequestException("DNS 解析失败", original);

        ex.TransportException.Should().BeSameAs(original);
        ex.InnerException.Should().BeSameAs(original);
    }

    [Fact]
    public void ApiRequestException_Message_ContainsRequestUri()
    {
        var ex = new ApiRequestException("请求超时", requestUri: "https://api.example.com/data");

        ex.Message.Should().Contain("请求超时");
    }
}
