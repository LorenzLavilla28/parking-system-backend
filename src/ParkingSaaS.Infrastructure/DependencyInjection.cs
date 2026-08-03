using System.IO;
using Amazon;
using Amazon.SecretsManager;
using Amazon.S3;
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
using ParkingSaaS.Infrastructure.TenantAssets;

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
        services.AddOptions<AwsSecretsOptions>().Bind(configuration.GetSection(AwsSecretsOptions.SectionName));
        services.AddOptions<TenantBrandingOptions>()
            .Bind(configuration.GetSection(TenantBrandingOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.BucketName), "TenantBranding:BucketName is required.")
            .Validate(o => o.MaxLogoBytes is > 0 and <= 10 * 1024 * 1024, "TenantBranding:MaxLogoBytes must be between 1 byte and 10 MiB.")
            .ValidateOnStart();
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(o => !o.Enabled ||
                !string.IsNullOrWhiteSpace(o.TenantId)
                && !string.IsNullOrWhiteSpace(o.ClientId)
                && !string.IsNullOrWhiteSpace(o.ClientSecret),
                "Email is enabled but its Microsoft Graph credentials are incomplete.")
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

        services.AddSingleton<IAmazonSecretsManager>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AwsSecretsOptions>>().Value;
            return new AmazonSecretsManagerClient(RegionEndpoint.GetBySystemName(options.Region));
        });
        services.AddSingleton<IAmazonS3>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TenantBrandingOptions>>().Value;
            return new AmazonS3Client(RegionEndpoint.GetBySystemName(options.Region));
        });
        services.AddSingleton<ITenantLogoStorage, S3TenantLogoStorage>();
        services.AddSingleton<IPayMongoCredentialStore, AwsPayMongoCredentialStore>();
        services.AddHttpClient<IPayMongoCredentialValidator, PayMongoCredentialValidator>();

        // PayMongo gateway (typed HttpClient) + background reconciliation.
        services.AddHttpClient<IPaymentGateway, PayMongoPaymentGateway>();
        services.AddHostedService<ReconciliationHostedService>();

        // Microsoft Graph is the only real email transport. When delivery is disabled, a
        // logging no-op keeps queued mail visible in dev/CI. Incomplete Graph credentials
        // fail startup when Enabled=true rather than silently dropping email.
        var email = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
        services.AddHttpClient();
        if (email.Enabled)
            services.AddSingleton<IEmailSender, GraphEmailSender>();
        else
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        services.AddHostedService<EmailDispatchHostedService>();
        services.AddHostedService<OperationsSummaryHostedService>();

        return services;
    }
}
