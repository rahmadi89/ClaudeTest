# Operations runbook

## Terminal statuses

| Status | Meaning | Typical action |
|---|---|---|
| **Online** | Reporting, in service, no issues | — |
| **Degraded** | In service with a non-critical issue: printer warning, low cash, a full reject bin, host latency or loss | Plan a visit or replenishment |
| **Out of service** | The application is out of service, or a critical device (card reader, dispenser, PIN pad, display) has failed | Dispatch or remote reset |
| **Maintenance** | Supervisor or maintenance mode (technician on site) | — |
| **Offline** | No traffic for `OfflineAfterSeconds` | See "Terminal offline" |
| **Unknown** | Registered but has never reported | Check the agent install |

## Alert catalogue

| Alert | Severity | Auto-resolves | First response |
|---|---|---|---|
| AtmOffline | Critical | Yes, on reconnect | Check the branch network or power. Ask the branch whether the screen is on. |
| ComponentFault (critical device) | Critical | Yes | Run `ResetDevice`. If it recurs, dispatch the CE. |
| ComponentFault (other) / ComponentWarning | Major / Warning | Yes | e.g. receipt paper low → branch staff |
| SecurityBreach (safe door, cabinet door, tamper) | Critical | Yes | **Follow the physical-security procedure.** Call the branch or security desk, check CCTV, consider `SetOutOfService`. |
| CashEmpty (cassette) / CashEmpty (all) | Major / Critical | Yes | Cash-in-transit (CIT) replenishment |
| CashLow | Warning | Yes | Schedule CIT |
| RejectBinFull | Major | Yes | Empty the reject bin at the next visit |
| HostUnreachable | Critical | Yes | Check the switch or host link. Other terminals in the same branch point to a network problem. |
| NetworkDegraded | Warning | Yes | Watch the trend; open a network ticket if it persists |
| DiskSpaceLow | Warning | Yes | Run the `cleanup` script (if allow-listed) or dispatch |
| OutOfService | Major | Yes | Find out why (look at other alerts). `SetInService` when fixed. |
| CommandFailed | Warning | **No** | Read the command output. Resolve the alert manually once handled. |

Acknowledging marks an alert as "someone is on it". Resolving closes it; if the condition is still present, a new
alert is opened.

## Playbooks

### Terminal offline
1. Check whether **other terminals in the same branch** are offline too. If so, it is a branch network or power issue.
2. Check the agent log on the terminal (via remote desktop or a technician):
   `%ProgramData%\AtmMonitor\Agent\logs\agent-YYYYMMDD.log`.
3. `Enrollment rejected` or `Server rejected the agent key` → the key was revoked or the terminal disabled.
   Re-enable it, or re-enroll with a new token.
4. Repeated `Connection attempt failed` → DNS, TLS, proxy or firewall between the branch and the data centre.

### Mass offline (many terminals at once)
Almost always a server-side or network event.
1. Check `/health/ready` and the API logs, then database health, then load-balancer WebSocket settings (idle timeout).
2. After recovery, agents reconnect within about 60 s thanks to jittered backoff. Do not bulk-restart agents.

### Stuck command (stays Sent or Running)
- `Sent` with the agent offline: it will be delivered on reconnect, or reach `TimedOut` at expiry. Cancel it if it
  is no longer wanted.
- `Running` for a long time: the agent executes commands one at a time, so a long script blocks the queue. It times
  out at `ExpiresAt`.

### Suspected compromised terminal
1. `Revoke agent key`, and **Disable** the terminal in the console to stop re-enrollment.
2. `SetOutOfService` if the agent is still reachable. Otherwise dispatch.
3. Export the audit log and command history for the investigation.

### Rotate the JWT signing key
Set the new `Jwt__SigningKey` and restart all instances. All users must sign in again. Agents are unaffected.

## Observability

- **Logs:** compact JSON on stdout (Serilog). Correlate by `TraceId`. Ship to the SIEM.
- **Metrics:** set `OTEL_EXPORTER_OTLP_ENDPOINT` to export ASP.NET Core, HTTP client, runtime and
  `atm_monitor.agents.connected` metrics.
- **Suggested SLOs:** status freshness (95% of terminals reported < 90 s ago); alert latency (offline detected
  < `OfflineAfterSeconds` + 15 s); command dispatch (p95 < 2 s to `Acknowledged` for connected terminals).
- **Alert on the monitor itself:** `/health/ready` failing; `agents.connected` dropping > 20% in 5 min; error log rate.
