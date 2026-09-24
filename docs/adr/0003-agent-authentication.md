# ADR 0003 — Agent authentication

**Status:** Accepted

## Context
Each terminal needs its own revocable identity. Distributing unique client certificates to thousands of ATMs
requires a PKI that not every estate has on day one.

## Decision
- An **admin-issued enrollment token** (limited uses, expiring, stored as SHA-256) is exchanged once for a
  **per-terminal 256-bit agent key**.
- The server stores only the key's SHA-256 hash and compares in constant time. Keys are high-entropy random values,
  so a slow KDF is unnecessary.
- The agent stores the key encrypted with **DPAPI (LocalMachine)** on Windows, or as a 0600 file on Linux.
- The agent presents `X-Agent-Id` and `X-Agent-Key` on every connection. TLS is mandatory; the agent refuses
  `http://` unless explicitly overridden for lab use.
- Revocation takes effect immediately: the admin revokes the key or disables the terminal, the live connection is
  aborted, and reconnects fail. Re-enrolling rotates the key.
- **mTLS at the edge** (reverse proxy verifies a device certificate) is recommended as an additional layer where a
  PKI exists. See SECURITY.md.

## Consequences
- ✅ Works without PKI and supports zero-touch rollout with batch tokens.
- ⚠️ A bearer secret on the device: if the disk is imaged, the key could be replayed (DPAPI mitigates this on
  Windows). The mitigations are mTLS and rotating keys by re-enrolling.
