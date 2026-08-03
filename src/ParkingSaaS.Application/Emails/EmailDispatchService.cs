using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Domain.Emails;

namespace ParkingSaaS.Application.Emails;

/// <summary>
/// Drains due messages from the email outbox and sends them via <see cref="IEmailSender"/>.
/// Each message's result is persisted individually so a mid-batch crash re-sends at most
/// one message. Failures are retried with backoff and dead-lettered after the configured
/// number of attempts. Not tenant-scoped: <see cref="EmailMessage"/> is provider-global.
/// </summary>
public sealed class EmailDispatchService : IEmailDispatchService
{
    private readonly IApplicationDbContext _db;
    private readonly IEmailSender _sender;
    private readonly IDateTime _clock;
    private readonly EmailOptions _options;
    private readonly ILogger<EmailDispatchService> _logger;

    public EmailDispatchService(
        IApplicationDbContext db,
        IEmailSender sender,
        IDateTime clock,
        IOptions<EmailOptions> options,
        ILogger<EmailDispatchService> logger)
    {
        _db = db;
        _sender = sender;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EmailDispatchSummary> DispatchDueAsync(CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Email delivery is disabled; pending messages remain queued.");
            return new EmailDispatchSummary(0, 0, 0, 0);
        }

        var now = _clock.UtcNow;
        var batchSize = Math.Clamp(_options.DispatchBatchSize, 1, 200);

        var due = await _db.Emails
            .Where(e => e.Status == EmailStatus.Pending && e.NextAttemptAt <= now)
            .OrderBy(e => e.NextAttemptAt)
            .Take(batchSize)
            .ToListAsync(ct);

        int sent = 0, failed = 0, deadLettered = 0;
        foreach (var message in due)
        {
            try
            {
                await _sender.SendAsync(message, ct);
                message.MarkSent(_clock.UtcNow);
                sent++;
            }
            catch (Exception ex)
            {
                message.MarkFailed(_clock.UtcNow, ex.Message);
                if (message.Status == EmailStatus.Failed)
                {
                    deadLettered++;
                    _logger.LogError(ex, "Email {EmailId} ({Kind}) dead-lettered after {Attempts} attempts.",
                        message.Id, message.Kind, message.AttemptCount);
                }
                else
                {
                    failed++;
                    _logger.LogWarning(ex, "Email {EmailId} ({Kind}) send failed (attempt {Attempts}); will retry.",
                        message.Id, message.Kind, message.AttemptCount);
                }
            }

            // Persist each result so a crash mid-batch can re-send at most one message.
            await _db.SaveChangesAsync(ct);
        }

        if (sent > 0 || failed > 0 || deadLettered > 0)
            _logger.LogInformation("Email dispatch: attempted {Attempted}, sent {Sent}, retrying {Failed}, dead-lettered {Dead}.",
                due.Count, sent, failed, deadLettered);

        return new EmailDispatchSummary(due.Count, sent, failed, deadLettered);
    }
}
