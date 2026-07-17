# CquVpnCore C0/C1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver an offline-testable, vendor-free CquVpnCore process that launches the browser portal and reports only C0/C1 authentication states to CquAutoLogin.

**Architecture:** `CquVpnCore.Contracts` owns the versioned named-pipe request/status types. `CquVpnCore` hosts a deterministic state machine and a local named-pipe server; its only external action is opening the fixed portal URI in the user's browser. CquAutoLogin starts that host hidden and renders its status, but does not inspect browser data, send controller traffic, or modify network configuration.

**Tech Stack:** .NET 9, Windows named pipes, `System.Text.Json`, xUnit.

---

## Files

- Create `CquVpnCore.Contracts/CquVpnCore.Contracts.csproj` for the DTO-only shared library.
- Create `CquVpnCore/CquVpnCore.csproj` for the WinExe named-pipe host.
- Create `CquVpnCore.Tests/CquVpnCore.Tests.csproj` for C0/C1 contract, state, and IPC tests.
- Create `Services/CquVpnCoreClient.cs` for the UI-side hidden host/process client.
- Modify `App.xaml.cs`, `Services/TrayIconService.cs`, and `CquAutoLogin.csproj` to remove vendor launch calls from VPN tray actions and copy the core host to the application output.

### Task 1: Define safe contracts and prove the redaction boundary

**Files:**

- Create `CquVpnCore.Contracts/VpnCoreContracts.cs`.
- Create `CquVpnCore.Contracts/RedactedProbeSchema.cs`.
- Create `CquVpnCore.Tests/Contracts/RedactedProbeSchemaTests.cs`.

- [ ] Write the failing test:

```csharp
[Fact]
public void Serialize_rejects_sensitive_field_names()
{
    var exception = Assert.Throws<InvalidOperationException>(
        () => RedactedProbeSerializer.Serialize(new { token = "secret" }));
    Assert.Contains("token", exception.Message, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] Run `dotnet test .\CquVpnCore.Tests\CquVpnCore.Tests.csproj --filter FullyQualifiedName~RedactedProbeSchemaTests` and confirm it fails because the serializer is absent.
- [ ] Add `VpnCoreState` with only `Stopped`, `AwaitingBrowserLogin`, and `BrowserLoginComplete`; `VpnCoreRequest`, `VpnCoreStatus`, protocol version `1`, and the explicit probe records for topology, endpoint metadata, framing metadata, network counts, and browser-confirmed checkpoint.
- [ ] Implement `RedactedProbeSerializer` so it recursively rejects `password`, `token`, `cookie`, `credential`, `secret`, `ticket`, `authorization`, `header`, `payload`, and `commandline` member names.
- [ ] Re-run the filtered test and confirm it passes.
- [ ] Commit only these new contract/test files with `feat: add vendor-free VPN core contracts`.

### Task 2: Implement the deterministic C0/C1 state machine

**Files:**

- Create `CquVpnCore/State/VpnCoreStateMachine.cs`.
- Create `CquVpnCore/State/VpnCoreTransitionException.cs`.
- Create `CquVpnCore.Tests/State/VpnCoreStateMachineTests.cs`.

- [ ] Write the failing test:

```csharp
[Fact]
public void ConfirmBrowserLogin_requires_waiting_state()
{
    var machine = new VpnCoreStateMachine(TimeProvider.System);
    Assert.Throws<VpnCoreTransitionException>(() => machine.ConfirmBrowserLogin());
}
```

- [ ] Run `dotnet test .\CquVpnCore.Tests\CquVpnCore.Tests.csproj --filter FullyQualifiedName~VpnCoreStateMachineTests` and confirm it fails because the state machine is absent.
- [ ] Implement `BeginBrowserLogin`, `ConfirmBrowserLogin`, `Stop`, and `GetStatus`. Permit `Stopped -> AwaitingBrowserLogin`, `AwaitingBrowserLogin -> BrowserLoginComplete`, and `any -> Stopped` only.
- [ ] Include a fresh operation ID on every browser-login request, UTC timestamps from injected `TimeProvider`, and non-sensitive error text for illegal transitions.
- [ ] Re-run the filtered test and confirm it passes.
- [ ] Commit the core state files and tests with `feat: add VPN core C0 C1 state machine`.

### Task 3: Add the standalone named-pipe host and portal launcher

**Files:**

- Create `CquVpnCore/Program.cs`.
- Create `CquVpnCore/Ipc/NamedPipeVpnCoreServer.cs`.
- Create `CquVpnCore/Ipc/NamedPipeVpnCoreClient.cs`.
- Create `CquVpnCore/Portal/IBrowserPortalLauncher.cs`.
- Create `CquVpnCore/Portal/ShellBrowserPortalLauncher.cs`.
- Create `CquVpnCore.Tests/Ipc/NamedPipeVpnCoreServerTests.cs`.

- [ ] Write the failing round-trip test:

```csharp
[Fact]
public async Task BeginBrowserLogin_returns_waiting_status()
{
    var launcher = new RecordingPortalLauncher();
    await using var server = await TestServer.StartAsync(launcher);
    var status = await server.Client.SendAsync(VpnCoreRequest.BeginBrowserLogin());
    Assert.Equal(VpnCoreState.AwaitingBrowserLogin, status.State);
    Assert.Equal(new Uri("https://atrust.cqu.edu.cn/portal/"), Assert.Single(launcher.OpenedUris));
}
```

- [ ] Run `dotnet test .\CquVpnCore.Tests\CquVpnCore.Tests.csproj --filter FullyQualifiedName~NamedPipeVpnCoreServerTests` and confirm it fails because the IPC host is absent.
- [ ] Implement one JSON request and one JSON response per named-pipe connection. Accept only `GetStatus`, `BeginBrowserLogin`, `ConfirmBrowserLogin`, and `Stop`; reject unknown protocol versions and commands with a non-sensitive error class.
- [ ] Make `ShellBrowserPortalLauncher` use `ProcessStartInfo { FileName = "https://atrust.cqu.edu.cn/portal/", UseShellExecute = true }`; no other endpoint is accepted in C1.
- [ ] Re-run all core tests and confirm no test creates adapters, routes, DNS entries, services, or outbound controller requests.
- [ ] Commit the host with `feat: host clean-room VPN core over named pipes`.

### Task 4: Route CquAutoLogin to CquVpnCore instead of ATrust

**Files:**

- Modify `CquAutoLogin.csproj` and `CquAutoLogin.Tests/CquAutoLogin.Tests.csproj`.
- Modify `App.xaml.cs` and `Services/TrayIconService.cs`.
- Create `Services/CquVpnCoreClient.cs`.
- Create `CquAutoLogin.Tests/Services/CquVpnCoreClientTests.cs`.

- [ ] Write the failing test:

```csharp
[Fact]
public async Task BeginBrowserLogin_starts_host_and_returns_waiting_state()
{
    var host = new RecordingCoreHost();
    var client = new CquVpnCoreClient(host, new FakePipeClient(VpnCoreState.AwaitingBrowserLogin));
    var status = await client.BeginBrowserLoginAsync(CancellationToken.None);
    Assert.True(host.Started);
    Assert.Equal(VpnCoreState.AwaitingBrowserLogin, status.State);
}
```

- [ ] Run `dotnet test .\CquAutoLogin.Tests\CquAutoLogin.Tests.csproj --filter FullyQualifiedName~CquVpnCoreClientTests` and confirm it fails because the new client is absent.
- [ ] Implement the hidden `CquVpnCore.exe` launch, bounded pipe readiness retry, and C0/C1 request forwarding. Do not reference `ATrustClientService`, vendor executables, vendor services, or vendor state.
- [ ] Make `App.ConnectVpnAsync` call `BeginBrowserLoginAsync`; make status refresh query the core client only. Display `等待浏览器认证（实验阶段，尚未建立 VPN 隧道）` and `浏览器认证已确认（等待后续连接能力）` for the two active states.
- [ ] Add a tray command that sends the explicit `ConfirmBrowserLogin` action. It records only the user's confirmation; it does not infer a session or claim connectivity.
- [ ] Copy `CquVpnCore.exe`, `.dll`, `.deps.json`, and `.runtimeconfig.json` to the CquAutoLogin build output through MSBuild, then re-run the focused client and tray tests.
- [ ] Commit the UI handoff with `feat: route VPN browser login through CquVpnCore`.

### Task 5: Verify and document the C0/C1 boundary

**Files:**

- Create `docs/CquVpnCore.zh-CN.md`.
- Modify `docs/ATrustIntegration.zh-CN.md` and `RELEASE_NOTES.md`.

- [ ] Publish with `dotnet publish .\CquVpnCore\CquVpnCore.csproj -c Release`.
- [ ] Run `rg -i --files-with-matches 'aTrust|Sangfor' .\CquVpnCore\bin\Release\net9.0-windows`; treat any match as a release-boundary failure.
- [ ] Run `dotnet test .\CquVpnCore.Tests\CquVpnCore.Tests.csproj` and `dotnet test .\CquAutoLogin.Tests\CquAutoLogin.Tests.csproj`.
- [ ] Document exactly that C0/C1 launch a browser and record explicit user confirmation only; they do not create a VPN tunnel, consume browser credentials, alter network settings, or support uninstalling the vendor client.
- [ ] Commit documentation with `docs: describe CquVpnCore experimental C0 C1 boundary`.
# Status update (2026-07-18)

> This C0/C1 implementation plan has been superseded for browser authentication by `docs/BrowserAuthBridge.zh-CN.md`: the current code uses an optional visible-state browser bridge and no longer provides a manual confirmation command.
