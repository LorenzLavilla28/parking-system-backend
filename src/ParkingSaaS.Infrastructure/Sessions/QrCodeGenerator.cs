using QRCoder;
using ParkingSaaS.Application.Abstractions;

namespace ParkingSaaS.Infrastructure.Sessions;

/// <summary>
/// Generates QR codes as PNG data URIs using QRCoder's <see cref="PngByteQRCode"/>,
/// which is fully managed and cross-platform (no System.Drawing dependency).
/// </summary>
public sealed class QrCodeGenerator : IQrCodeGenerator
{
    public string GeneratePngDataUri(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(pixelsPerModule: 10);
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }
}
