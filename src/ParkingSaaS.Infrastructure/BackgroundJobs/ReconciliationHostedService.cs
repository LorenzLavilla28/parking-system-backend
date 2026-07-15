using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Application.Payments;

namespace ParkingSaaS.Infrastructure.BackgroundJobs;

/// <summary>
/// Periodically reconciles open payments and expires stale fee quotes. Implemented
/// as a hosted service so the initial deployment needs no extra infrastructure;
/// the work itself lives behind <see cref="IPaymentReconciliationService"/> and can
/// later be driven by Hangfire/Quartz/SQS without changing the business logic.
/// </summary>
public sealed class ReconciliationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PayMongoOptions _options;
    private readonly ILogger<ReconciliationHostedService> _logger;

    public ReconciliationHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<PayMongoOptions> options,
        ILogger<ReconciliationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(15, _options.ReconcileIntervalSeconds));
        _logger.LogInformation("Payment reconciliation running every {Interval}.", interval);

        // Wait one interval before the first run so startup/migrations settle.
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var reconciler = scope.ServiceProvider.GetRequiredService<IPaymentReconciliationService>();
                await reconciler.ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconciliation sweep failed; will retry next interval.");
            }
        }
    }
}
