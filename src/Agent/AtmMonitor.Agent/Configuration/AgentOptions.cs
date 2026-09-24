using System.ComponentModel.DataAnnotations;

namespace AtmMonitor.Agent.Configuration;

public sealed class AgentOptions
{
    public const string Section = "Agent";

    /// <summary>Base URL of the monitoring server, e.g. https://atm-monitor.bank.local. Must be HTTPS in production.</summary>
    [Required, Url] public string ServerUrl { get; set; } = "";

    /// <summary>Terminal ID as configured on the switch. Unique per ATM.</summary>
    [Required, RegularExpression("^[A-Za-z0-9_-]{1,32}$")] public string TerminalId { get; set; } = "";

    /// <summary>One-time token used only for the first enrollment (or re-enrollment after key revocation).</summary>
    public string? EnrollmentToken { get; set; }

    /// <summary>Where credentials, the offline buffer and log offsets are stored. Relative paths resolve against the executable.</summary>
    [Required] public string DataDirectory { get; set; } = "data";

    [Range(5, 3600)] public int StatusIntervalSeconds { get; set; } = 30;
    [Range(1, 3600)] public int LogShipIntervalSeconds { get; set; } = 10;

    /// <summary>Maximum buffered outbound messages while offline; oldest are dropped beyond this.</summary>
    [Range(100, 1_000_000)] public int MaxOutboxItems { get; set; } = 50_000;

    /// <summary>Allow plain HTTP to the server. Only for development / lab setups.</summary>
    public bool AllowInsecureTransport { get; set; }

    /// <summary>"Simulator" for development/demo. Real hardware requires a vendor XFS/XFS4IoT provider (see docs/AGENT.md).</summary>
    [Required] public string DeviceProvider { get; set; } = "Simulator";

    public HostCheckOptions HostCheck { get; set; } = new();
    public List<LogSourceOptions> LogSources { get; set; } = [];

    /// <summary>Named scripts that the RunScript command may execute. Nothing outside this list can ever run.</summary>
    public Dictionary<string, ScriptOptions> Scripts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ApplicationControlOptions ApplicationControl { get; set; } = new();

    /// <summary>Remote reboot is off unless explicitly enabled on the terminal itself.</summary>
    public bool AllowReboot { get; set; }
}

public sealed class HostCheckOptions
{
    /// <summary>Transaction host / switch to probe. Empty disables the host check.</summary>
    public string? Host { get; set; }

    /// <summary>TCP port to probe (preferred – ICMP is often blocked). 0 = use ICMP ping.</summary>
    [Range(0, 65535)] public int Port { get; set; }

    [Range(1, 20)] public int Samples { get; set; } = 4;
    [Range(100, 30000)] public int TimeoutMs { get; set; } = 2000;

    /// <summary>Network interface to report. Empty = first operational non-loopback interface.</summary>
    public string? InterfaceName { get; set; }
}

public sealed class LogSourceOptions
{
    [Required] public string Name { get; set; } = "";
    /// <summary>File to tail (e.g. the XFS application journal).</summary>
    [Required] public string Path { get; set; } = "";
    /// <summary>Lines containing any of these (case-insensitive) are reported as Error.</summary>
    public List<string> ErrorKeywords { get; set; } = ["error", "fatal", "exception"];
    public List<string> WarningKeywords { get; set; } = ["warn"];
}

public sealed class ScriptOptions
{
    [Required] public string FileName { get; set; } = "";
    public List<string> Arguments { get; set; } = [];
    [Range(1, 3600)] public int TimeoutSeconds { get; set; } = 120;
}

public sealed class ApplicationControlOptions
{
    /// <summary>Command that restarts the ATM application, e.g. a vendor-supplied script. Empty = not supported.</summary>
    public string? RestartFileName { get; set; }
    public List<string> RestartArguments { get; set; } = [];
}
