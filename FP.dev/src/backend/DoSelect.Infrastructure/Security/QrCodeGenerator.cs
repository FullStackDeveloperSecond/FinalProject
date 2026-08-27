using DoSelect.Application.Security;
using QRCoder;

namespace DoSelect.Infrastructure.Security;

/// <summary>
/// ⚠ 新增套件：用 QRCoder 的 <see cref="PngByteQRCode"/>（非 System.Drawing 版本，
/// 避免 GDI+ 依賴）產生管理員 TOTP 綁定用的 QR 碼圖片。
/// </summary>
public sealed class QrCodeGenerator : ITotpQrCodeGenerator
{
    private const int PixelsPerModule = 10;

    public string CreatePngDataUri(string otpAuthUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(otpAuthUri);

        using var qrCodeGenerator = new QRCodeGenerator();
        using var qrCodeData = qrCodeGenerator.CreateQrCode(otpAuthUri, QRCodeGenerator.ECCLevel.M);
        var pngQrCode = new PngByteQRCode(qrCodeData);
        var pngBytes = pngQrCode.GetGraphic(PixelsPerModule);

        return $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";
    }
}
