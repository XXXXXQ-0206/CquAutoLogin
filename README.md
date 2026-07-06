# CquAutoLogin

## 中文

CquAutoLogin 是一个 Windows 托盘工具，用于监控并自动处理重庆大学校园网 Portal 登录状态。

它可以：

- 监听以太网和 `CQU_Wifi` 网络变化；
- 检测当前设备是否已经在校园网 Portal 在线；
- 在没有有效会话时提交 Portal 登录请求；
- 随 Windows 开机静默启动；
- 通过托盘菜单提供快速操作。

### 当前 Portal 说明

当前 Portal 适配目标：

- Portal 主机：`login.cqu.edu.cn`
- 默认 Portal 基础地址：`http://login.cqu.edu.cn:801`
- 会自动迁移旧配置中的 `https://login.cqu.edu.cn:802`

校园网 Portal 是运行中的业务系统，接口和参数可能随时变化。如果 Portal 端点或参数发生变化，本项目也需要相应更新。

### 环境要求

- Windows 10/11
- 从源码构建需要 .NET 9 SDK
- 可访问对应校园网环境

### 构建

```powershell
dotnet build .\CquAutoLogin.csproj
```

### 发布

```powershell
dotnet publish .\CquAutoLogin.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
```

### 配置

首次启动后，应用会在当前 Windows 用户的本地应用数据目录下创建配置文件：

```text
%LocalAppData%\CquAutoLogin\settings.json
```

你需要在其中填写自己的校园网账号和密码：

```json
{
  "Username": "your-account",
  "Password": "your-password"
}
```

请不要提交或公开你的个人 `settings.json`。

### 日志

运行日志位于：

```text
%LocalAppData%\CquAutoLogin\Logs
```

### 免责声明

本项目是非官方个人工具。它与重庆大学或任何网络服务提供方均无隶属、认可、赞助或维护关系。

请仅在你有权访问的网络和账号上使用本工具。你需要自行遵守所在机构的网络管理规定、可接受使用政策和适用法律。作者和贡献者不对账号问题、网络访问中断、政策违规、数据丢失或因使用本软件造成的任何其他损失负责。

校园网 Portal 行为可能随时变化。本软件不提供任何形式的担保。

### 安全说明

当前应用从本地配置文件读取凭据。请妥善保护该文件，避免分享可能暴露本地环境信息的日志或截图。后续版本宜迁移到 Windows Credential Manager 或 DPAPI 保护的凭据存储。

### 许可证

MIT

---

## English

CquAutoLogin is a small Windows tray utility for monitoring and reconnecting to the Chongqing University campus network portal.

It can:

- watch Ethernet and `CQU_Wifi` network changes;
- detect whether the current device is already online in the campus portal;
- submit a portal login request when no valid session is found;
- run silently from Windows startup;
- expose quick actions from a tray menu.

### Current Portal Notes

The portal integration currently targets:

- portal host: `login.cqu.edu.cn`
- default portal base URL: `http://login.cqu.edu.cn:801`
- fallback migration from the older `https://login.cqu.edu.cn:802` setting

Campus portals are operational systems and may change without notice. If the portal endpoints or parameters change, this project may need to be updated.

### Requirements

- Windows 10/11
- .NET 9 SDK for building from source
- Access to the relevant campus network

### Build

```powershell
dotnet build .\CquAutoLogin.csproj
```

### Publish

```powershell
dotnet publish .\CquAutoLogin.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
```

### Configuration

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

### Logs

Runtime logs are written to:

```text
%LocalAppData%\CquAutoLogin\Logs
```

### Disclaimer

This project is an unofficial personal utility. It is not affiliated with, endorsed by, sponsored by, or maintained by Chongqing University or any network service provider.

Use it only on networks and accounts that you are authorized to access. You are responsible for complying with your institution's network policies, acceptable-use rules, and applicable laws. The authors and contributors are not responsible for account issues, network access interruptions, policy violations, data loss, or any other damages arising from the use of this software.

The campus portal behavior may change at any time. No warranty is provided.

### Security

The current app reads credentials from the local settings file. Protect that file and avoid sharing logs or screenshots that may reveal local environment details. A future version should migrate credentials to Windows Credential Manager or DPAPI-protected storage.

### License

MIT
