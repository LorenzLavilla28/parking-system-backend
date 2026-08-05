using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Domain.Payments;

namespace ParkingSaaS.Application.Payments;

public sealed class PaymentCheckoutCleanupService : IPaymentCheckoutCleanupService
{
    private readonly IApplicationDbContext _db;
    private readonly IPaymentGateway _gateway;
    private readonly ILogger<PaymentCheckoutCleanupService> _logger;

    public PaymentCheckoutCleanupService(
        IApplicationDbContext db,
        IPaymentGateway gateway,
        ILogger<PaymentCheckoutCleanupService> logger)
    {
        _db = db;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task CloseOpenCheckoutsAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var openPayments = await _db.Payments
            .IgnoreQueryFilters()
            .Where(p => p.ParkingSessionId == sessionId
                        && p.Provider == PaymentProvider.PayMongo
                        && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing))
            .ToListAsync(cancellationToken);

        foreach (var payment in openPayments)
        {
            if (payment.ProviderCheckoutSessionId is not { Length: > 0 } checkoutId)
            {
                payment.Cancel();
                continue;
            }

            try
            {
                await _gateway.ExpireCheckoutAsync(payment.TenantId, checkoutId, cancellationToken);
                payment.Cancel();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not close checkout {CheckoutId} before closing session {SessionId}.",
                    checkoutId, sessionId);
                throw new ConflictException(
                    "The session still has an open online payment. Please try again after the payment provider responds.");
            }
        }
    }
}
