using System.Security.Cryptography;
using System.Text;
using DoSelect.Application.Orders;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Security;

/// <summary>
/// HMAC-SHA256 實作，金鑰來自 <see cref="GuestOrderAccessOptions.Pepper"/>。比照
/// <c>EfIdempotencyExecutor</c> 對 Pepper 長度的驗證方式。
/// </summary>
public sealed class GuestOrderAccessHasher : IGuestOrderAccessHasher
{
    private readonly byte[] _pepper;

    public GuestOrderAccessHasher(IOptions<GuestOrderAccessOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var pepper = options.Value.Pepper;
        if (Encoding.UTF8.GetByteCount(pepper) < 32)
        {
            throw new InvalidOperationException(
                "Configuration key 'GuestOrderAccess:Pepper' must contain at least 32 UTF-8 bytes.");
        }

        _pepper = Encoding.UTF8.GetBytes(pepper);
    }

    public byte[] HashIp(string ipAddress) => Hash("ip", ipAddress.Trim());

    public byte[] HashEmail(string emailNormalized) => Hash("email", emailNormalized);

    public byte[] HashOrderLookup(string orderNumber, string emailNormalized) =>
        Hash("order-lookup", $"{orderNumber.Trim().ToUpperInvariant()}:{emailNormalized}");

    public byte[] HashCode(string sixDigitCode) => Hash("code", sixDigitCode.Trim());

    public byte[] HashToken(string rawToken) => Hash("token", rawToken.Trim());

    private byte[] Hash(string scope, string value) =>
        HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes($"{scope}:{value}"));
}
