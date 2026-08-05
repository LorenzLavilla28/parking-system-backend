using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Audit;
using ParkingSaaS.Application.Auth;
using ParkingSaaS.Application.Customer;
using ParkingSaaS.Application.Emails;
using ParkingSaaS.Application.Guard;
using ParkingSaaS.Application.Locations;
using ParkingSaaS.Application.Payments;
using ParkingSaaS.Application.Pricing;
using ParkingSaaS.Application.RatePlans;
using ParkingSaaS.Application.Reports;
using ParkingSaaS.Application.Supervisor;
using ParkingSaaS.Application.Tenants;
using ParkingSaaS.Application.Users;
using ParkingSaaS.Domain.Pricing;

namespace ParkingSaaS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>(ServiceLifetime.Scoped);

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();
        services.AddScoped<ITenantBrandingService, TenantBrandingService>();

        services.AddScoped<IGuardEntryService, GuardEntryService>();
        services.AddScoped<IGuardSessionService, GuardSessionService>();
        services.AddScoped<IGuardLocationService, GuardLocationService>();
        services.AddScoped<ICustomerPublicService, CustomerPublicService>();

        // Pricing engine: the calculator is a pure, stateless domain service.
        services.AddSingleton<IParkingFeeCalculator, ParkingFeeCalculator>();
        services.AddScoped<IActiveRatePlanResolver, ActiveRatePlanResolver>();
        services.AddScoped<ISessionPricingService, SessionPricingService>();
        services.AddScoped<IRatePlanService, RatePlanService>();
        services.AddScoped<IDashboardReportService, DashboardReportService>();
        services.AddScoped<IOperationsSummaryService, OperationsSummaryService>();
        services.AddScoped<ICustomerPricingService, CustomerPricingService>();

        // Payments (Phase 4).
        services.AddScoped<IPaymentSettler, PaymentSettler>();
        services.AddScoped<IPaymentCheckoutCleanupService, PaymentCheckoutCleanupService>();
        services.AddScoped<ICustomerPaymentService, CustomerPaymentService>();
        services.AddScoped<IPaymentWebhookService, PaymentWebhookService>();
        services.AddScoped<IPaymentReconciliationService, PaymentReconciliationService>();
        services.AddScoped<IPaymentTrackingService, PaymentTrackingService>();
        services.AddScoped<IPayMongoConnectionService, PayMongoConnectionService>();
        services.AddScoped<IPayMongoCredentialsResolver, PayMongoCredentialsResolver>();

        // Email queue (transactional outbox + background dispatcher).
        services.AddScoped<IEmailQueue, EmailQueue>();
        services.AddScoped<IEmailDispatchService, EmailDispatchService>();

        // Exit, cash & overrides (Phase 5).
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IGuardExitService, GuardExitService>();
        services.AddScoped<IGuardCashPaymentService, GuardCashPaymentService>();
        services.AddScoped<ISupervisorOverrideService, SupervisorOverrideService>();

        return services;
    }
}
