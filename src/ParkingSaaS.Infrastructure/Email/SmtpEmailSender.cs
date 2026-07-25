using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Domain.Emails;

namespace ParkingSaaS.Infrastructure.Email;

/// <summary>
/// SMTP <see cref="IEmailSender"/>. Builds a multipart (plain-text + HTML) message and
/// sends it via the configured server. Throws on failure so the dispatcher can retry
/// and eventually dead-letter. One client is created per send (SmtpClient is not safe
/// to share across concurrent sends); the dispatcher sends sequentially anyway.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(IOptions<EmailOptions> options) => _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = message.Subject
        };
        mail.To.Add(string.IsNullOrWhiteSpace(message.ToName)
            ? new MailAddress(message.ToEmail)
            : new MailAddress(message.ToEmail, message.ToName));

        // MailMessage.Body is ignored once AlternateViews is non-empty, so the plain-text
        // part must be a view too. Least-preferred part first, per RFC 2046 §5.1.4.
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            message.TextBody, null, "text/plain"));
        var htmlView = InlineImageEmailView.Create(message.HtmlBody);
        mail.AlternateViews.Add(htmlView);

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        if (!string.IsNullOrWhiteSpace(_options.Username))
            client.Credentials = new NetworkCredential(_options.Username, _options.Password);

        await client.SendMailAsync(mail, cancellationToken);
    }
}

/// <summary>
/// Converts embedded image data URIs into CID-linked resources for SMTP delivery.
/// Data URIs are convenient in local previews but are commonly blocked by webmail
/// clients. CID resources are part of the MIME message and render reliably there.
/// </summary>
internal static partial class InlineImageEmailView
{
    private static readonly Regex DataImagePattern = CreateDataImagePattern();

    public static AlternateView Create(string html)
    {
        var resources = new List<LinkedResource>();
        var transformed = DataImagePattern.Replace(html, match =>
        {
            try
            {
                var contentType = match.Groups[1].Value;
                var bytes = Convert.FromBase64String(match.Groups[2].Value);
                var resource = new LinkedResource(new MemoryStream(bytes), contentType)
                {
                    ContentId = $"inline-{Guid.NewGuid():N}",
                    TransferEncoding = TransferEncoding.Base64,
                };
                resources.Add(resource);
                return $"src=\"cid:{resource.ContentId}\"";
            }
            catch (FormatException)
            {
                // Leave malformed image data untouched so the normal HTML fallback
                // and payment button remain available to the recipient.
                return match.Value;
            }
        });

        var view = AlternateView.CreateAlternateViewFromString(transformed, Encoding.UTF8, MediaTypeNames.Text.Html);
        foreach (var resource in resources)
            view.LinkedResources.Add(resource);
        return view;
    }

    [GeneratedRegex("src=\\\"data:(image/[^;\\\"]+);base64,([^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreateDataImagePattern();
}
