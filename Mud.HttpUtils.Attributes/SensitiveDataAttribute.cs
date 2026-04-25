namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class SensitiveDataAttribute : Attribute
{
    public SensitiveDataMaskMode MaskMode { get; set; } = SensitiveDataMaskMode.Mask;

    public int PrefixLength { get; set; } = 2;

    public int SuffixLength { get; set; } = 2;
}
