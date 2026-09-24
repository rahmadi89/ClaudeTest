# Deployment

## Reference production topology

```
Internet ✗     Branch VLANs (ATMs) ──outbound 443──►┐
                                                     ▼
                          ┌────────────── DMZ / edge ──────────────┐
                          │ Load balancer / reverse proxy (TLS,     │
                          │ optional mTLS for /hubs/agent,          │
                          │ WebSocket upgrade, idle timeout ≥ 120s) │
                          └──────────────┬──────────────────────────┘
                                         ▼
                    ┌── App tier (2+ instances for HA) ───┐
                    │ atm-monitor-api containers           │──► Redis (SignalR backplane)
                    └──────────────┬───────────────────────┘
                                   ▼
                        PostgreSQL (primary + replica, PITR backups)
```

- **Operators** reach the console over the internal network (VPN or zero-trust proxy). Do not expose the console to
  the internet.
- **Agents** need outbound HTTPS to one hostname only.

## Container image

`deploy/docker/server.Dockerfile` builds a single image: API plus the React console as static files. It runs as the
non-root `app` user on port 8080, and the built-in health probe is `dotnet AtmMonitor.Api.dll --healthcheck`.

```bash
docker build -f deploy/docker/server.Dockerfile -t registry.bank.local/atm-monitor-api:1.0.0 .
```

### Kubernetes probes

```yaml
livenessProbe:  { httpGet: { path: /health/live,  port: 8080 }, periodSeconds: 10 }
readinessProbe: { httpGet: { path: /health/ready, port: 8080 }, periodSeconds: 10 }
```

`/health/ready` checks database connectivity. Both endpoints are anonymous and expose no details.

## Configuration reference (server)

Supply secrets as environment variables from your secret store. `__` separates configuration sections.

| Key | Default | Notes |
|---|---|---|
| `ConnectionStrings__Default` | — | **Required.** Npgsql connection string. |
| `ConnectionStrings__Redis` | empty | Set when running more than one instance |
| `Database__Provider` | `Postgres` | `Sqlite` for dev only |
| `Database__MigrateOnStartup` | `true` | Set `false` and run migrations as a release job for strict change control (below) |
| `Jwt__SigningKey` | — | **Required**, ≥ 32 chars, random |
| `Jwt__Issuer` / `Jwt__Audience` | `atm-monitor` | |
| `Jwt__AccessTokenMinutes` | 480 | |
| `Bootstrap__AdminPassword` | empty | First start only. Creates `admin` if no users exist. |
| `Monitoring__OfflineAfterSeconds` | 120 | Should be ≥ 3 × the agent status interval |
| `Monitoring__TelemetrySampleSeconds` | 60 | Stored resolution of telemetry |
| `Alerts__DiskUsedWarningPercent` / `PacketLossWarningPercent` / `LatencyWarningMs` | 90 / 20 / 500 | |
| `Retention__TelemetryDays` / `LogDays` / `ResolvedAlertDays` / `CommandDays` / `AuditDays` | 30/90/180/180/2555 | |
| `RateLimits__LoginPerMinute` / `EnrollPerMinute` | 10 / 30 | Per client IP |
| `BackgroundJobs__Enabled` | `true` | See scaling |
| `ReverseProxy__KnownProxies__0` | — | IP of your proxy, so client IPs are trusted from it |
| `Cors__AllowedOrigins__0` | — | Only if the console is served from another origin |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | — | Enables OpenTelemetry traces and metrics export |
| `Serilog__MinimumLevel__Default` | `Information` | Logs are compact JSON on stdout |

## Database

- PostgreSQL 15+. Create a dedicated database and a least-privilege role that owns the schema.
- Migrations live in `src/Server/AtmMonitor.Infrastructure/Persistence/Migrations`. For controlled releases,
  generate an idempotent script and have a DBA apply it:
  ```bash
  dotnet tool restore
  dotnet ef migrations script --idempotent -p src/Server/AtmMonitor.Infrastructure -s src/Server/AtmMonitor.Infrastructure -o migrate.sql
  ```
  Then deploy with `Database__MigrateOnStartup=false`.
- Backups: daily base backup plus WAL archiving (PITR). Test restores quarterly.
- On the first start against an empty database, EF Core logs one `Failed executing DbCommand` error while it probes
  for the migrations history table. This is expected.

## Scaling and HA

| Fleet size | Suggested layout |
|---|---|
| ≤ 2,000 terminals | 2 API instances (HA) + Redis backplane, PostgreSQL 2 vCPU / 8 GB |
| 2,000–10,000 | 3–4 API instances; background jobs enabled on one instance only; PostgreSQL 4–8 vCPU; consider telemetry partitioning |
| > 10,000 | Shard by region (one stack per region), TimescaleDB for telemetry, and a federation view (roadmap) |

- **Load balancer:** enable WebSockets and set an idle timeout ≥ 120 s (agents send keep-alives every 15 s). Sticky
  sessions are only needed if WebSockets are blocked and SignalR falls back to long polling.
- **Rolling deploys:** agents reconnect automatically with jitter, so a restart reconnects the fleet within about
  60 s. Stage the rollout to avoid mass `Offline` alerts. `OfflineAfterSeconds` (120 s by default) absorbs a normal
  restart.

## Local evaluation (docker compose)

```bash
docker compose up --build
# http://localhost:8080  →  admin / ChangeMe!12345
```

This starts PostgreSQL, the API and console, and three simulated ATMs that enroll through a bootstrap token.
Override `ADMIN_PASSWORD`, `JWT_SIGNING_KEY`, `DB_PASSWORD` and `ENROLLMENT_TOKEN` through the environment or an
`.env` file.
