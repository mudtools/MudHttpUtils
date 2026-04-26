namespace Mud.HttpUtils.Encryption;

internal static class SecurityHelper
{
    internal static void ClearBytes(byte[]? bytes)
    {
        if (bytes == null)
            return;

        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = 0;
        }
    }
}
