using Microsoft.Extensions.Logging;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Domain.Emails;

namespace ParkingSaaS.Infrastructure.Email;

/// <summary>
/// No-op <see cref="IEmailSender"/> used when email delivery is disabled
/// (development/CI). Records what would have been sent so queued mail is observable
/// in logs, and always "succeeds" so nothing dead-letters.
/// </summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger) => _logger = logger;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[email:disabled] Would send {Kind} to {To} — \"{Subject}\"",
            message.Kind, message.ToEmail, message.Subject);
        return Task.CompletedTask;
    }
}
