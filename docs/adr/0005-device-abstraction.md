# ADR 0005 — Device abstraction and simulator

**Status:** Accepted

## Context
ATM hardware is accessed through vendor stacks: CEN/XFS 3.x (Windows, `msxfs.dll`), the newer XFS4IoT
(WebSocket/JSON), or proprietary SDKs. Behaviour differs per vendor (NCR, Diebold Nixdorf, Hyosung, GRG), and
integration needs the vendor SDK and physical or emulated hardware.

## Decision
The agent depends only on `IDeviceProvider` (snapshot, state-change event, device events, set mode, reset device,
self-test). The repository ships a **realistic simulator**: cash depletion and replenishment, printer, card-reader
and door faults, and mode changes. It powers development, demos, load tests and CI. Hardware providers are
implemented per estate as separate adapters.

## Consequences
- ✅ The whole stack is testable end to end without hardware.
- ⚠️ **Production on real terminals requires implementing an XFS/XFS4IoT provider.** This is roadmap item P0. The
  interface is deliberately small to make that tractable. See AGENT.md § Hardware integration.
