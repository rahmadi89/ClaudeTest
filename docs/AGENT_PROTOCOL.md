# Agent ↔ Server protocol (v1)

All types live in `src/Shared/AtmMonitor.Contracts` and are shared by both sides. JSON uses camelCase, and enums
are sent as **strings**. The protocol version is `Protocol.Version = 1`, sent by the agent as the
`X-Agent-Protocol` header.

## Endpoints

| Purpose | Transport | Auth |
|---|---|---|
| Enrollment | `POST /api/agent/enroll` | Enrollment token in body. Rate-limited per IP. |
| Live channel | SignalR hub `/hubs/agent` (WebSockets preferred) | Headers `X-Agent-Id` and `X-Agent-Key` |

## Agent → Server (hub methods, invoked with acknowledgement)

| Method | Payload | Semantics |
|---|---|---|
| `ReportStatus` | `StatusReport` | Full snapshot. Last-writer-wins by `CapturedAt`; older reports are ignored but still count as liveness. Sent every `StatusIntervalSeconds` (default 30), immediately on device change, and on reconnect. |
| `ReportLogs` | `LogBatch` (≤ 500 entries) | Appended. Delivered durably from the outbox. |
| `ReportCommandUpdate` | `CommandUpdate` | Status transition for a command. Duplicate or out-of-order updates are ignored by the server's state machine. |

## Server → Agent

| Method | Payload | Semantics |
|---|---|---|
| `ExecuteCommand` | `CommandEnvelope {commandId, type, parameters, issuedAt, expiresAt}` | At-least-once. Resent on every reconnect while the command is `Pending` or `Sent`. The agent executes each `commandId` at most once. |

## Command lifecycle

```
Pending ─► Sent ─► Acknowledged ─► Running ─► Succeeded
   │         │          │             ├──► Failed
   │         │          │             └──► Rejected   (terminal-side policy refused it)
   ├─► Cancelled (operator, only before Acknowledged)
   └─► TimedOut  (server, at ExpiresAt, from any non-final state)
```

The agent reports `Acknowledged` → `Running` → a final state. Output and errors are truncated to 64 KB.

## Status report fields

- `mode`: `InService | OutOfService | Supervisor | Maintenance | Unknown`, as reported by the ATM application.
- `components[]`: `{type, state: Ok|Warning|Error|Offline|Unknown, errorCode, description}`. Device types include
  card reader, dispenser, depository, printers, PIN pad, display, camera, safe door, cabinet door, tamper sensor,
  UPS, recycler and NFC.
- `cassettes[]`: `{cassetteId, type: Dispense|Recycle|Reject|Retract|Deposit, currency, denomination, count, capacity, status: Ok|Low|Empty|High|Full|Missing|Inoperative}`.
- `network`: host reachability, average latency, loss %, interface name, up/down, IP and link speed.
- `system`: CPU %, memory %, system-disk usage, uptime, OS, machine name.

## Compatibility rules

- Adding optional fields is **non-breaking**: both sides ignore unknown JSON members.
- Removing or renaming fields, or changing semantics, requires bumping `Protocol.Version`. The server must accept
  N-1 during rollout, because agents upgrade slowly across a fleet.
- New `CommandType` values: an older agent cannot deserialize an unknown enum value, so the message is dropped on the
  agent (logged) and the command ends `TimedOut`. Roll agents out before operators start using a new command type.
  Gating command types on the reported `agentVersion` is on the roadmap. Agents already answer `Rejected` for
  *known* types they are not configured to run.
