namespace Mud.HttpUtils.Client.Tests;

public class DefaultCurrentUserContextTests
{
    [Fact]
    public void UserId_DefaultIsNull()
    {
        var context = new DefaultCurrentUserContext<CurrentUserInfo>();

        context.UserId.Should().BeNull();
    }

    [Fact]
    public void SetUser_SetsUserId()
    {
        var context = new DefaultCurrentUserContext<CurrentUserInfo>();
        context.SetUser(new CurrentUserInfo { UserId = "user123" });

        context.UserId.Should().Be("user123");
    }

    [Fact]
    public void SetUser_NullClearsUserId()
    {
        var context = new DefaultCurrentUserContext<CurrentUserInfo>();
        context.SetUser(new CurrentUserInfo { UserId = "user123" });
        context.SetUser(null);

        context.UserId.Should().BeNull();
    }

    [Fact]
    public void SetUserId_SetsUserId()
    {
        var context = new DefaultCurrentUserContext<CurrentUserInfo>();
        context.SetUserId("user456");

        context.UserId.Should().Be("user456");
    }

    [Fact]
    public void SetUserId_NullClearsUserId()
    {
        var context = new DefaultCurrentUserContext<CurrentUserInfo>();
        context.SetUserId("user456");
        context.SetUserId(null);

        context.UserId.Should().BeNull();
    }

    [Fact]
    public async Task UserId_FlowsAcrossAsyncContext()
    {
        var context = new DefaultCurrentUserContext<CurrentUserInfo>();
        context.SetUser(new CurrentUserInfo { UserId = "async-user" });

        await Task.Yield();

        context.UserId.Should().Be("async-user");
    }

    [Fact]
    public async Task UserId_IsolatedAcrossTasks()
    {
        var context1 = new DefaultCurrentUserContext<CurrentUserInfo>();
        var context2 = new DefaultCurrentUserContext<CurrentUserInfo>();

        var t1 = Task.Run(async () =>
        {
            context1.SetUser(new CurrentUserInfo { UserId = "task1-user" });
            await Task.Delay(50);
            return context1.UserId;
        });

        var t2 = Task.Run(async () =>
        {
            context2.SetUser(new CurrentUserInfo { UserId = "task2-user" });
            await Task.Delay(50);
            return context2.UserId;
        });

        var results = await Task.WhenAll(t1, t2);

        results[0].Should().Be("task1-user");
        results[1].Should().Be("task2-user");
    }

    [Fact]
    public void MultipleInstances_AreIsolated()
    {
        var context1 = new DefaultCurrentUserContext<CurrentUserInfo>();
        var context2 = new DefaultCurrentUserContext<CurrentUserInfo>();

        context1.SetUserId("user1");
        context2.SetUserId("user2");

        context1.UserId.Should().Be("user1");
        context2.UserId.Should().Be("user2");
    }
}
