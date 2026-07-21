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
