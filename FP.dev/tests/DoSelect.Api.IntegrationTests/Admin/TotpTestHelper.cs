using System.Security.Cryptography;

namespace DoSelect.Api.IntegrationTests.Admin;

/// <summary>
/// 產生跟 ASP.NET Core Identity 的 <c>AuthenticatorTokenProvider</c> 相容的 TOTP 碼
/// （RFC 6238／RFC 4226，SHA1、6 碼、30 秒週期），用來在測試裡對一組已知的 Base32 秘鑰
/// 算出「現在有效」的驗證碼，不用真的去操作 Authenticator App。
/// </summary>
internal static class TotpTestHelper
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateCode(string base32Secret, DateTimeOffset? atUtc = null)
    {
        var key = Base32Decode(base32Secret);
        var counter = (long)((atUtc ?? DateTimeOffset.UtcNow) - DateTimeOffset.UnixEpoch).TotalSeconds / 30;

        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        var hash = HMACSHA1.HashData(key, counterBytes);
        var offset = hash[^1] & 0x0F;
        var binaryCode =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        return (binaryCode % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        var normalized = input.TrimEnd('=').ToUpperInvariant();
        var bits = new List<bool>(normalized.Length * 5);
        foreach (var c in normalized)
        {
            var index = Base32Alphabet.IndexOf(c);
            if (index < 0)
            {
                throw new ArgumentException($"'{c}' is not a valid Base32 character.", nameof(input));
            }

            for (var bit = 4; bit >= 0; bit--)
            {
                bits.Add((index & (1 << bit)) != 0);
            }
        }

        var bytes = new List<byte>(bits.Count / 8);
        for (var i = 0; i + 8 <= bits.Count; i += 8)
        {
            byte value = 0;
            for (var bit = 0; bit < 8; bit++)
            {
                if (bits[i + bit])
                {
                    value |= (byte)(1 << (7 - bit));
                }
            }

            bytes.Add(value);
        }

        return [.. bytes];
    }
}
