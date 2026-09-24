# REST API

The OpenAPI document is served at `/openapi/v1.json`, with an interactive reference at `/scalar`, in the
Development environment. All endpoints require a bearer JWT from `POST /api/auth/login` unless marked anonymous.
Errors are RFC 7807 `application/problem+json`. Enums are strings.

| Method & path | Role | Description |
|---|---|---|
| `POST /api/auth/login` | anonymous, rate-limited | `{userName, password}` → `{accessToken, expiresAt, user}` |
| `GET /api/auth/me` | Viewer | Current user |
| `POST /api/auth/change-password` | Viewer | `{currentPassword, newPassword}` (min. 12 characters) |
| `GET /api/dashboard/summary` | Viewer | Fleet KPIs, status and alert breakdown, cash by currency, recent alerts |
| `GET /api/atms?status=&search=&city=&page=&pageSize=` | Viewer | Paged terminal list |
| `GET /api/atms/{id}` | Viewer | Full detail: devices, cassettes, network, system |
| `POST /api/atms` · `PUT /api/atms/{id}` · `DELETE /api/atms/{id}` | Admin | Register, edit, delete |
| `POST /api/atms/{id}/enable` · `/disable` · `/revoke-agent` | Admin | Lifecycle and key revocation (disconnects the agent) |
| `GET /api/atms/{id}/telemetry?from=&to=` | Viewer | ≤ 31 days, ≤ 5000 points |
| `GET /api/atms/{id}/logs?minSeverity=&search=&from=&to=&page=` | Viewer | Paged, newest first |
| `GET /api/atms/{id}/commands` | Viewer | Command history for a terminal |
| `POST /api/atms/{id}/commands` | Operator / Admin (per type) | `{type, parameters?, reason?}` → `202` + command |
| `GET /api/commands?status=&atmId=` · `GET /api/commands/{id}` | Viewer | |
| `POST /api/commands/{id}/cancel` | Operator | Only while Pending or Sent |
| `GET /api/alerts?active=&status=&severity=&atmId=` | Viewer | |
| `POST /api/alerts/{id}/acknowledge` · `/resolve` | Operator | `{note?}` |
| `GET/POST /api/admin/enrollment-tokens` · `DELETE /api/admin/enrollment-tokens/{id}` | Admin | The secret is returned once on create |
| `GET/POST /api/admin/users` · `PUT /api/admin/users/{id}` · `POST /api/admin/users/{id}/reset-password` | Admin | |
| `GET /api/admin/audit?actor=&action=` | Admin | `action` is a prefix, e.g. `command.` |
| `POST /api/agent/enroll` | anonymous (enrollment token), rate-limited | See AGENT_PROTOCOL.md |
| `GET /health/live` · `GET /health/ready` | anonymous | Liveness, and readiness (database) |

**Real-time:** SignalR hub `/hubs/dashboard` (JWT as the `access_token` query parameter) pushes `AtmUpdated`,
`AlertChanged` and `CommandChanged`. Clients should treat these as hints to refetch.
