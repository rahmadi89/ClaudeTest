# Production readiness checklist

Tick every item before go-live. Items marked ⛔ block go-live. The **Status** column shows what the codebase already
provides (✅), what depends on your environment (🔧), and what is not yet built (❌, see ROADMAP).

## Security
| | Item | Status |
|---|---|---|
| ⛔ | TLS on all endpoints; agents configured with `https://` | ✅ enforced by agent · 🔧 certificates |
| ⛔ | `Jwt__SigningKey` ≥ 32 random bytes from a secret store; not in source control | 🔧 |
| ⛔ | `Bootstrap__AdminPassword` removed after first login; admin password changed | 🔧 |
| ⛔ | `Bootstrap__EnrollmentToken` **not** set | 🔧 |
| ⛔ | Named user accounts per operator (no shared logins); least-privilege roles | ✅ RBAC · 🔧 process |
| ⛔ | Log sources reviewed: no PAN / track / PIN data shipped | 🔧 |
| ⛔ | Allow-listed scripts reviewed and code-signed on terminals; `AllowReboot` set deliberately | ✅ mechanism · 🔧 content |
| | mTLS for `/hubs/agent` at the edge | 🔧 |
| | SSO + MFA for operators | ❌ (P1) |
| | Four-eyes approval for reboot / scripts | ❌ (P1) |
| | Pen test / security review signed off | 🔧 |
| | `ReverseProxy__KnownProxies` set so audit and rate-limit IPs are real | 🔧 |
| | Dependency scanning in CI (`dotnet list package --vulnerable`, `npm audit`) | ✅ |

## Reliability
| | Item | Status |
|---|---|---|
| ⛔ | PostgreSQL with automated backups and PITR; restore tested | 🔧 |
| ⛔ | ≥ 2 API instances + Redis backplane (or an accepted single-instance risk) | ✅ supported · 🔧 |
| ⛔ | Load balancer: WebSockets enabled, idle timeout ≥ 120 s, health checks on `/health/ready` | 🔧 |
| ⛔ | Agent installed as a service with recovery actions (install script) | ✅ |
| | Background jobs enabled on exactly one instance for large fleets | 🔧 |
| | Retention periods agreed with compliance (audit ≥ regulatory minimum) | ✅ configurable · 🔧 values |
| | Load test at 2× expected fleet (simulator agents) with results recorded | 🔧 |
| | Disaster-recovery runbook: region failover, agent `ServerUrl` DNS failover | 🔧 |

## Observability
| | Item | Status |
|---|---|---|
| ⛔ | Logs shipped to the central platform / SIEM | ✅ JSON stdout · 🔧 shipping |
| ⛔ | Monitoring of the monitor: health, connected agents, error rate | ✅ signals · 🔧 alerts |
| | OpenTelemetry traces and metrics exported | ✅ `OTEL_EXPORTER_OTLP_ENDPOINT` |
| | Audit log exported to WORM / SIEM | ❌ (P2) |

## Functional
| | Item | Status |
|---|---|---|
| ⛔ | **Hardware provider (XFS / XFS4IoT) for the fleet's vendors implemented and certified** | ❌ (P0) |
| ⛔ | Alert thresholds tuned with operations (`Alerts__*`, `Monitoring__OfflineAfterSeconds`) | ✅ configurable · 🔧 values |
| | Notification channels (email / SMS / Teams / webhook / ITSM) | ❌ (P1) |
| | Terminal metadata (branch, address, vendor, model) loaded | ✅ API/UI · 🔧 data |
| | Operators trained on the runbook (docs/OPERATIONS.md) | 🔧 |

## Delivery
| | Item | Status |
|---|---|---|
| ⛔ | CI green: build (warnings as errors), unit and integration tests, migration drift check, lint, typecheck | ✅ |
| ⛔ | Versioned container images in a private registry; images scanned | ✅ Dockerfiles · 🔧 registry/scanning |
| | Agent binaries code-signed; staged rollout plan (pilot branch → region → fleet) | 🔧 |
| | DB migrations applied via reviewed idempotent script | ✅ supported |
