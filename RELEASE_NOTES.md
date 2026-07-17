# Unreleased

## CquVpnCore C0/C1 experimental boundary

- VPN tray actions now start the bundled `CquVpnCore.exe`, not an official VPN process, service, Agent, or Tray.
- The core opens the browser authentication portal and accepts an explicit user confirmation through local IPC.
- The core deliberately does not establish a tunnel, read browser authentication data, change network configuration, or support uninstalling the official client.
- This is not a VPN-capable release and must not be described as connected, compatible with generic VPN clients, or ready to replace the official client.

# CquAutoLogin v0.1.0

Initial public release.

Highlights:

- Windows tray background monitor.
- Campus portal online-state detection.
- Current CQU portal compatibility using `http://login.cqu.edu.cn:801`.
- Empty credential defaults for public distribution.

Disclaimer:

This is an unofficial utility and is not affiliated with CQU or any network service provider. Use only with accounts and networks you are authorized to access.

This project is designed as a local client-side helper. It only performs ordinary portal status checks and credential-based sign-in requests that a user could initiate through the portal. It contains no destructive operation, scanning, exploitation, privilege escalation, data modification, or server-side control logic, and it is not capable of damaging, destroying, or impairing institutional network facilities, business systems, or third-party systems.
