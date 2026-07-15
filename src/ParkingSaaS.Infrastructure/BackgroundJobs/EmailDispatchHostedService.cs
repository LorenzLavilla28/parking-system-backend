using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;

namespace ParkingSaaS.Infrastructure.BackgroundJobs;

/// <summary>
/// Periodically drains the email outbox. Mirrors <see cref="ReconciliationHostedService"/>:
/// a hosted service so the initial deployment needs no extra infrastructure, with the work
/// itself behind <see cref="IEmailDispatchService"/> so it can later be driven by a queue
/// worker without changing behaviour.
/// </summary>
public sealed class EmailDispatchHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmailOptions _options;
    private readonly ILogger<EmailDispatchHostedService> _logger;

    public EmailDispatchHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<EmailOptions> options,
        ILogger<EmailDispatchHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.DispatchIntervalSeconds));
        _logger.LogInformation("Email dispatch running every {Interval}.", interval);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IEmailDispatchService>();
                await dispatcher.DispatchDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email dispatch sweep failed; will retry next interval.");
            }
        }
    }
}
