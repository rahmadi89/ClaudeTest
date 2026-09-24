# Security

ATM monitoring sits next to payment infrastructure. This document covers the threat model, the controls that are
implemented, and the gaps that remain. It is written for bank security and PCI reviewers.

> Scope note: this system never processes card data, PINs or transaction payloads. Keep it that way. Log sources
> must be configured so that no PAN or track data is shipped (see "Log hygiene"). It still sits on the same network
> as cardholder-data environment (CDE) systems, so treat it as **in scope for PCI DSS segmentation review**.

## Threat model (STRIDE summary)

| Threat | Example | Controls |
|---|---|---|
| **Spoofing** an agent | Attacker impersonates a terminal to hide an outage or inject false state | Per-terminal 256-bit key, stored hashed, compared in constant time; TLS required; key revocation with immediate disconnect; optional mTLS at the edge |
| **Spoofing** a user | Credential stuffing | PBKDF2 hashing (ASP.NET Core Identity v3, 100k+ iterations); lockout after 5 failures for 15 min; login rate limit per IP; no user enumeration (uniform 401) |
| **Tampering** with commands | Hijacked server issues malicious commands | Two-sided authorisation (ADR 0004): terminal-local allow-lists, no shell, fixed script arguments, reboot off by default; command expiry; idempotent execution |
| **Repudiation** | Operator denies rebooting a terminal | Append-only audit trail (actor, IP, target, reason); mandatory reason for high-impact commands; retention 7 years (configurable) |
| **Information disclosure** | Logs leak secrets; the API leaks internals | Secrets never logged; enrollment and agent keys shown once and stored hashed; RFC 7807 errors without stack traces; CSP, `X-Frame-Options: DENY`, `nosniff`, `Referrer-Policy: no-referrer`; HSTS outside Development |
| **Denial of service** | Reconnect storms; oversized payloads | Jittered backoff on agents; SignalR max message 1 MB; log batches ≤ 500; paging capped at 500; rate limits on login and enrollment |
| **Elevation of privilege** | Viewer issues commands | Hierarchical policies (Viewer ⊂ Operator ⊂ Admin) with **fallback policy = authenticated Viewer** on every endpoint; agent identities cannot call user APIs (separate scheme and policy); admins cannot demote or deactivate themselves |

## Controls in detail

### Transport
- TLS 1.2+ terminates at the reverse proxy or load balancer, or at Kestrel. Agents refuse `http://` unless
  `AllowInsecureTransport=true`.
- **Recommended:** mTLS between agents and the edge using device certificates from the bank PKI. The proxy
  verifies the client certificate, then forwards to the API. The agent key stays as a second factor.
- `X-Forwarded-*` headers are trusted only from `ReverseProxy:KnownProxies`, so rate limiting and audit IPs cannot
  be spoofed.

### Secrets
| Secret | Where it lives | Rotation |
|---|---|---|
| `Jwt:SigningKey` (≥ 32 bytes) | Secret store (Vault, Azure Key Vault, K8s Secret) → environment variable `Jwt__SigningKey` | Rotating it invalidates all sessions; do it in a maintenance window |
| DB connection string | Secret store → `ConnectionStrings__Default` | Per DBA policy |
| `Bootstrap:AdminPassword` | Only for first start, **then remove** | — |
| Agent keys | Server: SHA-256 hash only. Terminal: DPAPI (Windows) / 0600 file (Linux) | Re-enroll to rotate. Revoke from the console. |
| Enrollment tokens | Server: SHA-256 hash only | Short-lived and limited-use by design |

`Bootstrap:EnrollmentToken` exists for demos and CI. **Never set it in production.**

### Browser session
- JWT access token in `sessionStorage`, 8 h lifetime (one shift), and no refresh token. The strict CSP (no inline
  scripts, `connect-src 'self'`) reduces the XSS risk of storing it in script-accessible storage.
- **Planned (P1):** move to OIDC (Entra ID / Keycloak / ADFS) with a BFF and `HttpOnly` cookies. That gives SSO,
  MFA and central deprovisioning.

### Log hygiene
- The agent ships only the log files you list in `LogSources`. **Do not add** raw XFS traces or application
  journals that contain PAN or track data unless the vendor masks them. Add a masking step if unsure (roadmap: a
  built-in PAN regex scrubber).
- Command output is truncated to 64 KB and visible to all console users. Allow-listed scripts must not print secrets.

## Known gaps / accepted risks

| Gap | Risk | Plan |
|---|---|---|
| No SSO/MFA for operators | Password-only accounts | P1: OIDC integration |
| No dual control (four-eyes) for reboot and scripts | A single admin can act alone (audited) | P1 |
| Agent key is a bearer secret | Replay if extracted from a terminal disk | mTLS at the edge; DPAPI on Windows; key rotation by re-enrollment |
| Agent update channel not signed by this system | Relies on the bank's software distribution | Use code-signing for published binaries (CI step to add) |
| Audit log is append-only by convention, not cryptographically | A DB admin could alter records | Stream audit to a SIEM / WORM storage (P2) |

## Reporting
Report vulnerabilities privately to the platform security team. Do not open public issues.
