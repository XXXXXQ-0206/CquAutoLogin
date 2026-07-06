# CquAutoLogin

CquAutoLogin is a small Windows tray utility for monitoring and reconnecting to the Chongqing University campus network portal.

It can:

- watch Ethernet and `CQU_Wifi` network changes;
- detect whether the current device is already online in the campus portal;
- submit a portal login request when no valid session is found;
- run silently from Windows startup;
- expose quick actions from a tray menu.

## Current Portal Notes

The portal integration currently targets:

- portal host: `login.cqu.edu.cn`
- default portal base URL: `http://login.cqu.edu.cn:801`
- fallback migration from the older `https://login.cqu.edu.cn:802` setting

Campus portals are operational systems and may change without notice. If the portal endpoints or parameters change, this project may need to be updated.

## Requirements

- Windows 10/11
- .NET 9 SDK for building from source
- Access to the relevant campus network

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

You must fill in your own campus network account and password there:

```json
{
  "Username": "your-account",
  "Password": "your-password"
}
```

Do not commit your personal `settings.json`.

## Logs

Runtime logs are written to:

```text
%LocalAppData%\CquAutoLogin\Logs
```

## Disclaimer

This project is an unofficial personal utility. It is not affiliated with, endorsed by, sponsored by, or maintained by Chongqing University or any network service provider.

Use it only on networks and accounts that you are authorized to access. You are responsible for complying with your institution's network policies, acceptable-use rules, and applicable laws. The authors and contributors are not responsible for account issues, network access interruptions, policy violations, data loss, or any other damages arising from the use of this software.

The campus portal behavior may change at any time. No warranty is provided.

## Security

The current app reads credentials from the local settings file. Protect that file and avoid sharing logs or screenshots that may reveal local environment details. A future version should migrate credentials to Windows Credential Manager or DPAPI-protected storage.

## License

MIT
