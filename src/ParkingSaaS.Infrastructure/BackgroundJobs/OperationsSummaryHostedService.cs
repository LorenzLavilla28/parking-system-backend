using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Application.Reports;

namespace ParkingSaaS.Infrastructure.BackgroundJobs;

/// <summary>
/// Queues the latest operations digest for each active tenant. The email outbox
/// dispatcher sends the queued messages independently of this scheduler.
/// </summary>
public sealed class OperationsSummaryHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmailOptions _options;
    private readonly ILogger<OperationsSummaryHostedService> _logger;

    public OperationsSummaryHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<EmailOptions> options,
        ILogger<OperationsSummaryHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.OperationsSummaryEnabled)
        {
            _logger.LogInformation("Operations summary scheduler is disabled.");
            return;
        }

        var hours = Math.Clamp(_options.OperationsSummaryIntervalHours, 1, 24);
        var interval = TimeSpan.FromHours(hours);
        _logger.LogInformation("Operations summary scheduler running every {Interval}.", interval);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var summaries = scope.ServiceProvider.GetRequiredService<IOperationsSummaryService>();
                var queued = await summaries.QueueScheduledEmailsAsync(hours, stoppingToken);
                _logger.LogInformation("Operations summary scheduler queued {Recipients} recipient email(s).", queued);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Operations summary sweep failed; will retry next interval.");
            }
        }
    }
}
