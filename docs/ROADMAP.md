# Roadmap

Priorities: **P0** blocks production on real hardware · **P1** expected by operations or security for a bank
rollout · **P2** scale and polish.

## Phase 0 — Foundation ✅ (this release, v1.0)
- Agent: enrollment, secure key storage, resilient connection, offline outbox, status / cash / network / system
  collection, log tailing, allow-listed commands, Windows Service / systemd, simulator.
- Server: agent and dashboard hubs, REST API, JWT + RBAC, alert engine (dedup, auto-resolve), command lifecycle
  with at-least-once delivery, offline watchdog, retention, audit, rate limiting, health checks, OpenTelemetry,
  PostgreSQL migrations.
- Console: dashboard, terminal list and detail (devices, cash, network, telemetry charts, logs, commands, alerts),
  alert workflow, command dialog, admin (enrollment tokens, users, audit), live updates, light and dark themes.
- Delivery: 49 .NET tests (domain, API integration incl. SignalR round-trip, agent) + web unit tests, CI, Dockerfiles, compose demo, installers, documentation.

## Phase 1 — Pilot on real terminals (≈ 6–8 weeks)
| Pri | Item | Notes |
|---|---|---|
| P0 | **CEN/XFS 3.x device provider** for the pilot vendor (e.g. NCR or Diebold Nixdorf) | `IDeviceProvider`; see AGENT.md § Hardware integration. Includes the vendor application API for in-service and out-of-service. |
| P0 | Pilot at 5–10 terminals in 1–2 branches; tune thresholds | |
| P1 | **Notification channels**: email, SMS, Teams/Slack, generic webhook, ServiceNow/ITSM incident creation | Routing by severity, region and time of day; escalation if not acknowledged |
| P1 | **OIDC SSO + MFA** (Entra ID / Keycloak / ADFS), cookie BFF | Replaces local passwords; group → role mapping |
| P1 | **Four-eyes approval** for RebootMachine, RunScript, RestartApplication | Second operator approves within N minutes |
| P1 | PAN / track-data scrubber in the agent log pipeline | Luhn-validated regex masking before shipping |
| P1 | Command-type gating by agent version | Prevents sending commands to agents that do not understand them |

## Phase 2 — Fleet rollout (≈ 2–3 months)
| Pri | Item |
|---|---|
| P1 | XFS4IoT provider; second vendor provider |
| P1 | Agent self-update channel (signed packages, staged rings) |
| P1 | Maintenance windows (suppress alerts; scheduled out-of-service) |
| P2 | Cash forecasting: days-to-empty per terminal from withdrawal rate; CIT route suggestions |
| P2 | Transaction counters (approved / declined / reversals) from the application journal → availability and business KPIs |
| P2 | Map view (terminal coordinates are already stored) |
| P2 | Bulk commands with rate control (e.g. collect logs from a region) |
| P2 | Saved views and filters; CSV export; scheduled PDF reports (availability SLA per branch) |

## Phase 3 — Scale and compliance
| Pri | Item |
|---|---|
| P2 | TimescaleDB or native partitioning for telemetry and logs; continuous aggregates |
| P2 | Audit stream to SIEM / WORM storage with hash chaining |
| P2 | Leader election for background jobs (Postgres advisory lock) instead of a config flag |
| P2 | Multi-region federation view |
| P2 | Playwright E2E suite in CI; k6/NBomber load-test suite driving 10k simulated agents |
| P2 | Localisation (i18n) of the console |
