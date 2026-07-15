using System.IO;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Domain.Services;
using ParkingSaaS.Infrastructure.BackgroundJobs;
using ParkingSaaS.Infrastructure.Email;
using ParkingSaaS.Infrastructure.Identity;
using ParkingSaaS.Infrastructure.Payments.PayMongo;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.Infrastructure.Persistence.Interceptors;
using ParkingSaaS.Infrastructure.Security;
using ParkingSaaS.Infrastructure.Sessions;
using ParkingSaaS.Infrastructure.Tenancy;
using ParkingSaaS.Infrastructure.Time;

namespace ParkingSaaS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName)).ValidateOnStart();
        services.AddOptions<LockoutOptions>().Bind(configuration.GetSection(LockoutOptions.SectionName));
        services.AddOptions<PasswordResetOptions>().Bind(configuration.GetSection(PasswordResetOptions.SectionName));
        services.AddOptions<PublicUrlOptions>().Bind(configuration.GetSection(PublicUrlOptions.SectionName));
        services.AddOptions<LookupThrottleOptions>().Bind(configuration.GetSection(LookupThrottleOptions.SectionName));
        services.AddOptions<PricingOptions>().Bind(configuration.GetSection(PricingOptions.SectionName));
        services.AddOptions<PayMongoOptions>().Bind(configuration.GetSection(PayMongoOptions.SectionName));
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.Host),
                "Email:Enabled is true but Email:Host is not set.")
            .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.FromAddress),
                "Email:Enabled is true but Email:FromAddress is not set.")
            .ValidateOnStart();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Default.");

        services.AddHttpContextAccessor();

        // Request-scoped tenant/user/clock services.
        services.AddScoped<ITenantContext, HttpTenantContext>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddSingleton<IDateTime, SystemDateTime>();

        services.AddScoped<AuditAndTenantInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
            options
                .UseNpgsql(connectionString)
                .AddInterceptors(sp.GetRequiredService<AuditAndTenantInterceptor>()));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IRefreshTokenService, RefreshTokenService>();

        // Plate normalization is a pure domain service with no dependencies.
        services.AddSingleton<IPlateNormalizer, PlateNormalizer>();

        // Data Protection keys must persist so reprintable tokens survive restarts.
        // In AWS, point this at an EFS mount or back it with S3/KMS.
        var configuredKeyRingPath = configuration["DataProtection:KeyRingPath"];
        var keyRingPath = string.IsNullOrWhiteSpace(configuredKeyRingPath)
            ? Path.Combine(AppContext.BaseDirectory, "dp-keys")
            : configuredKeyRingPath;
        Directory.CreateDirectory(keyRingPath);
        services.AddDataProtection()
            .SetApplicationName("ParkingSaaS")
            .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));

        services.AddMemoryCache();
        services.AddSingleton<IParkingTokenService, ParkingTokenService>();
        services.AddSingleton<IQrCodeGenerator, QrCodeGenerator>();
        services.AddSingleton<ILookupThrottle, MemoryLookupThrottle>();
        services.AddScoped<ICaptchaVerifier, NoCaptchaVerifier>();

        // PayMongo gateway (typed HttpClient) + background reconciliation.
        services.AddHttpClient<IPaymentGateway, PayMongoPaymentGateway>();
        services.AddHostedService<ReconciliationHostedService>();

        // Email delivery transport: real SMTP when enabled, otherwise a logging no-op so
        // queued mail is visible without a mail server (dev/CI). A misconfigured Enabled=true
        // fails at startup via the EmailOptions validation above rather than silently no-opping.
        var email = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
        if (email.Enabled)
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        else
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        services.AddHostedService<EmailDispatchHostedService>();
        services.AddHostedService<OperationsSummaryHostedService>();

        return services;
    }
}
