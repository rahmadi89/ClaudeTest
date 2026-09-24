namespace AtmMonitor.Contracts;

/// <summary>Overall health of a terminal as derived by the server.</summary>
public enum AtmStatus
{
    Unknown = 0,
    Online = 1,
    Degraded = 2,
    OutOfService = 3,
    Offline = 4,
    Maintenance = 5,
}

/// <summary>Operational mode reported by the ATM application itself.</summary>
public enum OperationalMode
{
    Unknown = 0,
    InService = 1,
    OutOfService = 2,
    Supervisor = 3,
    Maintenance = 4,
}

public enum ComponentType
{
    CardReader = 0,
    CashDispenser = 1,
    Depository = 2,
    ReceiptPrinter = 3,
    JournalPrinter = 4,
    PinPad = 5,
    Display = 6,
    Camera = 7,
    SafeDoor = 8,
    CabinetDoor = 9,
    TamperSensor = 10,
    Ups = 11,
    CashRecycler = 12,
    Nfc = 13,
}

public enum ComponentState
{
    Unknown = 0,
    Ok = 1,
    Warning = 2,
    Error = 3,
    Offline = 4,
}

public enum CassetteType
{
    Dispense = 0,
    Recycle = 1,
    Reject = 2,
    Retract = 3,
    Deposit = 4,
}

public enum CassetteStatus
{
    Unknown = 0,
    Ok = 1,
    Low = 2,
    Empty = 3,
    High = 4,
    Full = 5,
    Missing = 6,
    Inoperative = 7,
}

public enum AlertSeverity
{
    Info = 0,
    Warning = 1,
    Major = 2,
    Critical = 3,
}

public enum AlertStatus
{
    Open = 0,
    Acknowledged = 1,
    Resolved = 2,
}

public enum AlertType
{
    AtmOffline = 0,
    ComponentFault = 1,
    ComponentWarning = 2,
    CashLow = 3,
    CashEmpty = 4,
    RejectBinFull = 5,
    NetworkDegraded = 6,
    HostUnreachable = 7,
    DiskSpaceLow = 8,
    SecurityBreach = 9,
    OutOfService = 10,
    CommandFailed = 11,
}

public enum CommandType
{
    /// <summary>Round-trip liveness check.</summary>
    Ping = 0,
    /// <summary>Force the agent to collect and push a full status report now.</summary>
    RefreshStatus = 1,
    /// <summary>Upload recent log entries / a named log file.</summary>
    CollectLogs = 2,
    /// <summary>Run built-in diagnostics (network, disk, devices).</summary>
    RunDiagnostics = 3,
    /// <summary>Ask the ATM application to go out of service.</summary>
    SetOutOfService = 4,
    /// <summary>Ask the ATM application to go back in service.</summary>
    SetInService = 5,
    /// <summary>Restart the ATM application (vendor XFS app).</summary>
    RestartApplication = 6,
    /// <summary>Restart the monitoring agent process.</summary>
    RestartAgent = 7,
    /// <summary>Reboot the whole terminal. High impact.</summary>
    RebootMachine = 8,
    /// <summary>Execute a script from the agent's local allow-list (never arbitrary code).</summary>
    RunScript = 9,
    /// <summary>Reset a single device (e.g. card reader) via the device provider.</summary>
    ResetDevice = 10,
}

public enum CommandStatus
{
    Pending = 0,
    Sent = 1,
    Acknowledged = 2,
    Running = 3,
    Succeeded = 4,
    Failed = 5,
    TimedOut = 6,
    Cancelled = 7,
    Rejected = 8,
}

public enum LogSeverity
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
}

public static class CommandStatusExtensions
{
    public static bool IsTerminal(this CommandStatus status) => status is
        CommandStatus.Succeeded or CommandStatus.Failed or CommandStatus.TimedOut or
        CommandStatus.Cancelled or CommandStatus.Rejected;
}
