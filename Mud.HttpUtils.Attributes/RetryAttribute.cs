namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RetryAttribute : Attribute
{
    public RetryAttribute(int maxRetries = 3)
    {
        MaxRetries = maxRetries;
    }

    public int MaxRetries { get; set; }

    public int DelayMilliseconds { get; set; } = 1000;

    public bool UseExponentialBackoff { get; set; } = true;
}
