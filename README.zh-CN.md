# CquAutoLogin

<p align="right">
  <a href="./README.md"><img alt="English README" src="https://img.shields.io/badge/README-English-blue"></a>
</p>

CquAutoLogin 是一个 Windows 托盘工具，用于监控 CQU Portal 登录状态，并在当前设备没有有效 Portal 会话时辅助重新登录。

它可以：

- 监听以太网和 `CQU_Wifi` 网络变化；
- 检测当前设备是否已经在 Portal 在线；
- 在没有有效会话时提交 Portal 登录请求；
- 随 Windows 开机静默启动；
- 通过托盘菜单提供快速操作。

## 实验性 VPN 核心

随程序附带的 `CquVpnCore` 当前处于 C0/C1 阶段。它不会启动官方 VPN 服务、Agent 或 Tray；它只在系统浏览器打开认证门户，由用户自行完成单点登录或多因素认证，并可通过可选浏览器桥接自动检测保守的可见认证状态。

桥接启动时会先发送不含页面数据的 `bridge-ready` 回执；认证页只发送 `unknown`、`auth-required` 或 `authenticated` 三态结果。它不读取 Cookie、浏览器存储、密码、Token、Authorization header 或网络响应体。它**不会**建立 VPN 隧道、修改路由或 DNS，也不能替代官方客户端。托盘会明确显示连接能力仍在后续阶段。精确边界见 [浏览器认证桥接说明](./docs/BrowserAuthBridge.zh-CN.md) 和 [CquVpnCore 说明](./docs/CquVpnCore.zh-CN.md)。

## 当前 Portal 说明

当前 Portal 适配目标：

- Portal 主机：`login.cqu.edu.cn`
- 默认 Portal 基础地址：`http://login.cqu.edu.cn:801`
- 会自动迁移旧配置中的 `https://login.cqu.edu.cn:802`

Portal 是运行中的业务系统，接口和参数可能随时变化。如果端点或参数发生变化，本项目也需要相应更新。

## 环境要求

- Windows 10/11
- 从源码构建需要 .NET 9 SDK
- 可访问对应的 CQU Portal 环境

## 构建

```powershell
dotnet build .\CquAutoLogin.csproj
```

## 发布

```powershell
dotnet publish .\CquAutoLogin.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
```

自包含发布目录会同时包含 `CquAutoLogin.exe` 和 `CquVpnCore.exe`。

## 配置

首次启动后，应用会在当前 Windows 用户的本地应用数据目录下创建配置文件：

```text
%LocalAppData%\CquAutoLogin\settings.json
```

请填写你本人有权使用的 Portal 账号和密码：

```json
{
  "Username": "your-account",
  "Password": "your-password"
}
```

请不要提交或公开你的个人 `settings.json`。

## 日志

运行日志位于：

```text
%LocalAppData%\CquAutoLogin\Logs
```

## 免责声明

本项目是非官方个人工具。它与 CQU 或任何网络服务提供方均无隶属、认可、赞助或维护关系。

请仅在你有权访问的网络和账号上使用本工具。你需要自行遵守所在机构的网络管理规定、可接受使用政策和适用法律。作者和贡献者不对账号问题、网络访问中断、政策违规、数据丢失或因使用本软件造成的任何其他损失负责。

本项目被设计为本地客户端辅助工具，只执行普通的 Portal 状态查询和基于用户凭据的登录请求，也就是用户通常可以通过 Portal 页面自行发起的操作。它不包含破坏、扫描、攻击、提权、篡改数据或控制服务端系统的功能；从本项目的设计和实现范围看，它不具备对网络设施、业务系统或第三方系统造成破坏、损毁或损害的能力。

Portal 行为可能随时变化。本软件不提供任何形式的担保。

## 安全说明

当前应用从本地配置文件读取凭据。请妥善保护该文件，避免分享可能暴露本地环境信息的日志或截图。后续版本宜迁移到 Windows Credential Manager 或 DPAPI 保护的凭据存储。

## 许可证

MIT
