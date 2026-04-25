namespace Mud.HttpUtils;

public enum SensitiveDataMaskMode
{
    Hide,

    Mask,

    TypeOnly
}

public interface ISensitiveDataMasker
{
    string Mask(string value, SensitiveDataMaskMode mode = SensitiveDataMaskMode.Mask, int prefixLength = 2, int suffixLength = 2);

    string MaskObject(object obj);
}
