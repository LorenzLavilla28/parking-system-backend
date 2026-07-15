namespace ParkingSaaS.Application.Abstractions;

/// <summary>Renders a URL into a QR code image for display or printing.</summary>
public interface IQrCodeGenerator
{
    /// <summary>Returns a PNG <c>data:</c> URI the client can render directly in an &lt;img&gt;.</summary>
    string GeneratePngDataUri(string content);
}
