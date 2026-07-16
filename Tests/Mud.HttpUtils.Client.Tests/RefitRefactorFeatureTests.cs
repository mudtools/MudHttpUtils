// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// Refit 对标重构方案新增功能的单元测试（Phase 0-7 覆盖）。
/// </summary>
public class RefitRefactorFeatureTests
{
    #region Phase 2: ApiException 增强

    [Fact]
    public void ApiException_RequestContent_CanBeSetAndGet()
    {
        // T2.3: RequestContent 属性在基类上，有 getter 和 setter
        var ex = new ApiException(HttpStatusCode.BadRequest, "error");

        ex.RequestContent = "{\"name\":\"test\"}";
        ex.RequestContent.Should().Be("{\"name\":\"test\"}");
        ex.HasRequestContent.Should().BeTrue();
    }

    [Fact]
    public void ApiException_RequestContent_DefaultNull()
    {
        var ex = new ApiException(HttpStatusCode.BadRequest, "error");

        ex.RequestContent.Should().BeNull();
        ex.HasRequestContent.Should().BeFalse();
    }

    [Fact]
    public void ApiException_Content_Setter_Works()
    {
        // T2.1: Content 补充 setter 以支持 ExceptionRedactor
        var ex = new ApiException(HttpStatusCode.BadRequest, "sensitive data");

        ex.Content = null;
        ex.Content.Should().BeNull();
    }

    [Fact]
    public void ApiException_RequestUri_Setter_Works()
    {
        // T2.1: RequestUri 补充 setter
        var ex = new ApiException(HttpStatusCode.BadRequest, "error", "https://api.example.com");

        ex.RequestUri = null;
        ex.RequestUri.Should().BeNull();
    }

    #endregion

    #region Phase 2: IExceptionRedactor + DelegateExceptionRedactor

    [Fact]
    public void DelegateExceptionRedactor_RedactsContent()
    {
        // T2.1: DelegateExceptionRedactor 可以擦除敏感数据
        var redactor = new DelegateExceptionRedactor(ex =>
        {
            ex.Content = null;
            ex.RequestContent = null;
        });

        var ex = new ApiException(HttpStatusCode.Unauthorized, "token=abc123", "https://api.example.com")
        {
            RequestContent = "{\"password\":\"secret\"}"
        };

        redactor.Redact(ex);

        ex.Content.Should().BeNull();
        ex.RequestContent.Should().BeNull();
    }

    #endregion

    #region Phase 2: ApiResponseExtensions

    [Fact]
    public void ApiResponseExtensions_GetHeader_ReturnsFirstValue()
    {
        // T2.5: GetHeader 扩展方法
        var headers = new Dictionary<string, List<string>>
        {
            ["X-Request-Id"] = new List<string> { "abc-123", "def-456" }
        };

        var response = new Response<string>(
            HttpStatusCode.OK,
            "data",
            null,
            headers,
            null);

        var headerValue = response.GetHeader("X-Request-Id");
        headerValue.Should().Be("abc-123");
    }

    [Fact]
    public void ApiResponseExtensions_GetHeader_ReturnsNull_WhenMissing()
    {
        var headers = new Dictionary<string, List<string>>();

        var response = new Response<string>(
            HttpStatusCode.OK,
            "data",
            null,
            headers,
            null);

        var headerValue = response.GetHeader("X-Non-Existent");
        headerValue.Should().BeNull();
    }

    [Fact]
    public void ApiResponseExtensions_EnsureSuccessStatusCode_ThrowsOnNon2xx()
    {
        // T2.5: EnsureSuccessStatusCode 在非 2xx 时抛出 ApiException
        var response = new Response<string>(
            HttpStatusCode.InternalServerError,
            null,
            "error content",
            null,
            null);

        var act = () => response.EnsureSuccessStatusCode();

        var ex = act.Should().Throw<ApiException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        ex.Which.Content.Should().Be("error content");
    }

    [Fact]
    public void ApiResponseExtensions_EnsureSuccessStatusCode_ReturnsOn2xx()
    {
        var response = new Response<string>(
            HttpStatusCode.OK,
            "data",
            null,
            null,
            null);

        var result = response.EnsureSuccessStatusCode();
        result.Should().BeSameAs(response);
    }

    #endregion

    #region Phase 2: ProblemDetailsJsonContext

    [Fact]
    public void ProblemDetailsJsonContext_CanDeserializeProblemDetails()
    {
        // T2.4: ProblemDetailsJsonContext 可在 AOT 下反序列化
        var json = "{\"type\":\"https://example.com/probs/out-of-credit\",\"title\":\"You do not have enough credit\",\"status\":400,\"detail\":\"Your current balance is 30, but that costs 50.\",\"instance\":\"/account/12345\",\"errors\":{\"name\":[\"The name field is required.\"]},\"extensions\":{\"traceId\":\"abc-123\"}}";

        var problemDetails = JsonSerializer.Deserialize(json, ProblemDetailsJsonContext.Default.ProblemDetails);

        problemDetails.Should().NotBeNull();
        problemDetails!.Status.Should().Be(400);
        problemDetails.Title.Should().Be("You do not have enough credit");
        problemDetails.Errors.Should().NotBeNull();
        problemDetails.Errors!["name"].Should().Contain("The name field is required.");
    }

    #endregion

    #region Phase 3: CollectionFormat

    [Fact]
    public void CollectionFormat_Tsv_Exists()
    {
        // T3.3: Tsv 枚举值存在
        var format = CollectionFormat.Tsv;
        ((int)format).Should().Be(4);
    }

    [Fact]
    public void CollectionFormat_Pipes_Exists()
    {
        // T3.3: Pipes 枚举值存在
        var format = CollectionFormat.Pipes;
        ((int)format).Should().Be(5);
    }

    #endregion

    #region Phase 3: UrlResolutionMode

    [Fact]
    public void UrlResolutionMode_Default_IsZero()
    {
        // T3.1: UrlResolutionMode 枚举
        var mode = UrlResolutionMode.Default;
        ((int)mode).Should().Be(0);
    }

    [Fact]
    public void UrlResolutionMode_Rfc3986_IsOne()
    {
        var mode = UrlResolutionMode.Rfc3986;
        ((int)mode).Should().Be(1);
    }

    #endregion

    #region Phase 1: RequestBodySerializationMode

    [Fact]
    public void RequestBodySerializationMode_Default_IsZero()
    {
        // T1.2: RequestBodySerializationMode 枚举
        var mode = RequestBodySerializationMode.Default;
        ((int)mode).Should().Be(0);
    }

    [Fact]
    public void RequestBodySerializationMode_Buffered_IsOne()
    {
        var mode = RequestBodySerializationMode.Buffered;
        ((int)mode).Should().Be(1);
    }

    [Fact]
    public void RequestBodySerializationMode_Streamed_IsTwo()
    {
        var mode = RequestBodySerializationMode.Streamed;
        ((int)mode).Should().Be(2);
    }

    #endregion

    #region Phase 1: StreamingContentFormat

    [Fact]
    public void StreamingContentFormat_JsonArray_IsZero()
    {
        // T1.1: StreamingContentFormat 枚举
        var format = StreamingContentFormat.JsonArray;
        ((int)format).Should().Be(0);
    }

    [Fact]
    public void StreamingContentFormat_JsonLines_IsOne()
    {
        var format = StreamingContentFormat.JsonLines;
        ((int)format).Should().Be(1);
    }

    #endregion

    #region Phase 3: EnhancedHttpClientOptions 新增属性

    [Fact]
    public void EnhancedHttpClientOptions_DefaultValues_AreBackwardCompatible()
    {
        // T3.1/T3.4/T3.5: 默认值向后兼容
        var options = new EnhancedHttpClientOptions();

        options.RequestBodySerialization.Should().Be(RequestBodySerializationMode.Default);
        options.UrlResolution.Should().Be(UrlResolutionMode.Default);
        options.ExceptionRedactor.Should().BeNull();
        options.MaxExceptionContentLength.Should().BeNull();
        options.CaptureRequestContent.Should().BeFalse();
        options.HttpRequestMessageOptions.Should().BeNull();
        // HttpVersion 仅在 NET6_0_OR_GREATER 下定义，测试项目 net8.0 可用但 IDE 可能用 netstandard2.0 分析
    }

    [Fact]
    public void EnhancedHttpClientOptions_CanSet_NewProperties()
    {
        var options = new EnhancedHttpClientOptions
        {
            RequestBodySerialization = RequestBodySerializationMode.Buffered,
            UrlResolution = UrlResolutionMode.Rfc3986,
            MaxExceptionContentLength = 1024,
            CaptureRequestContent = true,
            HttpRequestMessageOptions = new() { ["key"] = "value" }
        };

        options.RequestBodySerialization.Should().Be(RequestBodySerializationMode.Buffered);
        options.UrlResolution.Should().Be(UrlResolutionMode.Rfc3986);
        options.MaxExceptionContentLength.Should().Be(1024);
        options.CaptureRequestContent.Should().BeTrue();
        options.HttpRequestMessageOptions.Should().ContainKey("key");
    }

    #endregion

    #region Phase 0: GeneratedClientOptions

    [Fact]
    public void GeneratedClientOptions_GeneratedOnlyMode_DefaultIsNull()
    {
        // T0.3: 实例级 GeneratedOnlyMode 覆盖
        var options = new GeneratedClientOptions();
        options.GeneratedOnlyMode.Should().BeNull();
    }

    [Fact]
    public void GeneratedClientOptions_CanSet_ExceptionRedactor_MaxExceptionContentLength_CaptureRequestContent()
    {
        // T2.1/T2.2/T2.3: GeneratedClientOptions 携带异常相关配置
        var options = new GeneratedClientOptions
        {
            ExceptionRedactor = new DelegateExceptionRedactor(_ => { }),
            MaxExceptionContentLength = 2048,
            CaptureRequestContent = true
        };

        options.ExceptionRedactor.Should().NotBeNull();
        options.MaxExceptionContentLength.Should().Be(2048);
        options.CaptureRequestContent.Should().BeTrue();
    }

    #endregion

    #region Phase 4: MudHttpClientNamingPresets

    [Fact]
    public void MudHttpClientNamingPresets_CamelCase_ReturnsOptions()
    {
        // T4.3: 命名策略工厂方法
        var options = MudHttpClientNamingPresets.CamelCase();
        options.Should().NotBeNull();
        options.PropertyNamingPolicy.Should().NotBeNull();
    }

    [Fact]
    public void MudHttpClientNamingPresets_SnakeCase_ReturnsOptions()
    {
        var options = MudHttpClientNamingPresets.SnakeCase();
        options.Should().NotBeNull();
        options.PropertyNamingPolicy.Should().NotBeNull();
    }

    [Fact]
    public void MudHttpClientNamingPresets_KebabCase_ReturnsOptions()
    {
        var options = MudHttpClientNamingPresets.KebabCase();
        options.Should().NotBeNull();
        options.PropertyNamingPolicy.Should().NotBeNull();
    }

    #endregion

    #region Phase 4: PushStreamContent

    [Fact]
    public void PushStreamContent_CanBeCreated()
    {
        // T4.4: PushStreamContent 存在
        using var content = new PushStreamContent(_ => { }, "application/json");
        content.Should().NotBeNull();
        content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    #endregion

    #region Phase 4: CamelCaseStringEnumConverter

    [Fact]
    public void CamelCaseStringEnumConverter_CanConvertEnum()
    {
        // T4.5: CamelCaseStringEnumConverter
        var converter = new CamelCaseStringEnumConverter();
        converter.CanConvert(typeof(TestEnum)).Should().BeTrue();
    }

    private enum TestEnum { None, FirstValue, SecondValue }

    #endregion

    #region Phase 4: SeparatedCaseJsonNamingPolicy

    [Fact]
    public void SeparatedCaseJsonNamingPolicy_Snake_ConvertsToSnakeCase()
    {
        var policy = SeparatedCaseJsonNamingPolicy.Snake;
        policy.ConvertName("UserName").Should().Be("user_name");
    }

    [Fact]
    public void SeparatedCaseJsonNamingPolicy_Kebab_ConvertsToKebabCase()
    {
        var policy = SeparatedCaseJsonNamingPolicy.Kebab;
        policy.ConvertName("UserName").Should().Be("user-name");
    }

    #endregion

    #region Phase 4: URL 参数键格式化器

    [Fact]
    public void CamelCaseUrlParameterKeyFormatter_FormatsCorrectly()
    {
        var formatter = new CamelCaseUrlParameterKeyFormatter();
        formatter.Format("user_name").Should().Be("userName");
    }

    [Fact]
    public void SnakeCaseUrlParameterKeyFormatter_FormatsCorrectly()
    {
        var formatter = new SnakeCaseUrlParameterKeyFormatter();
        formatter.Format("userName").Should().Be("user_name");
    }

    [Fact]
    public void KebabCaseUrlParameterKeyFormatter_FormatsCorrectly()
    {
        var formatter = new KebabCaseUrlParameterKeyFormatter();
        formatter.Format("userName").Should().Be("user-name");
    }

    #endregion
}
