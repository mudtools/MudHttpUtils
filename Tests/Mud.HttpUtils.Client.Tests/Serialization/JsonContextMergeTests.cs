// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

#if NET8_0_OR_GREATER

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Client.Tests.Serialization;

/// <summary>
/// 多 JSON 上下文合并便捷件（<c>AddMudHttpClientJsonContexts</c>）：
/// 全类型覆盖进链、未覆盖类型不进链、<b>后者优先</b>（链短路可观测）、null 守卫。
/// </summary>
public class JsonContextMergeTests
{
    public sealed class PayloadA { public int Value { get; set; } }

    public sealed class PayloadB { public int Value { get; set; } }

    public sealed class PayloadC { public int Value { get; set; } }

    public sealed class SharedPayload { public int Value { get; set; } }

    /// <summary>
    /// 记录型 context 替身：仅对指定类型经<b>独立的反射 resolver</b> 产出类型信息
    /// （不能调用 <c>options.GetTypeInfo</c> —— 那会经 TypeInfoResolver 回到本链造成递归），
    /// 并计数命中次数 —— 链上首个非 null 命中即短路，计数即优先级证据。
    /// </summary>
    private sealed class SpyJsonContext : JsonSerializerContext, IJsonTypeInfoResolver
    {
        private static readonly IJsonTypeInfoResolver ReflectionResolver = new DefaultJsonTypeInfoResolver();

        private readonly Type _supported;

        public SpyJsonContext(Type supported)
            : base(null) => _supported = supported;

        public int HitCount;

        protected override JsonSerializerOptions? GeneratedSerializerOptions => null;

        /// <summary>基类抽象成员：本替身经显式接口实现供电，此处恒不命中。</summary>
        public override JsonTypeInfo? GetTypeInfo(Type type) => null;

        JsonTypeInfo? IJsonTypeInfoResolver.GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (type != _supported)
                return null;
            Interlocked.Increment(ref HitCount);
            return ReflectionResolver.GetTypeInfo(type, options);
        }
    }

    private static JsonSerializerOptions ResolveMergedOptions(params JsonSerializerContext[] contexts)
    {
        var services = new ServiceCollection();
        services.AddMudHttpClientJsonContexts(contexts);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<JsonSerializerOptions>>().Value;
    }

    [Fact]
    public void MultipleContexts_AllTypesCovered_UncoveredTypeNotInChain()
    {
        var ctxA = new SpyJsonContext(typeof(PayloadA));
        var ctxB = new SpyJsonContext(typeof(PayloadB));

        var options = ResolveMergedOptions(ctxA, ctxB);
        var resolver = options.TypeInfoResolver;

        resolver.Should().NotBeNull();
        resolver!.GetTypeInfo(typeof(PayloadA), options).Should().NotBeNull("第一个 context 覆盖的类型应进链");
        resolver.GetTypeInfo(typeof(PayloadB), options).Should().NotBeNull("第二个 context 覆盖的类型应进链");
        resolver.GetTypeInfo(typeof(PayloadC), options).Should().BeNull("未覆盖的类型不进链");
        ctxA.HitCount.Should().Be(1);
        ctxB.HitCount.Should().Be(1);
    }

    [Fact]
    public void OverlappingContexts_LatterWins_ChainShortCircuits()
    {
        var ctxA = new SpyJsonContext(typeof(SharedPayload));
        var ctxB = new SpyJsonContext(typeof(SharedPayload));

        var options = ResolveMergedOptions(ctxA, ctxB);

        var info = options.TypeInfoResolver!.GetTypeInfo(typeof(SharedPayload), options);
        info.Should().NotBeNull();

        ctxB.HitCount.Should().Be(1, "后者优先：第二个 context 在链首命中");
        ctxA.HitCount.Should().Be(0, "链短路：首个非 null 命中即返回，前者不被调用");
    }

    [Fact]
    public void NullContextsArray_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddMudHttpClientJsonContexts(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NullContextElement_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddMudHttpClientJsonContexts(
            new SpyJsonContext(typeof(PayloadA)), null!);

        act.Should().Throw<ArgumentNullException>();
    }
}

#endif
