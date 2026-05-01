namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class TimeoutAttribute : Attribute
{
    public TimeoutAttribute(int timeoutMilliseconds)
    {
        TimeoutMilliseconds = timeoutMilliseconds;
    }

    public int TimeoutMilliseconds { get; set; }
}
