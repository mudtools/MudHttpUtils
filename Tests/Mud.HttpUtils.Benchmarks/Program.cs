// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace Mud.HttpUtils.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        var switcher = new BenchmarkSwitcher(new[]
        {
            typeof(Benchmarks),
            typeof(HttpClientBenchmarks)
        });
        switcher.Run(args);
    }
}

[MemoryDiagnoser]
public class Benchmarks
{
    private const string SampleToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
    private const string SamplePhone = "13812345678";
    private const string SampleEmail = "test@example.com";
    private const string SampleIdCard = "11010119900307999X";
    private const string SampleMessage = "用户 test@example.com 的手机号是 13812345678，身份证号是 11010119900307999X，Token: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

    [Benchmark(Description = "敏感信息脱敏 - 短文本")]
    public string SanitizeShortText()
    {
        return MessageSanitizer.Sanitize("Hello World");
    }

    [Benchmark(Description = "敏感信息脱敏 - 长文本")]
    public string SanitizeLongText()
    {
        return MessageSanitizer.Sanitize(SampleMessage);
    }

    [Benchmark(Description = "敏感信息脱敏 - 包含多种敏感信息")]
    public string SanitizeComplexText()
    {
        return MessageSanitizer.Sanitize($"Token: {SampleToken}, Phone: {SamplePhone}, Email: {SampleEmail}, IdCard: {SampleIdCard}");
    }
}
