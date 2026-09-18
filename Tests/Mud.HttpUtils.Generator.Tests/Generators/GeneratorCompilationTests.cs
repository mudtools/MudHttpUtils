// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// 编译断言测试（GEN-10 / M0 A-0）：
/// 每个用例走 <see cref="GeneratorCompileAssert.RunAndAssertNoErrors"/>，
/// 断言「输入源 + 生成产物」整体无 <see cref="DiagnosticSeverity.Error"/> 级编译诊断。
/// <para>
/// 背景：本类更名为 Compilation 就应断言「可编译」，而非仅断言生成器自身诊断 + 文本 Contains 方法名
/// （旧实现从不断言 <c>outputCompilation.GetDiagnostics()</c>，生成不可编译代码时静默通过）。
/// 软断言（<c>if (generatedCode != null)</c>）已依 I-21 清除。
/// </para>
/// </summary>
public class GeneratorCompilationTests
{
    /// <summary>
    /// 模拟 <c>&lt;ImplicitUsings&gt;enable&lt;/ImplicitUsings&gt;</c> 的标准隐式 using 头。
    /// <see cref="GeneratorCompileAssert"/> 刻意不注入这些 using（用于捕捉生成代碼「缺 using」的 F2 类缺陷），
    /// 因此测试输入源需自带等价于真实消费配置的隐式 using 头。
    /// </summary>
    private const string ImplicitUsingsPreamble = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;

        """;

    /// <summary>带隐式 using 头走编译断言。</summary>
    private static Compilation Compiles(string source, string description)
        => GeneratorCompileAssert.RunAndAssertNoErrors(ImplicitUsingsPreamble + source, description: description);

    #region Basic GET Interface - Compile Assert

    [Fact]
    public void Generator_SimpleGetInterface_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync();
    }
}";

        Compiles(source, "基础 GET 接口");
    }

    #endregion

    #region POST with Body - Compile Assert

    [Fact]
    public void Generator_PostWithBody_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public class CreateUserRequest
    {
        public string Name { get; set; }
        public string Email { get; set; }
    }

    [HttpClientApi]
    public interface ITestApi
    {
        [Post(""/users"")]
        Task<string> CreateUserAsync([Body] CreateUserRequest request);
    }
}";

        // AOT（CreateUserRequest 未被 JsonSerializerContext 覆盖）告警属预期，判级为 Warning；
        // RunAndAssertNoErrors 仅阻断 Error，因此该用例可正常通过。
        Compiles(source, "POST + Body 接口");
    }

    #endregion

    #region Path Parameter - Compile Assert

    [Fact]
    public void Generator_WithPathParameter_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/users/{userId}"")]
        Task<string> GetUserAsync([Path] int userId);
    }
}";

        Compiles(source, "路径参数接口");
    }

    #endregion

    #region Query Parameter - Compile Assert

    [Fact]
    public void Generator_WithQueryParameter_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/search"")]
        Task<string> SearchAsync([Query] string keyword, [Query] int page);
    }
}";

        Compiles(source, "查询参数接口");
    }

    #endregion

    #region Header Parameter - Compile Assert

    [Fact]
    public void Generator_WithHeaderParameter_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/data"")]
        Task<string> GetDataAsync([Header(""X-Request-Id"")] string requestId);
    }
}";

        Compiles(source, "头参数接口");
    }

    #endregion

    #region Token Management - Compile Assert

    [Fact]
    public void Generator_WithTokenManager_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public interface ITestTokenManager
    {
        IMudAppContext GetDefaultApp();
        IMudAppContext GetApp(string appKey);
    }

    [HttpClientApi(TokenManage = ""ITestTokenManager"")]
    public interface ITestApi
    {
        [Get(""/secure-data"")]
        Task<string> GetSecureDataAsync();
    }
}";

        Compiles(source, "TokenManager 接口");
    }

    #endregion

    #region Form Parameters - Compile Assert

    [Fact]
    public void Generator_WithFormParameters_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Post(""/login"")]
        Task<string> LoginAsync([Form] string username, [Form] string password);
    }
}";

        Compiles(source, "表单参数接口");
    }

    #endregion

    #region Multipart Form with Upload - Compile Assert

    [Fact]
    public void Generator_WithMultipartFormUpload_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;
using System.IO;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Post(""/upload"")]
        Task<string> UploadAsync([MultipartForm] Stream fileStream, [MultipartForm] string description);
    }
}";

        Compiles(source, "Multipart 上传接口");
    }

    #endregion

    #region Response<T> Return Type - Compile Assert

    [Fact]
    public void Generator_WithResponseType_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/users/{id}"")]
        Task<Response<string>> GetUserAsync([Path] int id);
    }
}";

        Compiles(source, "Response&lt;T&gt; 返回类型接口");
    }

    #endregion

    #region Interface Properties - Compile Assert

    [Fact]
    public void Generator_WithInterfaceProperties_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    [InterfaceQuery(""version"", ""v1"")]
    public interface ITestApi
    {
        [Get(""/data"")]
        Task<string> GetDataAsync();
    }
}";

        Compiles(source, "接口级 Query 属性");
    }

    #endregion

    #region Multiple HTTP Methods - Compile Assert

    [Fact]
    public void Generator_WithMultipleMethods_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public class CreateUserRequest
    {
        public string Name { get; set; }
    }

    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync();

        [Get(""/users/{id}"")]
        Task<string> GetUserAsync([Path] int id);

        [Post(""/users"")]
        Task<string> CreateUserAsync([Body] CreateUserRequest request);

        [Put(""/users/{id}"")]
        Task<string> UpdateUserAsync([Path] int id, [Body] CreateUserRequest request);

        [Delete(""/users/{id}"")]
        Task DeleteUserAsync([Path] int id);
    }
}";

        // 含未覆盖的 Body DTO 会产生 AOT 告警（Warning），RunAndAssertNoErrors 仅阻断 Error，可通过。
        Compiles(source, "多 HTTP 方法接口");
    }

    #endregion

    #region QueryMap Parameter - Compile Assert

    [Fact]
    public void Generator_WithQueryMapParameter_GeneratesCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public class SearchParams
    {
        public string Keyword { get; set; }
        public int Page { get; set; }
    }

    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/search"")]
        Task<string> SearchAsync([QueryMap] SearchParams parameters);
    }
}";

        Compiles(source, "QueryMap 参数接口");
    }

    #endregion

    #region No Errors for Valid Interfaces

    [Fact]
    public void Generator_ValidInterface_NoGeneratorDiagnostics()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/data"")]
        Task<string> GetDataAsync();
    }
}";

        // 合法接口应符合「可编译无 Error」；生成器级无 Error/Warning 的判定由
        // GeneratorCompilationTests 其余用例与诊断专项用例覆盖，此处只做编译断言。
        Compiles(source, "合法接口无诊断");
    }

    #endregion

    #region GEN-03 方法级固定 Header/Query - Compile Assert

    /// <summary>
    /// GEN-03：方法级固定 [Header("Accept", "application/json")] 须发射 Add 且整体可编译。
    /// </summary>
    [Fact]
    public void MethodLevelHeader_EmitsFixedHeader()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/users"")]
        [Header(""Accept"", ""application/json"")]
        Task<string> GetUsersAsync();
    }
}";
        var output = Compiles(source, "方法级固定 Header 参数");
        var code = string.Join("", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));
        code.Should().Contain("Headers.Add(\"Accept\", \"application/json\")");
    }

    /// <summary>
    /// GEN-03：方法级固定 [Query("status", "active")] 须发射 __queryParams.Add 且整体可编译。
    /// </summary>
    [Fact]
    public void MethodLevelQuery_EmitsFixedQueryParam()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/users"")]
        [Query(""status"", ""active"")]
        Task<string> GetUsersAsync();
    }
}";
        var output = Compiles(source, "方法级固定 Query 参数");
        var code = string.Join("", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));
        code.Should().Contain("__queryParams.Add(\"status\", \"active\")");
    }

    #endregion
}