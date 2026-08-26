using System.Security.Cryptography;
using System.Text;
using DoSelect.Application.Orders;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Security;

/// <summary>
/// HMAC-SHA256 實作，金鑰來自 <see cref="GuestOrderAccessOptions.Pepper"/>。長度檢查現在
/// 由 <c>ConfigurationValidationExtensions.AddValidatedConfiguration</c> 的
/// <c>ValidateOnStart()</c> 在應用程式啟動時就先擋下，這裡的建構子檢查是第二層防線
/// （例如測試直接 new 這個類別、繞過完整 DI 啟動流程時）。
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
