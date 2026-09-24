# ADR 0004 — Two-sided command authorisation

**Status:** Accepted

## Context
Remote commands on ATMs (reboot, out-of-service, scripts) are high impact. A compromised operator account or a
compromised server must not be able to run arbitrary code on the fleet.

## Decision
- **Server side:** role-based (`CommandPolicy`). Operators get diagnostics, service mode, device reset and agent
  restart. Admins additionally get application restart, reboot and scripts. High-impact commands require a
  free-text *reason*. Every issue and cancel is written to the audit trail with the actor and IP address.
- **Terminal side:** the agent enforces its **own** configuration:
  - `RunScript` runs only named entries from `Agent:Scripts`, with fixed arguments and no shell. Server-supplied
    arguments are ignored.
  - `RebootMachine` requires `Agent:AllowReboot=true`.
  - `RestartApplication` requires `Agent:ApplicationControl` to be configured.
  - `CollectLogs` reads only configured sources or the agent's own log.
- Commands carry an expiry, and the agent rejects expired commands. Execution is idempotent by command ID.

## Consequences
- ✅ A server compromise cannot become remote code execution beyond what each terminal already allows.
- ⚠️ Adding a new script needs a configuration change on the terminal (through software distribution). This is intended.
- Future: dual control (four-eyes approval) for reboot and scripts. See ROADMAP.
