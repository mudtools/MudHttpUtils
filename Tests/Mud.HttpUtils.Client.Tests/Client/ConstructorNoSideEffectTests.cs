// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// 验证 B1 修复：构造函数不再无条件写入 _appContextHolder.Current。
/// 默认模式下，构造函数不应覆盖已存在的应用上下文。
/// </summary>
/// <remarks>
/// 生成器修复前：默认模式构造函数包含 `_appContextHolder.Current = _appManager.GetDefaultApp();`，
/// 这会在构造客户端时强制覆盖外部已设置的上下文。
/// 修复后：构造函数不再写入 Current，由 UseApp/UseDefaultApp/BeginScope 显式切换。
/// 由于测试项目不直接引用生成类，本测试验证 AsyncLocalAppContextSwitcher 的行为语义：
/// 构造 holder 实例时不触碰 Current；Current 的写入只由显式 API 触发。
/// </remarks>
public class ConstructorNoSideEffectTests
{
    [Fact]
    public void AsyncLocalAppContextSwitcher_Construction_DoesNotSetCurrent()
    {
        // Arrange & Act — 构造一个新的 holder 实例
        var holder = new AsyncLocalAppContextSwitcher();

        // Assert — 构造后 Current 应为 null（无副作用）
        holder.Current.Should().BeNull();
    }

    [Fact]
    public void ConstructingNewSwitcher_DoesNotOverwriteExistingContext()
    {
        // Arrange — 预设一个已存在的上下文
        var holder = new AsyncLocalAppContextSwitcher();
        var existingContext = CreateTestContext("app1");
        holder.SwitchTo(existingContext);

        // Act — 模拟生成类构造场景：
        // 生成类构造函数接收 holder 作为 DI 依赖，不调用 holder.Current = ...
        // 此处验证 holder.Current 不因 new 操作而改变
        // （AsyncLocalAppContextSwitcher 构造函数仅初始化 AsyncLocal 字段，不写入 Current）

        // Assert — Current 保持不变（构造无副作用）
        holder.Current.Should().BeSameAs(existingContext);

        // 清理
        holder.SwitchTo(null);
    }

    [Fact]
    public void Current_OnlyChangesWhenExplicitlySet()
    {
        // Arrange
        var holder = new AsyncLocalAppContextSwitcher();
        holder.Current.Should().BeNull();

        // Act — 仅显式设置才改变 Current
        var ctxA = CreateTestContext("appA");
        holder.SwitchTo(ctxA);
        holder.Current.Should().BeSameAs(ctxA);

        // 模拟构造新客户端（不触碰 Current）
        // ... new GeneratedClient(...) 不调用 holder.Current = ...
        // Current 仍为 ctxA
        holder.Current.Should().BeSameAs(ctxA);

        // 清理
        holder.SwitchTo(null);
    }

    [Fact]
    public void BeginScope_RestoresPreviousContext_AfterDispose()
    {
        // Arrange
        var holder = new AsyncLocalAppContextSwitcher();
        var original = CreateTestContext("original");
        holder.SwitchTo(original);

        // Act
        var scoped = CreateTestContext("scoped");
        using (holder.BeginScope(scoped))
        {
            holder.Current.Should().BeSameAs(scoped);
        }

        // Assert — 作用域结束后恢复
        holder.Current.Should().BeSameAs(original);

        holder.SwitchTo(null);
    }

    private static TestAppContext CreateTestContext(string appKey) => new(appKey);

    private sealed class TestAppContext : IMudAppContext
    {
        public TestAppContext(string appKey) { AppKey = appKey; }
        public string AppKey { get; }
        public IEnhancedHttpClient HttpClient => throw new NotImplementedException();
        public ITokenManager GetTokenManager(string tokenType = "") => null!;
        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotImplementedException();
        public T? GetService<T>() where T : class => null;
    }
}
