# Unreleased

## CquVpnCore C0/C1 experimental boundary

- VPN tray actions now start the bundled `CquVpnCore.exe`, not an official VPN process, service, Agent, or Tray.
- The core opens the browser authentication portal and can receive an automatic visible-state report through the optional browser bridge.
- The bridge now emits a page-independent readiness acknowledgement so the tray can distinguish a working bridge from an absent report without treating either as VPN connectivity.
- For a visible portal page, the browser bridge reports only `unknown`, `auth-required`, or `authenticated`; it does not read browser credentials or session storage.
- The core deliberately does not establish a tunnel, read browser authentication data, change network configuration, or support uninstalling the official client.
- Release builds omit PDB debug symbols so published executables do not expose local source paths.
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
