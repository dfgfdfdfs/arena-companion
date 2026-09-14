# Arena 筛选助手

这是 Windows 桌面版源码。它为每个实例保存独立的 Arena 浏览器资料与账号状态，可自动提交任务、识别回答状态、收集回答中的 HTML 并渲染为候选图集。

## 当前功能

- 多实例：创建、重命名、删除实例；各实例使用独立浏览器资料和账号。
- 首次使用：独立分发版不带默认密码。新实例要求输入两次密码，密码至少 8 位，并包含一个大写字母和一个符号。
- 任务设置：保存提示词和附件副本，切换账号后继续沿用。
- 候选图集：自动收集完成回答里的 HTML，渲染完整 PNG；可选择、丢弃、保存账号与对话，并从保存入口返回原对话。保存结果统一进入用户所选位置下的“账号”子文件夹；单个对话文件夹重命名后，入口仍按稳定对话 ID 找回记录。
- 会话恢复：识别 `Assistant node ... not found in session ...`，核对状态并备份后恢复可输入会话。
- 限流处理：识别 429 后切换到账号页并触发无副作用的“测试”按钮；用户点击“切换到下一个不同 IP”且切换确认成功后，软件返回 Arena 并自动重试刚才失败的任务。
- 网络信息：账号页每 5 秒刷新当前节点和公网出口；目标不会提前固定。“切换到下一个不同 IP”在点击时实时读取全部 v2rayN 节点和延迟，解析并按服务器 IP 去重，同一 IP 取最低延迟配置，排除中国大陆、香港、台湾、美国及地区未知节点。其余候选优先日本，日本节点之间按最低延迟选择；最近 4 次成功 IP 不重复，第 5 次起较早 IP 可再次使用。
- 本机接口：使用仅当前 Windows 用户可访问的命名管道，不开放 HTTP 端口。

## 构建

系统要求：Windows x64、.NET Framework 4.6.2 或更高兼容版本，以及 Microsoft Edge WebView2 Runtime。

```powershell
.\build.ps1 -NoDefaultPassword
```

输出位于 `成品\Arena筛选助手.exe`。仓库包含编译所需的 WebView2 SDK 文件，因此主程序构建不需要联网恢复 NuGet 包。也可以在 Visual Studio 中打开 `ArenaCompanion.sln`，使用 `Release | x64` 构建。

## 测试

DOM 测试需要 Node.js 18 或更高版本：

```powershell
npm ci
.\test.ps1
.\test-gallery.ps1
.\test-recovery.ps1
.\test-cooldown.ps1
.\test-instance-manager.ps1
.\test-multi.ps1
.\test-network.ps1
```

这些测试使用本地夹具，不需要真实 Arena 账号。网站界面变化仍可能需要更新选择器。

429 不会按倒计时自行重试。IP 切换失败时任务保持停止；平时在空闲或手动暂停状态切换 IP，也不会意外启动任务。

IP 国家码通过 `https://api.country.is/` 批量读取，每批最多 100 个地址，因此节点超过 100 个时仍会全部检查。地区服务不可用或某个地址无法确认时，该地址不会被当作允许地区节点。

## 打包独立分发版

```powershell
.\package-distribution.ps1
```

脚本始终使用 `-NoDefaultPassword` 构建，并检查分发目录中不存在 `account.dpapi`。可选的 v2rayN 控制组件会从官方 `2dust/v2rayN` 的 `7.12.7` 标签构建，不覆盖正在运行的 v2rayN。

发布源码包和独立版压缩包：

```powershell
.\package-release.ps1 -Version v2026.09.12.2
```

## v2rayN 控制桥

`v2rayn-bridge` 保存针对 v2rayN 7.12.7 的新增源码与最小补丁。桥只暴露同一 Windows 用户下的命名管道，返回节点显示信息并调用 v2rayN 自身的活动配置切换及核心重载逻辑，不返回订阅地址、UUID 或密码。

该组件基于 GPL-3.0 的 v2rayN。构建和分发时会包含对应的完整修改源码。详见 `v2rayn-bridge/README.md`。

## 数据位置

运行数据默认位于 `%LOCALAPPDATA%\Arena筛选助手`，包括实例资料、任务设置和候选图集。账号密码使用当前 Windows 用户的 DPAPI 加密。仓库和源码压缩包不包含账号、密码、浏览器资料、候选图、测试日志或 EXE。

详细依赖见 `DEPENDENCIES.md`，验证范围见 `VALIDATION.md`。
