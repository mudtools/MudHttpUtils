namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CacheAttribute : Attribute
{
    public CacheAttribute(int durationSeconds = 300)
    {
        DurationSeconds = durationSeconds;
    }

    public int DurationSeconds { get; set; }

    public string? CacheKeyTemplate { get; set; }

    public bool VaryByUser { get; set; }

    public bool UseSlidingExpiration { get; set; }

    public CachePriority Priority { get; set; } = CachePriority.Normal;
}

public enum CachePriority
{
    Low,
    Normal,
    High,
    NeverRemove
}
