using System.Diagnostics.Metrics;
using System.Security.Claims;
using System.Text.Json.Serialization;
using AtmMonitor.Api;
using AtmMonitor.Api.Auth;
using AtmMonitor.Api.Background;
using AtmMonitor.Api.Hubs;
using AtmMonitor.Api.Services;
using AtmMonitor.Contracts;
using AtmMonitor.Domain.Entities;
using AtmMonitor.Infrastructure;
using AtmMonitor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;

// Container health probe without curl in the image: `dotnet AtmMonitor.Api.dll --healthcheck`.
if (args.Contains("--healthcheck"))
{
    var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0] ?? "8080";
    using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    try
    {
        using var res = await probe.GetAsync(new Uri($"http://127.0.0.1:{port}/health/ready"));
        return res.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException)
    {
        return 1;
    }
    catch (TaskCanceledException)
    {
        return 1;
    }
}

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "AtmMonitor.Api"));

// ---------- Options (validated at startup so misconfiguration fails fast) ----------
builder.Services.AddOptions<JwtOptions>().Bind(builder.Configuration.GetSection(JwtOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<MonitoringOptions>().Bind(builder.Configuration.GetSection(MonitoringOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<RetentionOptions>().Bind(builder.Configuration.GetSection(RetentionOptions.Section)).ValidateDataAnnotations().ValidateOnStart();

// ---------- Core services ----------
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<AgentConnectionRegistry>();
builder.Services.AddSingleton<TelemetryThrottle>();
builder.Services.AddScoped<AuditLogger>();
builder.Services.AddScoped<DashboardNotifier>();
builder.Services.AddScoped<StatusIngestionService>();
builder.Services.AddScoped<CommandService>();

if (builder.Configuration.GetValue("BackgroundJobs:Enabled", true))
{
    builder.Services.AddHostedService<OfflineWatchdog>();
    builder.Services.AddHostedService<CommandTimeoutWorker>();
    builder.Services.AddHostedService<RetentionWorker>();
}

// ---------- AuthN / AuthZ ----------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        o.MapInboundClaims = true;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = JwtTokenService.CreateKey(jwt.SigningKey),
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
        };
        o.Events = new JwtBearerEvents
        {
            // Browsers cannot set headers on WebSocket requests; SignalR sends the token as a query parameter.
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments(Protocol.DashboardHubPath))
                {
                    ctx.Token = token;
                }

                return Task.CompletedTask;
            },
        };
    })
    .AddScheme<AuthenticationSchemeOptions, AgentKeyAuthenticationHandler>(AgentKeyAuthenticationHandler.SchemeName, null);

builder.Services.AddAuthorization(Policies.Register);
builder.Services.AddRateLimiter(o => RateLimits.Configure(o, builder.Configuration));

// ---------- Web ----------
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var signalR = builder.Services
    .AddSignalR(o =>
    {
        o.MaximumReceiveMessageSize = 1024 * 1024;
        o.EnableDetailedErrors = builder.Environment.IsDevelopment();
        o.KeepAliveInterval = TimeSpan.FromSeconds(15);
        o.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    })
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

if (builder.Configuration.GetConnectionString("Redis") is { Length: > 0 } redis)
{
    // Required when running more than one API instance so commands and dashboard events reach every connection.
    signalR.AddStackExchangeRedis(redis, o => o.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("atm-monitor"));
}

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Only trust X-Forwarded-* from explicitly configured proxies, otherwise clients could spoof their IP.
    foreach (var proxy in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
    {
        o.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
    }
});

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

// ---------- Observability ----------
var meter = new Meter("AtmMonitor.Api");
builder.Services.AddSingleton(meter);
if (builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is { Length: > 0 })
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("atm-monitor-api"))
        .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddOtlpExporter())
        .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentationIfAvailable()
            .AddMeter(meter.Name).AddOtlpExporter());
}

var app = builder.Build();

var registry = app.Services.GetRequiredService<AgentConnectionRegistry>();
meter.CreateObservableGauge("atm_monitor.agents.connected", () => registry.Count, description: "Agent connections held by this instance");

// ---------- Database ----------
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await DbInitializer.InitializeAsync(
        scope.ServiceProvider.GetRequiredService<AppDbContext>(),
        scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(),
        scope.ServiceProvider.GetRequiredService<TimeProvider>(),
        app.Logger,
        app.Configuration["Bootstrap:AdminPassword"],
        app.Configuration["Bootstrap:EnrollmentToken"]);
}

// ---------- Pipeline ----------
app.UseForwardedHeaders();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h.XContentTypeOptions = "nosniff";
    h.XFrameOptions = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";
    if (!ctx.Request.Path.StartsWithSegments("/scalar") && !ctx.Request.Path.StartsWithSegments("/openapi"))
    {
        h.ContentSecurityPolicy = "default-src 'self'; connect-src 'self' ws: wss:; img-src 'self' data:; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'";
    }

    await next();
});

app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
    ex is not null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
    : ctx.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose
    : Serilog.Events.LogEventLevel.Information);

// The React console can be served by this process (single deployable) or by a separate web server.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHub<AgentHub>(Protocol.AgentHubPath);
app.MapHub<DashboardHub>(Protocol.DashboardHubPath);

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.MapFallbackToFile("index.html").AllowAnonymous();

await app.RunAsync();
return 0;

internal static class OpenTelemetryExtensions
{
    public static MeterProviderBuilder AddRuntimeInstrumentationIfAvailable(this MeterProviderBuilder b) =>
        b.AddMeter("System.Runtime");
}

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
