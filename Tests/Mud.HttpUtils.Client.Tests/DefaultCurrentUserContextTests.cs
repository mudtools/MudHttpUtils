namespace Mud.HttpUtils.Client.Tests;

public class DefaultCurrentUserContextTests
{
    [Fact]
    public void UserId_DefaultIsNull()
    {
        var context = new DefaultCurrentUserContext();

        context.UserId.Should().BeNull();
    }

    [Fact]
    public void SetUserId_SetsValue()
    {
        DefaultCurrentUserContext.SetUserId("user123");
        try
        {
            var context = new DefaultCurrentUserContext();
            context.UserId.Should().Be("user123");
        }
        finally
        {
            DefaultCurrentUserContext.SetUserId(null);
        }
    }

    [Fact]
    public void SetUserId_NullClearsValue()
    {
        DefaultCurrentUserContext.SetUserId("user123");
        DefaultCurrentUserContext.SetUserId(null);
        try
        {
            var context = new DefaultCurrentUserContext();
            context.UserId.Should().BeNull();
        }
        finally
        {
            DefaultCurrentUserContext.SetUserId(null);
        }
    }

    [Fact]
    public async Task UserId_FlowsAcrossAsyncContext()
    {
        DefaultCurrentUserContext.SetUserId("async-user");
        try
        {
            await Task.Yield();

            var context = new DefaultCurrentUserContext();
            context.UserId.Should().Be("async-user");
        }
        finally
        {
            DefaultCurrentUserContext.SetUserId(null);
        }
    }

    [Fact]
    public async Task UserId_IsolatedAcrossTasks()
    {
        DefaultCurrentUserContext.SetUserId(null);

        var t1 = Task.Run(async () =>
        {
            DefaultCurrentUserContext.SetUserId("task1-user");
            await Task.Delay(50);
            var context = new DefaultCurrentUserContext();
            return context.UserId;
        });

        var t2 = Task.Run(async () =>
        {
            DefaultCurrentUserContext.SetUserId("task2-user");
            await Task.Delay(50);
            var context = new DefaultCurrentUserContext();
            return context.UserId;
        });

        var results = await Task.WhenAll(t1, t2);

        results[0].Should().Be("task1-user");
        results[1].Should().Be("task2-user");

        DefaultCurrentUserContext.SetUserId(null);
    }
}
