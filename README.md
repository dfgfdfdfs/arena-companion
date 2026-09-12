# Arena 筛选助手

适用于 Windows 的 Arena 桌面辅助工具，使用 C#、WinForms 和 WebView2。

## 下载与使用

在本仓库右侧 **Releases** 中下载最新的 Windows x64 压缩包，完整解压后双击 `Arena筛选助手.exe`。不要只复制 exe，旁边的 DLL 和 assets 文件夹也需要保留。

首次运行填写账号密码，密码至少 8 位，并包含一个大写字母和一个符号。分发版不附带默认密码或开发者账号。需要 Windows x64、.NET Framework 4.6.2 或以上，以及 Microsoft Edge WebView2 Runtime。

觉得有用可以点击页面右上角 **Star**；问题和建议可提交到 **Issues**。

## 功能

- 多实例保存独立账号、浏览器资料和任务设置。
- 自定义提示词，绑定附件后自动用于后续任务；默认无需附件。
- 根据页面实际显示的 Thinking / Thought 状态筛选回答。未观察到 Thinking 不代表确定使用了某个模型。
- 回答完成后读取 HTML，渲染成候选图片，并与原账号、原对话关联。
- 图集支持比较图片、右键打开原对话、丢弃图片和保存会话入口。
- 遇到已确认的 HTTP 429 限流后，等待约 5 秒重试原问题。新限流回复更新网站提示的倒计时，提交成功后继续任务。页面和附件检查可能使间隔略长。
- 对符合特定状态的 `Assistant node ... not found in session ...` 错误，先保留恢复备份，再修复本地会话状态。

手动暂停会停止自动重试。遇到人机验证、登录失效、用户修改草稿或提交结果不明时保留现场，避免重复提交。网站界面或接口变化可能需要适配。

## 本地数据

独立分发版默认将数据保存在 `%LOCALAPPDATA%\Arena筛选助手独立版`。密码使用 Windows 当前用户的 DPAPI 加密；账号、浏览器资料、附件和候选图片均保存在本机，不包含在此仓库和分发包中。

## 从源码构建

在 Windows PowerShell 中运行：

```powershell
./build.ps1
```

输出位于 `成品` 文件夹。构建脚本使用 Windows 自带的 .NET Framework C# 编译器；所需 WebView2 SDK 文件已放在 `vendor/webview2`，版本为 `1.0.4191.47`。第三方许可和声明见该目录的 `LICENSE.txt` 与 `NOTICE.txt`。

## 测试

JavaScript 测试需要 Node.js：

```powershell
npm ci
npm test
./test.ps1
./test-cooldown.ps1
./test-gallery.ps1
./test-recovery.ps1
```

先执行 `build.ps1`，再运行 WebView 测试。这些测试使用本地测试页面，不会注册 Arena 账号或向 Arena 发送生成请求。测试输出放在 `qa`，不会提交到仓库。

本次版本包含读取通道并发修复、HTML 双入口识别修复，以及五秒限流重试。验证范围见 [VALIDATION.md](VALIDATION.md)。
