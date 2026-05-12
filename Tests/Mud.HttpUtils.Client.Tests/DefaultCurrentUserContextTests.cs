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
        DefaultCurrentUserContext<CurrentUserInfo>.SetUser(new CurrentUserInfo { UserId = "user123" });
        try
        {
            var context = new DefaultCurrentUserContext<CurrentUserInfo>();
            context.UserId.Should().Be("user123");
        }
        finally
        {
            DefaultCurrentUserContext<CurrentUserInfo>.SetUser(null);
        }
    }

    [Fact]
    public void SetUser_NullClearsUserId()
    {
        DefaultCurrentUserContext<CurrentUserInfo>.SetUser(new CurrentUserInfo { UserId = "user123" });
        DefaultCurrentUserContext<CurrentUserInfo>.SetUser(null);
        try
        {
            var context = new DefaultCurrentUserContext<CurrentUserInfo>();
            context.UserId.Should().BeNull();
        }
        finally
        {
            DefaultCurrentUserContext<CurrentUserInfo>.SetUser(null);
        }
    }

    [Fact]
    public async Task UserId_FlowsAcrossAsyncContext()
    {
        DefaultCurrentUserContext<CurrentUserInfo>.SetUser(new CurrentUserInfo { UserId = "async-user" });
        try
        {
            await Task.Yield();

            var context = new DefaultCurrentUserContext<CurrentUserInfo>();
            context.UserId.Should().Be("async-user");
        }
        finally
        {
            DefaultCurrentUserContext<CurrentUserInfo>.SetUser(null);
        }
    }

    [Fact]
    public async Task UserId_IsolatedAcrossTasks()
    {
        DefaultCurrentUserContext<CurrentUserInfo>.SetUser(null);

        var t1 = Task.Run(async () =>
        {
            DefaultCurrentUserContext<CurrentUserInfo>.SetUser(new CurrentUserInfo { UserId = "task1-user" });
            await Task.Delay(50);
            var context = new DefaultCurrentUserContext<CurrentUserInfo>();
            return context.UserId;
        });

        var t2 = Task.Run(async () =>
        {
            DefaultCurrentUserContext<CurrentUserInfo>.SetUser(new CurrentUserInfo { UserId = "task2-user" });
            await Task.Delay(50);
            var context = new DefaultCurrentUserContext<CurrentUserInfo>();
            return context.UserId;
        });

        var results = await Task.WhenAll(t1, t2);

        results[0].Should().Be("task1-user");
        results[1].Should().Be("task2-user");

        DefaultCurrentUserContext<CurrentUserInfo>.SetUser(null);
    }
}
