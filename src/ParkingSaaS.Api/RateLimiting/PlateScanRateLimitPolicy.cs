using System.Threading.RateLimiting;
using ParkingSaaS.Application.Common.Options;

namespace ParkingSaaS.Api.RateLimiting;

public static class PlateScanRateLimitPolicy
{
    public const string Name = "plate-scan";

    public static ConcurrencyLimiterOptions CreateOptions(PlateScanningOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new ConcurrencyLimiterOptions
        {
            PermitLimit = options.MaxConcurrentScans,
            QueueLimit = options.MaxQueuedScans,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        };
    }
}
