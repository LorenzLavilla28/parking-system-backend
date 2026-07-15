using ParkingSaaS.Domain.Emails;

namespace ParkingSaaS.Application.Abstractions;

/// <summary>
/// Transport that actually delivers an email (SMTP, a provider API, or a no-op log
/// sink in environments without mail configured). The concrete implementation lives
/// in infrastructure; the dispatcher depends only on this. Implementations should
/// throw on failure so the dispatcher can retry/dead-letter.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
