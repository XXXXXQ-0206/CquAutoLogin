# 浏览器认证自动检测

当前版本通过一个可选的 Chrome/Edge 扩展桥接浏览器认证状态。扩展只检查认证页当前可见的 URL、页面可见性和固定界面文字，并只向 CquAutoLogin 发送三态结果：

- `unknown`
- `auth-required`
- `authenticated`

扩展不会读取 Cookie、Local Storage、密码、Token、Authorization header 或网络响应体。应用注册 Native Messaging 主机后，扩展通过当前用户本地状态文件把结果交给 `CquVpnCore`；没有“我已完成浏览器认证”按钮，也不需要在每次登录后手工确认。状态文件只接受短时有效的记录，且仅含三态和报告时间。

## 一次性安装扩展

1. 启动 CquAutoLogin，使它把 `CquVpnCore.exe` 注册为当前用户的 Native Messaging 主机。
2. 在 Chrome 或 Edge 打开扩展管理页并启用开发者模式。
3. 选择“加载已解压的扩展”，目录选择安装目录下的 `Assets\BrowserBridge`。
4. 保持扩展启用。以后打开认证页或认证状态发生变化时，状态会自动上报。

这一桥接只表示浏览器认证状态，不表示 VPN 隧道已经建立；当前 `CquVpnCore` 仍处于 C0/C1 阶段。
