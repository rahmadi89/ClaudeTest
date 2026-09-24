# ADR 0001 — SignalR over WebSockets for agent transport

**Status:** Accepted

## Context
Agents must push state continuously and receive commands with low latency. Terminals sit behind NAT and firewalls
that allow outbound HTTPS only. Candidates: HTTP polling, gRPC bidirectional streaming, MQTT, SignalR.

## Decision
Use ASP.NET Core SignalR (WebSockets, with Server-Sent Events and long polling as automatic fallbacks) for agent ↔
server traffic. Use a separate hub for browsers.

## Consequences
- ✅ Outbound-only from terminals. One persistent connection carries both directions, and commands arrive in about 100 ms.
- ✅ First-party .NET client and JS client. Groups (`atm:{id}`) give per-terminal addressing for free, and the Redis backplane scales it out.
- ✅ Works through most corporate proxies thanks to transport fallback.
- ⚠️ Not a durable queue. At-least-once delivery is implemented in the application: server-side command state, a
  resend on reconnect, and an agent-side outbox with de-duplication.
- ⚠️ Sticky sessions are required only for the non-WebSocket fallbacks. Configure the load balancer accordingly.
- Rejected: **MQTT** would add a broker to operate and secure, for no gain at this scale. **gRPC streaming** has
  weaker proxy traversal and needs HTTP/2 end to end. **Polling** gives poor command latency, or heavy load if
  polled fast.
