# Agent guide

The agent is a .NET 10 worker service that runs on each terminal. It runs as a **Windows Service** on Windows ATMs
and under **systemd** on Linux kiosks. Published self-contained, it needs no .NET runtime on the terminal.

## Responsibilities

| Worker | What it does |
|---|---|
| `ConnectionWorker` | Enrolls if needed, keeps the SignalR connection alive (jittered backoff 1–60 s), re-enrolls if the key is revoked and a token is configured |
| `StatusWorker` | Every `StatusIntervalSeconds`, on device state change, and on request: collects device, cash, network and system state and pushes a `StatusReport` |
| `LogShippingWorker` | Every `LogShipIntervalSeconds`: drains device events, tails configured log files (offsets persisted, rotation-aware) and ships them in batches |
| `CommandWorker` | Executes commands **sequentially**, because devices must not be driven concurrently |

Local state lives in `DataDirectory`:

- `credentials.dat` — agent key. DPAPI-encrypted on Windows.
- `agent.db` — SQLite: outbox, executed-command log and log-file offsets.
- `logs/agent-*.log` — the agent's own logs. Rolled daily, 20 MB per file, 14 files kept.

## Install

### Windows (production terminals)

```powershell
# From the CI artifact atm-monitor-agent-win-x64 (self-contained AtmMonitor.Agent.exe + scripts)
.\install-agent.ps1 -ServerUrl https://atm-monitor.bank.local `
                    -TerminalId ATM-NYC-0001 `
                    -EnrollmentToken <token-from-console> `
                    -TransactionHost switch.bank.local -TransactionHostPort 5000
```

The script copies the binaries to `%ProgramFiles%\AtmMonitor\Agent` and locks `%ProgramData%\AtmMonitor\Agent`
(the data directory) down to SYSTEM and Administrators. It then registers the `AtmMonitorAgent` service with
delayed auto-start and SCM recovery: restart after 10 s, 30 s and 60 s, with `failureflag` so the RestartAgent
command's non-zero exit triggers a restart. Once the terminal appears in the console, remove `EnrollmentToken`
from `appsettings.Production.json`.

Mass rollout: create one enrollment token with `MaxUses` equal to the batch size and a short validity, and
distribute it with your software-distribution tool (SCCM, Intune, or the ATM vendor's distribution suite).

### Linux

Copy the `linux-x64` artifact to `/opt/atm-monitor-agent`, create the `atm-monitor` user, put configuration in
`/etc/atm-monitor-agent/appsettings.json` (picked up through `ATM_AGENT_CONFIG`), install
`deploy/agent/linux/atm-monitor-agent.service`, then run `systemctl enable --now atm-monitor-agent`.

## Configuration reference (`Agent` section)

| Key | Default | Notes |
|---|---|---|
| `ServerUrl` | — | **https** required unless `AllowInsecureTransport=true` (labs only) |
| `TerminalId` | — | `^[A-Za-z0-9_-]{1,32}$`. Must match the terminal ID used on the switch. |
| `EnrollmentToken` | — | Needed only for first enrollment or re-enrollment |
| `DataDirectory` | `data` | Relative paths resolve against the executable's directory |
| `StatusIntervalSeconds` | 30 | 5–3600 |
| `LogShipIntervalSeconds` | 10 | |
| `MaxOutboxItems` | 50000 | Oldest items are dropped beyond this |
| `DeviceProvider` | `Simulator` | See hardware integration below |
| `HostCheck:Host` / `Port` | — / 0 | Transaction host to probe. Port > 0 uses a TCP connect (recommended); 0 uses ICMP. |
| `HostCheck:Samples`, `TimeoutMs` | 4, 2000 | Latency is averaged; loss % = failed / samples |
| `HostCheck:InterfaceName` | auto | NIC to report |
| `LogSources[]` | [] | `{Name, Path, ErrorKeywords[], WarningKeywords[]}`, e.g. the XFS application's electronic journal |
| `Scripts{name}` | {} | `{FileName, Arguments[], TimeoutSeconds}`: the **only** things `RunScript` can run |
| `ApplicationControl:RestartFileName` / `RestartArguments` | — | Vendor procedure to restart the ATM application |
| `AllowReboot` | false | Remote reboot is refused unless this is true |

Every key can also be set through environment variables (`Agent__HostCheck__Host=...`) or the command line.

## Hardware integration (XFS / XFS4IoT)

> **Status:** only the simulator ships today. Connecting to real devices means implementing `IDeviceProvider` for
> your estate's middleware. This is roadmap item P0.

Implementation notes for a CEN/XFS 3.x provider (Windows):

- Open sessions with `WFSStartUp` and `WFSOpen` for the logical services (`IDC` card reader, `CDM` dispenser,
  `PIN`, `PTR` printers, `SIU` sensors and doors, `CIM` depository) through `msxfs.dll`, using P/Invoke on a
  dedicated STA thread. Many vendor service providers require a message window.
- **Snapshot:** use `WFSGetInfo` with `WFS_INF_xxx_STATUS` for device state. For cassettes, use
  `WFS_INF_CDM_CASH_UNIT_INFO` (type, currency, value, count, status such as LOW/EMPTY/MISSING).
- **Events:** register for `WFS_SYSTEM_EVENT`, `WFS_SERVICE_EVENT` and `WFS_USER_EVENT` (e.g.
  `WFS_SRVE_SIU_PORT_STATUS` for door or tamper) → raise `StateChanged` and enqueue `DeviceEvent`s.
- **Mode:** in-service and out-of-service are owned by the ATM *application*, not by XFS. Integrate through the
  vendor's application API (for example the NCR APTRA or Diebold Nixdorf ProCash management interfaces).
  Otherwise report `Unknown` and use `ApplicationControl` scripts.
- **Reset:** `WFSExecute` with `WFS_CMD_xxx_RESET`. Never reset the dispenser during a transaction; check the
  application state first.
- XFS4IoT: the same mapping over the service's WebSocket JSON API, and not tied to Windows.

Register the provider in `Program.cs` behind `Agent:DeviceProvider` and extend the options validation.

## Operating notes

- **Clock:** the server clamps reports more than 5 minutes in the future. Keep NTP configured on terminals.
- **Bandwidth:** about 2–4 KB per status report. At a 30 s interval that is roughly 10 MB per day per terminal,
  plus logs.
- **Linux reboot:** with the hardened unit (`NoNewPrivileges`), `systemctl reboot` needs a polkit rule allowing the
  `atm-monitor` user to call `org.freedesktop.login1.reboot`. Otherwise keep `AllowReboot=false`.
