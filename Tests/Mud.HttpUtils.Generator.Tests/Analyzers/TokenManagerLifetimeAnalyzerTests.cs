using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Mud.HttpUtils.Analyzers;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// <see cref="TokenManagerLifetimeAnalyzer"/>（MUD004）专用测试（P3.5 / C5，TK-24）。
/// </summary>
/// <remarks>
/// 使用合成编译（自含 <c>IServiceCollection</c> 与 <c>ServiceCollectionServiceExtensions</c> 定义），
/// <c>Mud.HttpUtils.ITokenManager</c> 通过引用程序集解析，专注验证 MUD004 的触发/不触发逻辑。
/// </remarks>
public class TokenManagerLifetimeAnalyzerTests
{
    // 仅含命名空间声明，置于测试某源的 using 指令之后拼接，以满足 CS1529 的 using 顺序约束。
    private const string DiBoilerplate = """
        namespace Microsoft.Extensions.DependencyInjection
        {
            public interface IServiceCollection { }

            public static class ServiceCollectionServiceExtensions
            {
                public static IServiceCollection AddScoped<T>(this IServiceCollection s) => s;
                public static IServiceCollection AddScoped<TService, TImpl>(this IServiceCollection s) where TImpl : class, TService => s;
                public static IServiceCollection AddTransient<T>(this IServiceCollection s) => s;
                public static IServiceCollection AddTransient<TService, TImpl>(this IServiceCollection s) where TImpl : class, TService => s;
                public static IServiceCollection TryAddScoped<T>(this IServiceCollection s) => s;
                public static IServiceCollection TryAddTransient<T>(this IServiceCollection s) => s;
                public static IServiceCollection AddSingleton<T>(this IServiceCollection s) => s;
            }
        }
        """;

    private const string TokenManagerClassTemplate = """
        public class MyTokenManager : ITokenManager
        {
            public Task<string> GetTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult("t");
            public Task<string> GetTokenAsync(string[]? scopes, CancellationToken cancellationToken = default) => Task.FromResult("t");
            public Task<string> GetOrRefreshTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult("t");
            public Task<string> GetOrRefreshTokenAsync(string[]? scopes, CancellationToken cancellationToken = default) => Task.FromResult("t");
            public Task<TokenResult> InvalidateTokenAsync(string[]? scopes = null, CancellationToken cancellationToken = default) => Task.FromResult(TokenResult.Empty);
            public bool SupportsBackgroundRefresh => true;
            public void Dispose() { }
        }
        """;

    private static ImmutableArray<Diagnostic> RunAnalyzer(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = BasicReferenceAssemblies.GetReferences();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new TokenManagerLifetimeAnalyzer();
        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public void AddScoped_ConcreteTypeImplementingITokenManager_ReportsMUD004()
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using Mud.HttpUtils;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                {{TokenManagerClassTemplate}}

                public static class Setup
                {
                    public static void Register(IServiceCollection services)
                        => services.AddScoped<MyTokenManager>();
                }
            }
            """;

        var fullSource = source + "\n" + DiBoilerplate;
        var diagnostics = RunAnalyzer(fullSource);

        diagnostics.Should().Contain(d => d.Id == "MUD004");
    }

    [Fact]
    public void AddTransient_ITokenManagerServiceType_ReportsMUD004()
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using Mud.HttpUtils;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                {{TokenManagerClassTemplate}}

                public static class Setup
                {
                    public static void Register(IServiceCollection services)
                        => services.AddTransient<ITokenManager, MyTokenManager>();
                }
            }
            """;

        var fullSource = source + "\n" + DiBoilerplate;
        var diagnostics = RunAnalyzer(fullSource);

        diagnostics.Should().Contain(d => d.Id == "MUD004");
    }

    [Fact]
    public void AddScoped_ITokenManagerServiceType_ReportsMUD004()
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using Mud.HttpUtils;

            namespace TestNamespace
            {
                {{TokenManagerClassTemplate}}

                public static class Setup
                {
                    public static void Register(IServiceCollection services)
                        => services.AddScoped<ITokenManager, MyTokenManager>();
                }
            }
            """;

        var fullSource = source + "\n" + DiBoilerplate;
        var diagnostics = RunAnalyzer(fullSource);

        diagnostics.Should().Contain(d => d.Id == "MUD004");
    }

    [Fact]
    public void AddSingleton_ConcreteType_NoMUD004()
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using Mud.HttpUtils;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                {{TokenManagerClassTemplate}}

                public static class Setup
                {
                    public static void Register(IServiceCollection services)
                        => services.AddSingleton<MyTokenManager>();
                }
            }
            """;

        var fullSource = source + "\n" + DiBoilerplate;
        var diagnostics = RunAnalyzer(fullSource);

        diagnostics.Should().NotContain(d => d.Id == "MUD004");
    }

    [Fact]
    public void AddScoped_NonTokenManagerType_NoMUD004()
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;

            namespace TestNamespace
            {
                public class Repository { }

                public static class Setup
                {
                    public static void Register(IServiceCollection services)
                        => services.AddScoped<Repository>();
                }
            }
            """;

        var fullSource = source + "\n" + DiBoilerplate;
        var diagnostics = RunAnalyzer(fullSource);

        diagnostics.Should().NotContain(d => d.Id == "MUD004");
    }
}