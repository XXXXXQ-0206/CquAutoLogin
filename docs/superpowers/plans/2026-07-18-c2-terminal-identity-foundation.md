# C2 Terminal Identity Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a local-only factory for fresh, non-exportable CNG terminal identities without attempting server registration, browser credential access, tunnel negotiation, or system-network changes.

**Architecture:** `TerminalIdentityFactory` creates a named P-256 ECDSA key in the current user's Microsoft Software Key Storage Provider and returns only the key name, DER SubjectPublicKeyInfo bytes, and a SHA-256 public-key fingerprint. It is not wired into `Program`, IPC, browser bridge, or state transitions; only explicit callers can create it. Tests open the generated key, sign and verify a synthetic payload, and delete the named key in `finally`.

**Tech Stack:** .NET 9 for Windows, `System.Security.Cryptography.Cng`, xUnit.

---

### Task 1: Specify fresh identity behavior with a real CNG test

**Files:**
- Create: `CquVpnCore.Tests/Identity/TerminalIdentityFactoryTests.cs`
- Create: `CquVpnCore/Identity/TerminalIdentity.cs`
- Create: `CquVpnCore/Identity/TerminalIdentityFactory.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Create_generates_a_current_user_P256_identity_that_can_sign_and_verify()
{
    var keyName = $"CquVpnCore.Tests.{Guid.NewGuid():N}";
    var factory = new TerminalIdentityFactory();

    try
    {
        var identity = factory.Create(keyName);

        Assert.Equal(keyName, identity.KeyName);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(identity.SubjectPublicKeyInfo)), identity.PublicKeySha256);
        Assert.NotEmpty(identity.SubjectPublicKeyInfo);

        using var key = CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider);
        Assert.Equal(CngAlgorithm.ECDsaP256.Algorithm, key.Algorithm.Algorithm);
        Assert.False(key.ExportPolicy.HasFlag(CngExportPolicies.AllowPlaintextExport));
        using var algorithm = new ECDsaCng(key);
        var payload = new byte[] { 1, 2, 3, 4 };
        var signature = algorithm.SignData(payload, HashAlgorithmName.SHA256);
        Assert.True(algorithm.VerifyData(payload, signature, HashAlgorithmName.SHA256));
    }
    finally
    {
        DeleteKeyIfPresent(keyName);
    }
}
```

The test helper opens and deletes only the test key name:

```csharp
private static void DeleteKeyIfPresent(string keyName)
{
    if (CngKey.Exists(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider))
    {
        using var key = CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider);
        key.Delete();
    }
}
```

- [ ] **Step 2: Run the focused test and verify it fails because `TerminalIdentityFactory` does not exist**

Run: `dotnet test .\CquVpnCore.Tests\CquVpnCore.Tests.csproj --filter FullyQualifiedName~TerminalIdentityFactoryTests --no-restore`

Expected: compilation failure naming the missing `TerminalIdentityFactory` type.

- [ ] **Step 3: Implement the minimal local-only factory**

```csharp
public sealed record TerminalIdentity(
    string KeyName,
    byte[] SubjectPublicKeyInfo,
    string PublicKeySha256);

public sealed class TerminalIdentityFactory
{
    public TerminalIdentity Create(string keyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyName);
        if (CngKey.Exists(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider))
        {
            throw new InvalidOperationException($"A terminal identity already exists for '{keyName}'.");
        }

        var parameters = new CngKeyCreationParameters
        {
            Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
            ExportPolicy = CngExportPolicies.None
        };
        using var key = CngKey.Create(CngAlgorithm.ECDsaP256, keyName, parameters);
        using var algorithm = new ECDsaCng(key);
        var publicKey = algorithm.ExportSubjectPublicKeyInfo();
        return new TerminalIdentity(keyName, publicKey, Convert.ToHexString(SHA256.HashData(publicKey)));
    }
}
```

- [ ] **Step 4: Run the focused test and verify it passes**

Run: `dotnet test .\CquVpnCore.Tests\CquVpnCore.Tests.csproj --filter FullyQualifiedName~TerminalIdentityFactoryTests --no-restore`

Expected: one test passes and the generated key no longer exists after the test.

### Task 2: Preserve the C2 boundary in documentation and release checks

**Files:**
- Modify: `docs/CquVpnCoreC2Research.zh-CN.md`
- Modify: `docs/CquVpnCore.zh-CN.md`

- [ ] **Step 1: Document the local-only identity foundation**

State that the identity factory is groundwork only: CquVpnCore startup does not invoke it, it does not register with a server, and it does not imply C2 passed or VPN connectivity. State that the private key is not exported and is not derived from a vendor terminal identity.

- [ ] **Step 2: Run full validation**

Run:

```powershell
dotnet test .\CquVpnCore.Tests\CquVpnCore.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\CquAutoLogin.Tests\CquAutoLogin.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\CquAutoLogin.csproj -c Release --no-restore -p:UseSharedCompilation=false
rg -n -i 'httpclient|socket|networkinterface|route|dns|firewall|atrust|sangfor' .\CquVpnCore\Identity
git diff --check
```

Expected: all tests and Release build pass; the identity source has no network, route, DNS, firewall, or vendor references; the diff has no whitespace errors.

- [ ] **Step 3: Commit and update PR #7**

Run:

```powershell
git add CquVpnCore\Identity\TerminalIdentity.cs CquVpnCore\Identity\TerminalIdentityFactory.cs CquVpnCore.Tests\Identity\TerminalIdentityFactoryTests.cs docs\CquVpnCoreC2Research.zh-CN.md docs\CquVpnCore.zh-CN.md docs\superpowers\plans\2026-07-18-c2-terminal-identity-foundation.md
git commit -m "feat: add local terminal identity foundation"
```

If Git HTTPS remains unavailable, update the PR through the Git Database API only after validating the remote parent SHA, each raw Git blob SHA, the resulting tree SHA, and a non-forced ref update.
