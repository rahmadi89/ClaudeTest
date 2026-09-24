using System.Threading.Channels;
using AtmMonitor.Agent;
using AtmMonitor.Agent.Commands;
using AtmMonitor.Agent.Configuration;
using AtmMonitor.Agent.Connectivity;
using AtmMonitor.Agent.Devices;
using AtmMonitor.Agent.Identity;
using AtmMonitor.Agent.Logs;
using AtmMonitor.Agent.Monitoring;
using AtmMonitor.Agent.Storage;
using AtmMonitor.Agent.Workers;
using AtmMonitor.Contracts;
using Microsoft.Extensions.Options;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Optional machine-specific config file outside the install dir (e.g. /etc/atm-monitor-agent/appsettings.json),
// so upgrades can replace the install directory wholesale. Environment variables and args still win.
if (Environment.GetEnvironmentVariable("ATM_AGENT_CONFIG") is { Length: > 0 } extraConfig)
{
    builder.Configuration.AddJsonFile(extraConfig, optional: false, reloadOnChange: false);
    builder.Configuration.AddEnvironmentVariables();
    builder.Configuration.AddCommandLine(args);
}
builder.Services.AddWindowsService(o => o.ServiceName = "AtmMonitorAgent");
builder.Services.AddSystemd();

builder.Services.AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.Section))
    .ValidateDataAnnotations()
    .Validate(o => o.DeviceProvider.Equals("Simulator", StringComparison.OrdinalIgnoreCase),
        "Agent:DeviceProvider must be 'Simulator'. Hardware providers (CEN/XFS, XFS4IoT) are vendor-specific; see docs/AGENT.md.")
    .ValidateOnStart();

// Resolve the data directory against the executable, not the working directory (services start in System32).
var agentSection = builder.Configuration.GetSection(AgentOptions.Section);
var dataDir = agentSection["DataDirectory"] ?? "data";
if (!Path.IsPathRooted(dataDir))
{
    dataDir = Path.Combine(AppContext.BaseDirectory, dataDir);
    agentSection["DataDirectory"] = dataDir;
}

Directory.CreateDirectory(dataDir);

builder.Services.AddSerilog(cfg => cfg
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("TerminalId", agentSection["TerminalId"] ?? "")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(Path.Combine(dataDir, "logs", "agent-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14,
        fileSizeLimitBytes: 20 * 1024 * 1024, rollOnFileSizeLimit: true, shared: true));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ICredentialStore>(sp => new FileCredentialStore(dataDir, sp.GetRequiredService<ILogger<FileCredentialStore>>()));
builder.Services.AddSingleton(sp => new LocalStore(Path.Combine(dataDir, "agent.db"), sp.GetRequiredService<IOptions<AgentOptions>>().Value.MaxOutboxItems));
builder.Services.AddHttpClient<EnrollmentClient>(c => c.Timeout = TimeSpan.FromSeconds(30)).AddStandardResilienceHandler();

builder.Services.AddSingleton<IDeviceProvider, SimulatedDeviceProvider>(_ => new SimulatedDeviceProvider());
builder.Services.AddSingleton<ISystemMetricsCollector, SystemMetricsCollector>();
builder.Services.AddSingleton<INetworkProbe, NetworkProbe>();
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<StatusCollector>();
builder.Services.AddSingleton<LogCollector>();
builder.Services.AddSingleton<StatusTrigger>();
builder.Services.AddSingleton<ServerConnection>();
builder.Services.AddSingleton<Messenger>();
builder.Services.AddSingleton<CommandExecutor>();
builder.Services.AddSingleton(Channel.CreateBounded<CommandEnvelope>(new BoundedChannelOptions(100) { SingleReader = true }));

builder.Services.AddHostedService<ConnectionWorker>();
builder.Services.AddHostedService<StatusWorker>();
builder.Services.AddHostedService<LogShippingWorker>();
builder.Services.AddHostedService<CommandWorker>();

var host = builder.Build();
host.Services.GetRequiredService<ILogger<Program>>()
    .LogInformation("ATM Monitor Agent {Version} starting (protocol v{Protocol}), data dir {DataDir}", AgentInfo.Version, Protocol.Version, dataDir);
await host.RunAsync();
