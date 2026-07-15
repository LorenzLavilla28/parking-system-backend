using System.Net;
using System.Net.Mail;
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
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            message.HtmlBody, null, "text/html"));

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
