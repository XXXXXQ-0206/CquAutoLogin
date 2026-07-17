# Browser Bridge Setup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the browser bridge's one-time user-consented setup reachable directly from CquAutoLogin without pretending that a local extension can be silently installed on Windows.

**Architecture:** Keep the existing Native Messaging registration and fixed extension ID unchanged. Add a small, injectable launcher that opens the current bridge directory and `chrome://extensions`, falling back to `edge://extensions` only when the Chrome URI cannot be opened. The tray command retains its existing identifier, so no IPC or persisted setting contract changes.

**Tech Stack:** .NET 9, WPF/WinForms tray integration, xUnit.

---

### Task 1: Define setup-launch behavior with tests

**Files:**
- Create: `CquAutoLogin.Tests/Services/BrowserBridgeSetupLauncherTests.cs`
- Create: `Services/BrowserBridgeSetupLauncher.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void Open_launches_the_bridge_directory_then_the_Chrome_extensions_page()
{
    var launched = new List<ProcessStartInfo>();
    var launcher = new BrowserBridgeSetupLauncher(launched.Add);
    var directory = Path.Combine(Path.GetTempPath(), $"CquAutoLogin.Tests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        launcher.Open(directory);

        Assert.Collection(
            launched,
            item => Assert.Equal(Path.GetFullPath(directory), item.FileName),
            item => Assert.Equal("chrome://extensions", item.FileName));
        Assert.All(launched, item => Assert.True(item.UseShellExecute));
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

[Fact]
public void Open_uses_the_Edge_extensions_page_when_Chrome_cannot_be_opened()
{
    var launched = new List<ProcessStartInfo>();
    var launcher = new BrowserBridgeSetupLauncher(info =>
    {
        launched.Add(info);
        if (info.FileName == "chrome://extensions")
        {
            throw new Win32Exception("Chrome is unavailable.");
        }
    });
    var directory = Path.Combine(Path.GetTempPath(), $"CquAutoLogin.Tests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        launcher.Open(directory);

        Assert.Equal(new[] { Path.GetFullPath(directory), "chrome://extensions", "edge://extensions" },
            launched.Select(item => item.FileName));
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}
```

- [ ] **Step 2: Run the focused test assembly and verify the tests fail because `BrowserBridgeSetupLauncher` does not exist**

Run: `dotnet test .\CquAutoLogin.Tests\CquAutoLogin.Tests.csproj --filter FullyQualifiedName~BrowserBridgeSetupLauncherTests --no-restore`

Expected: compilation failure naming the missing `BrowserBridgeSetupLauncher` type.

- [ ] **Step 3: Implement the minimal launcher**

```csharp
public sealed class BrowserBridgeSetupLauncher
{
    private readonly Action<ProcessStartInfo> _start;

    public BrowserBridgeSetupLauncher(Action<ProcessStartInfo>? start = null)
    {
        _start = start ?? static info => Process.Start(info);
    }

    public void Open(string bridgeDirectory)
    {
        var directory = Path.GetFullPath(bridgeDirectory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("The browser bridge directory was not found.", directory);
        }

        Start(directory);
        try
        {
            Start("chrome://extensions");
        }
        catch (Win32Exception)
        {
            Start("edge://extensions");
        }
    }

    private void Start(string target) => _start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
}
```

- [ ] **Step 4: Re-run the focused test assembly and verify it passes**

Run: `dotnet test .\CquAutoLogin.Tests\CquAutoLogin.Tests.csproj --filter FullyQualifiedName~BrowserBridgeSetupLauncherTests --no-restore`

Expected: two tests pass.

### Task 2: Route the tray command through setup launcher

**Files:**
- Modify: `App.xaml.cs:232-234,335-356`
- Modify: `Services/TrayIconService.cs:251`
- Modify: `CquAutoLogin.Tests/Services/TrayVpnCommandContractTests.cs`

- [ ] **Step 1: Write the failing tray wiring assertion**

```csharp
[Fact]
public void Browser_bridge_tray_action_uses_the_setup_launcher()
{
    Assert.Contains("new BrowserBridgeSetupLauncher().Open(directory)", FindSourceFile("App.xaml.cs"));
    Assert.Contains("设置浏览器桥接", FindSourceFile(Path.Combine("Services", "TrayIconService.cs")));
}
```

- [ ] **Step 2: Run the focused tray contract tests and verify the new assertion fails before the launcher is wired**

Run: `dotnet test .\CquAutoLogin.Tests\CquAutoLogin.Tests.csproj --filter FullyQualifiedName~TrayVpnCommandContractTests --no-restore`

Expected: failure because `App.xaml.cs` does not reference `BrowserBridgeSetupLauncher` and the menu source still says `打开浏览器桥接目录`.

- [ ] **Step 3: Replace the folder-only action with setup launcher invocation**

```csharp
private static void OpenBrowserBridgeFolder()
{
    var directory = Path.Combine(AppContext.BaseDirectory, "Assets", "BrowserBridge");
    try
    {
        new BrowserBridgeSetupLauncher().Open(directory);
    }
    catch (DirectoryNotFoundException)
    {
        ShowNonFatalMessage("未找到浏览器桥接文件。请重新安装 CquAutoLogin。");
    }
    catch (Exception exception)
    {
        ShowNonFatalMessage($"无法打开浏览器桥接设置：{exception.Message}");
    }
}
```

Use the menu label `设置浏览器桥接` for `IdOpenBrowserBridgeFolder`; retain `TrayMenuCommand.OpenBrowserBridgeFolder` so callers and tests remain compatible. Add `FindSourceFile` to the test class using the existing parent-directory traversal pattern.

- [ ] **Step 4: Re-run the focused tray tests and verify they pass**

Run: `dotnet test .\CquAutoLogin.Tests\CquAutoLogin.Tests.csproj --filter FullyQualifiedName~TrayVpnCommandContractTests --no-restore`

Expected: all selected tests pass.

### Task 3: Update the user-facing boundary and verify the release

**Files:**
- Modify: `docs/BrowserAuthBridge.zh-CN.md`
- Modify: `README.md`
- Modify: `README.zh-CN.md`

- [ ] **Step 1: Document the one-time setup accurately**

State that selecting `设置浏览器桥接` opens the unpacked bridge directory and the browser's extension manager. State that Chrome's Windows distribution rules require the user to enable an unpacked extension or use a future Chrome Web Store listing; the app does not set force-install policy. Keep the existing prohibition on reading browser credentials and on treating bridge reports as VPN connectivity.

- [ ] **Step 2: Run release validation**

Run:

```powershell
dotnet test .\CquVpnCore.Tests\CquVpnCore.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet test .\CquAutoLogin.Tests\CquAutoLogin.Tests.csproj --no-restore -p:UseSharedCompilation=false
node --check .\Assets\BrowserBridge\background.js
node --check .\Assets\BrowserBridge\content-script.js
dotnet build .\CquAutoLogin.csproj -c Release --no-restore -p:UseSharedCompilation=false
git diff --check
```

Expected: both test projects pass, JavaScript syntax checks pass, Release build has zero warnings/errors, and the diff has no whitespace errors.

- [ ] **Step 3: Commit and update the existing PR**

Run:

```powershell
git add App.xaml.cs Services\BrowserBridgeSetupLauncher.cs Services\TrayIconService.cs CquAutoLogin.Tests\Services\BrowserBridgeSetupLauncherTests.cs CquAutoLogin.Tests\Services\TrayVpnCommandContractTests.cs docs\BrowserAuthBridge.zh-CN.md README.md README.zh-CN.md docs\superpowers\plans\2026-07-18-browser-bridge-setup.md
git commit -m "feat: streamline browser bridge setup"
```

If Git HTTPS remains unavailable, create the equivalent tree and commit through the GitHub Git Database API only after validating parent SHA, every changed blob SHA, tree SHA, and the non-forced ref update.
