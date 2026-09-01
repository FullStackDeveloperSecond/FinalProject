using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace DoSelect.Infrastructure.Promotions;

/// <summary>
/// 訪客優惠券每人使用鍵的 V1 設定。Secret 只可由 User Secrets 或部署環境提供。
/// </summary>
public sealed class CouponGuestUsageOptions
{
    public const string SectionName = "Security";

    public string CouponGuestUsageHmacKeyV1 { get; set; } = string.Empty;
}

/// <summary>
/// 依 DEC-P262，以伺服器 V1 Secret 對正規化訂單 Email 計算 HMAC-SHA-256。
/// </summary>
/// <remarks>
/// 驗證刻意延後到真正使用訪客優惠券時：會員 Checkout，以及未使用優惠券的訪客
/// Checkout，不應因為這把選用 Secret 尚未設定而無法啟動或下單。
/// </remarks>
public sealed class CouponGuestUsageHasher
{
    private readonly string _key;

    public CouponGuestUsageHasher(IOptions<CouponGuestUsageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _key = options.Value.CouponGuestUsageHmacKeyV1 ?? string.Empty;
    }

    public byte[] HashEmail(string email)
    {
        if (Encoding.UTF8.GetByteCount(_key) < 32)
        {
            throw new InvalidOperationException(
                "Configuration key 'Security:CouponGuestUsageHmacKeyV1' must contain at least 32 UTF-8 bytes before a guest coupon can be used.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("The guest order Email is required.", nameof(email));
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        return HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(_key),
            Encoding.UTF8.GetBytes(normalizedEmail));
    }
}
