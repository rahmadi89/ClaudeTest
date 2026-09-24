# ATM Fleet Monitor

Real-time monitoring and remote management for ATM fleets.

- **Agent:** C#/.NET 10 service on each terminal.
- **Server:** ASP.NET Core 10 API with SignalR.
- **Console:** React 19 + TypeScript.

It tracks, per terminal:

- **Terminal health:** Online, Degraded, Out of service, Offline, Maintenance.
- **Devices:** card reader, dispenser, PIN pad, printers, doors, tamper sensor, UPS, and more.
- **Cash:** per cassette, per currency.
- **Network:** host reachability, latency, packet loss, interface state.
- **Host metrics:** CPU, memory, disk, uptime.
- **Logs:** shipped from the terminal.

Alerts are raised and deduplicated automatically. Operators can run **audited, allow-listed remote commands**:
ping, diagnostics, log collection, device reset, in/out of service, application or agent restart, reboot, and
scripts.

```
 ATM + Agent ──WSS (outbound only)──► ASP.NET Core API ◄──HTTPS/WSS── React console
  (Windows Service)                   SignalR · REST · alert engine
  offline buffer, allow-lists         PostgreSQL · (Redis backplane)
```

## Quick start

### Option A — docker compose (everything, 3 simulated ATMs)

```bash
docker compose up --build
```

Open <http://localhost:8080> and sign in as `admin` / `ChangeMe!12345`.

### Option B — local development

Prerequisites: .NET SDK 10, Node 22. No database server is needed: development uses SQLite.

```bash
# 1. API on http://localhost:5080 (creates atm-monitor.dev.db, bootstraps admin / ChangeMe!12345)
dotnet run --project src/Server/AtmMonitor.Api --launch-profile http

# 2. Console on http://localhost:5173 (proxies /api and /hubs to the API)
cd web && npm install && npm run dev

# 3. Create an enrollment token in the console (Administration → Agent enrollment), then start a simulated ATM:
DOTNET_ENVIRONMENT=Development Agent__EnrollmentToken=<token> dotnet run --project src/Agent/AtmMonitor.Agent
#    More terminals: add Agent__TerminalId=ATM-DEV-002 Agent__DataDirectory=./data2
```

### Tests

```bash
dotnet test                              # domain, API integration (incl. SignalR agent round-trip), agent
cd web && npm run lint && npm run typecheck && npm test && npm run build
```

## Documentation

| Document | Contents |
|---|---|
| [Architecture](docs/ARCHITECTURE.md) | Context, components, flows (enrollment, status, commands), data model, scaling |
| [ADRs](docs/adr/) | Key decisions: transport, database, agent auth, command safety, device abstraction |
| [Agent protocol](docs/AGENT_PROTOCOL.md) | Wire contract, delivery semantics, versioning |
| [Agent guide](docs/AGENT.md) | Install (Windows / Linux), configuration, **hardware (XFS) integration** |
| [API](docs/API.md) | REST endpoints and real-time events |
| [Security](docs/SECURITY.md) | Threat model, controls, known gaps |
| [Deployment](docs/DEPLOYMENT.md) | Production topology, configuration reference, database, scaling and HA |
| [Operations runbook](docs/OPERATIONS.md) | Status and alert meanings, playbooks, observability |
| [Production checklist](docs/PRODUCTION_CHECKLIST.md) | Go-live gates, with what is done vs. environment-specific |
| [Roadmap](docs/ROADMAP.md) | What comes next, prioritised |

## Status

v1.0 is a complete, tested foundation: agent, server, console, CI, containers and documentation. Before production
on **real** terminals, one piece remains: a hardware provider for your ATM vendors' CEN/XFS or XFS4IoT stack.
Today the agent ships with a realistic simulator behind a small `IDeviceProvider` interface. See
[ROADMAP](docs/ROADMAP.md) Phase 1 and the [production checklist](docs/PRODUCTION_CHECKLIST.md).

## Repository layout

```
src/Shared/AtmMonitor.Contracts        wire protocol (shared by server + agent)
src/Server/AtmMonitor.Domain           entities & business rules (no framework deps)
src/Server/AtmMonitor.Infrastructure   EF Core, migrations, alert reconciliation
src/Server/AtmMonitor.Api              ASP.NET Core host
src/Agent/AtmMonitor.Agent             terminal agent (Windows Service / systemd)
web/                                   React console
tests/                                 xUnit test projects
deploy/                                Dockerfiles, Windows installer, systemd unit
docs/                                  documentation
```
