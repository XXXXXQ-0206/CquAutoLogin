# CquVpnCore Clean-Room Design

## Decision

`CquVpnCore` replaces the installed vendor runtime as the release target. A release that requires `aTrustAgent`, `aTrustTray`, vendor plugins, vendor drivers, copied configuration, or an existing vendor terminal identity is not a CquVpnCore release.

Manual SSO and MFA remain user-operated in the official browser portal. CquVpnCore must not capture, read, store, export, replay, or log passwords, verification codes, cookies, bearer tokens, SID tickets, device secrets, or an existing terminal identity.

## Success Definition

The program is complete only when all of the following are true on a clean Windows installation after the vendor client has been uninstalled:

1. CquAutoLogin starts CquVpnCore without starting any vendor process.
2. The user completes a new manual browser SSO/MFA session.
3. CquVpnCore registers or restores only its own newly created terminal identity through a verified server-supported path.
4. CquVpnCore establishes one authenticated resource connection through its own transport implementation.
5. CquVpnCore applies and removes split routes and DNS policy transactionally.
6. A reboot, reconnect, logout, error path, and uninstall validation all pass without a vendor executable, DLL, driver, registry key, setting, or data file.

Until then, every build and document must label the work as an experimental clean-room gate, not a VPN replacement.

## Non-Goals

- Do not bypass MFA, CAPTCHA, device policy, terminal trust, access control, or server authorization.
- Do not derive a usable profile for Clash, OpenVPN, WireGuard, or another generic client from a vendor session.
- Do not copy, distribute, patch, load, invoke, or re-sign vendor binaries, drivers, Electron resources, configuration, or database files.
- Do not clone a vendor device identifier, certificate, key pair, terminal record, browser session, or cached credential.
- Do not alter system routes, DNS, adapters, firewall state, or services before the transport gate is independently proven.

## Architecture

### CquVpnCore Host

`CquVpnCore` is a separate Windows process with a versioned localhost IPC contract. CquAutoLogin owns the visible controls and receives only high-level states:

```text
Stopped -> AwaitingBrowserLogin -> BrowserLoginComplete ->
TerminalRegistration -> TunnelNegotiation -> Connected -> Disconnecting -> Stopped
```

The IPC contract contains state, non-sensitive error class, operation ID, and progress timestamp. It never contains browser content, credentials, session artifacts, server responses, device values, or packet payloads.

### Portal Bridge

The portal bridge launches the official controller URL in the user's browser and records only an explicit user confirmation that browser authentication was completed. It does not embed the portal, inject scripts, inspect browser storage, listen to browser traffic, or parse redirect parameters.

The bridge can later consume a documented, server-supported handoff to CquVpnCore. A browser success page alone is not considered a handoff contract.

### Compatibility Probe

`CquVpnProbe` is a development-only command that produces redacted evidence for a fresh, user-mediated session. Its schema permits only:

- local process names and parent-process relationship;
- service state transitions;
- endpoint host class, port, protocol version, ALPN, and timing;
- packet direction, length, and framing class without payload bytes;
- adapter state, route count, and DNS server count before and after a transition;
- a boolean user-confirmed browser-login checkpoint.

The probe rejects command lines, HTTP bodies, cookies, headers that carry authorization, certificate private data, database files, browser profiles, and vendor logs. Probe output is local by default and is never bundled in a release.

### Controller Client

The controller client begins with a simulator and only adds a real request after a probe demonstrates the exact request boundary without sensitive material. Each real interaction has an isolated request/response model, timeout, cancellation, and a test fixture using synthetic values.

No interaction may reuse a value read from an installed vendor client. A request that cannot be independently reconstructed from public controller behavior and data newly created by CquVpnCore remains unsupported.

### Terminal Identity

CquVpnCore generates a fresh identity in Windows CNG, using a non-exportable private key where the accepted server registration path permits it. The identity store has a narrow interface with explicit deletion and never imports a vendor key, identifier, cache, or terminal record.

Terminal registration is introduced only after a new identity is accepted by the controller in a manual test. Failed registration leaves no adapter, route, DNS, or firewall state behind.

### Transport and Network Policy

Transport implementation starts after terminal registration and one authenticated resource negotiation are proven. It has a state machine, synthetic-frame tests, bounded reconnect logic, and no reliance on a vendor DLL or driver.

Adapter selection is deferred until this point. Any adapter dependency must be independently licensed, redistributable, and validated on a disposable Windows configuration before it is added to a release. Route and DNS changes use a transaction journal so every failure path restores the previous state.

## Gates

| Gate | Deliverable | Required evidence | Stop condition |
| --- | --- | --- | --- |
| C0 | Contracts, simulator, probe schema | Offline tests pass; probe excludes forbidden fields | No live network work |
| C1 | Browser portal bridge | User can complete SSO/MFA without vendor process being launched by CquAutoLogin | No session extraction |
| C2 | New terminal identity | A newly generated identity is accepted through a verified server path | Do not copy or emulate a vendor identity |
| C3 | Controller and transport prototype | One authenticated resource negotiation with synthetic and live framing evidence | No adapter or route changes before success |
| C4 | Adapter, routes, DNS, reconnect, logout | Disposable-machine integration tests and cleanup evidence | Keep CquVpnCore disabled on failures |
| C5 | Vendor-free release | Reboot and fresh manual login pass after vendor uninstall | Only this gate permits an ATrust-free claim |

## Initial Slice: C0 and C1

The first implementation slice creates the CquVpnCore solution, localhost IPC contracts, a deterministic state machine, a redacting probe model, a browser portal launcher, and an offline simulator. It deliberately does not send controller requests, create an adapter, change routes, or install a driver.

This slice gives CquAutoLogin a vendor-free VPN surface that can report `AwaitingBrowserLogin` and `BrowserLoginComplete` after an explicit user action. It does not claim that browser login has established a tunnel.

## Test Strategy

- Unit tests cover all legal and illegal state transitions, cancellation, IPC serialization, redaction, and identity-store refusal to import external material.
- Integration tests use a local fake controller and synthetic frame fixtures only.
- Live tests require a new manual authentication session and record only the allowed probe schema.
- A release check rejects vendor filenames, vendor paths, credentials, session artifacts, packet payload files, and probe output.

## Migration and Rollback

The current installed-runtime integration is not extended. CquAutoLogin will switch its VPN command to CquVpnCore only after C3 proves an authenticated negotiation. Until C5, the core remains an experimental development feature and does not alter existing vendor installation state.

No new release may describe CquVpnCore as connected, compatible, vendor-free, or ready for uninstall until the corresponding gate has passed.
