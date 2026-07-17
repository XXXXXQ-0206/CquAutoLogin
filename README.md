# CquAutoLogin

<p align="right">
  <a href="./README.zh-CN.md"><img alt="Chinese README" src="https://img.shields.io/badge/README-%E4%B8%AD%E6%96%87-blue"></a>
</p>

CquAutoLogin is a small Windows tray utility that monitors CQU portal sign-in status and helps reconnect when the current device has no valid portal session.

It can:

- watch Ethernet and `CQU_Wifi` network changes;
- detect whether the current device is already online in the portal;
- submit a portal sign-in request when no valid session is found;
- run silently from Windows startup;
- expose quick actions from a tray menu.

## Experimental VPN Core

The bundled `CquVpnCore` C0/C1 slice starts no official VPN service, Agent, or Tray. It opens the browser portal for user-operated SSO/MFA and can automatically detect a conservative visible authentication state through the optional browser bridge.

The bridge sends a page-independent `bridge-ready` acknowledgement when it starts, then reports only `unknown`, `auth-required`, or `authenticated` for the visible portal state. It does **not** read browser cookies, storage, passwords, tokens, authorization headers, or network bodies. For one-time setup, choose `Set up browser bridge` from the Campus VPN tray submenu; it opens the current bridge directory and the browser's extension manager. The user confirms the browser-controlled first enablement, and the app does not write a force-install policy. The core does **not** establish a VPN tunnel, modify routes or DNS, or replace an official client. Its tray state deliberately says that further connection capability is pending. See [the browser bridge note](./docs/BrowserAuthBridge.zh-CN.md) and [the Chinese CquVpnCore note](./docs/CquVpnCore.zh-CN.md) for the precise boundary.

## Current Portal Notes

The portal integration currently targets:

- portal host: `login.cqu.edu.cn`
- default portal base URL: `http://login.cqu.edu.cn:801`
- fallback migration from the older `https://login.cqu.edu.cn:802` setting

Portal systems may change without notice. If endpoints or parameters change, this project may need to be updated.

## Requirements

- Windows 10/11
- .NET 9 SDK for building from source
- Access to the relevant CQU portal environment

## Build

```powershell
dotnet build .\CquAutoLogin.csproj
```

## Publish

```powershell
dotnet publish .\CquAutoLogin.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
```

The self-contained publish directory includes both `CquAutoLogin.exe` and `CquVpnCore.exe`.

## Configuration

On first launch, the app creates a settings file under the current Windows user's local application data directory:

```text
%LocalAppData%\CquAutoLogin\settings.json
```

Fill in your own authorized portal account and password:

```json
{
  "Username": "your-account",
  "Password": "your-password"
}
```

Do not commit or publish your personal `settings.json`.

## Logs

Runtime logs are written to:

```text
%LocalAppData%\CquAutoLogin\Logs
```

## Disclaimer

This project is an unofficial personal utility. It is not affiliated with, endorsed by, sponsored by, or maintained by CQU or any network service provider.

Use it only on networks and accounts that you are authorized to access. You are responsible for complying with your institution's network policies, acceptable-use rules, and applicable laws. The authors and contributors are not responsible for account issues, network access interruptions, policy violations, data loss, or any other damages arising from the use of this software.

This project is designed as a local client-side helper. It only performs ordinary portal status checks and credential-based sign-in requests that a user could initiate through the portal. It contains no destructive operation, scanning, exploitation, privilege escalation, data modification, or server-side control logic, and it is not capable of damaging, destroying, or impairing institutional network facilities, business systems, or third-party systems.

Portal behavior may change at any time. No warranty is provided.

## Security

The current app reads credentials from the local settings file. Protect that file and avoid sharing logs or screenshots that may reveal local environment details. A future version should migrate credentials to Windows Credential Manager or DPAPI-protected storage.

## License

MIT
