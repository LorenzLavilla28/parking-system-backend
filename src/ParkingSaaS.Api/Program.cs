using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Api.Middleware;
using ParkingSaaS.Api.Realtime;
using ParkingSaaS.Api.Validation;
using ParkingSaaS.Application;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Infrastructure;
using ParkingSaaS.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging (Serilog, structured) -----------------------------------------
builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext());

// ---- Options ---------------------------------------------------------------
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Missing Jwt configuration.");

// ---- Application + Infrastructure ------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ---- Realtime (self-hosted SignalR) ----------------------------------------
builder.Services.AddSignalR();
builder.Services.AddSingleton<ISessionRealtimeNotifier, SignalRSessionNotifier>();

// ---- Controllers, validation, JSON -----------------------------------------
builder.Services.AddScoped<ValidationFilter>();
builder.Services
    .AddControllers(options => options.Filters.Add<ValidationFilter>())
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();

// ---- Swagger / OpenAPI with bearer auth ------------------------------------
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "ParkingSaaS API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new()
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header
    });
    c.AddSecurityRequirement(new()
    {
        [new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>()
    });
});

// ---- Authentication / Authorization ----------------------------------------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };

        // Browsers can't set the Authorization header on a WebSocket, so the
        // SignalR JS client passes the access token as a query-string parameter.
        // Accept it only for hub requests.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options => options.AddParkingPolicies());

// ---- Rate limiting (stricter on auth) --------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // Coarse network-level limit on public plate lookups; the application-level
    // ILookupThrottle adds CAPTCHA escalation and per-plate blocking on top.
    options.AddPolicy("public-lookup", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// ---- CORS (SPA origins only) -----------------------------------------------
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options => options.AddPolicy("spa", policy =>
{
    policy.WithOrigins(corsOrigins)
          .AllowAnyHeader()
          .AllowAnyMethod()
          .AllowCredentials() // required for the SignalR browser handshake
          .WithExposedHeaders(CorrelationIdMiddleware.HeaderName);
}));

builder.Services.AddHealthChecks();

var app = builder.Build();

// ---- Database migration + dev seed -----------------------------------------
// Database:ResetOnStartup wipes and rebuilds the DB on every boot. It only ever
// takes effect in Development so a stray config can never destroy real data.
var resetOnStartup = app.Environment.IsDevelopment()
    && builder.Configuration.GetValue<bool>("Database:ResetOnStartup");

await DatabaseInitializer.InitializeAsync(
    connectionString: builder.Configuration.GetConnectionString("Default")!,
    seedDevelopmentData: app.Environment.IsDevelopment(),
    resetData: resetOnStartup,
    loggerFactory: app.Services.GetRequiredService<ILoggerFactory>(),
    passwordHasher: app.Services.GetRequiredService<IPasswordHasher>());

// ---- Middleware pipeline ----------------------------------------------------
app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors("spa");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<SessionsHub>("/hubs/sessions");

app.Run();

// Exposed so a future WebApplicationFactory-based test host can reference it.
public partial class Program;
