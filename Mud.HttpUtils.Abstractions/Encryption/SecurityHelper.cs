// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

internal static class SecurityHelper
{
    internal static void ClearBytes(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return;

#if NET5_0_OR_GREATER
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes.AsSpan());
#else
        for (var i = 0; i < bytes.Length; i++)
        {
            Volatile.Write(ref bytes[i], (byte)0);
        }
#endif
    }

    /// <summary>
    /// 常量时间（constant-time）字节序列比较，防止时序攻击。
    /// </summary>
    /// <param name="left">第一个字节序列。</param>
    /// <param name="right">第二个字节序列。</param>
    /// <returns>两个序列长度相等且内容完全一致时返回 <c>true</c>；否则返回 <c>false</c>。</returns>
    /// <remarks>
    /// <para>
    /// 全局唯一实现，供 <c>DefaultAesEncryptionProvider</c>（CBC+HMAC 信封的 MAC 校验）与
    /// <c>DefaultHmacSignatureProvider</c>（请求签名校验）共用。
    /// </para>
    /// <para>
    /// net5.0+ 直接委托 <c>System.Security.Cryptography.CryptographicOperations.FixedTimeEquals</c>；
    /// netstandard2.0 使用等价的 XOR 累积实现，保证比较时间与数据内容无关。
    /// </para>
    /// </remarks>
    internal static bool FixedTimeEquals(byte[] left, byte[] right)
    {
#if NET5_0_OR_GREATER
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
#else
        if (left.Length != right.Length)
            return false;

        var diff = 0;
        for (var i = 0; i < left.Length; i++)
            diff |= left[i] ^ right[i];

        return diff == 0;
#endif
    }
}
