namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CircuitBreakerAttribute : Attribute
{
    public CircuitBreakerAttribute(int failureThreshold = 5)
    {
        FailureThreshold = failureThreshold;
    }

    public int FailureThreshold { get; set; }

    public int BreakDurationSeconds { get; set; } = 30;
}
