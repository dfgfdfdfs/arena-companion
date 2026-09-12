# 依赖与构建环境

## 主程序

- Windows x64。
- .NET Framework 4.6.2 或更高兼容版本。
- Microsoft Edge WebView2 Runtime。
- Microsoft WebView2 SDK `1.0.4191.47`。

仓库保留主程序编译需要的三个 SDK 二进制文件：

- `vendor/webview2/lib/net462/Microsoft.Web.WebView2.Core.dll`
- `vendor/webview2/lib/net462/Microsoft.Web.WebView2.WinForms.dll`
- `vendor/webview2/runtimes/win-x64/native/WebView2Loader.dll`

微软的条款与声明位于 `vendor/webview2/LICENSE.txt` 和 `vendor/webview2/NOTICE.txt`。

`build.ps1` 使用 Windows 自带的 .NET Framework 64 位 C# 编译器，按 C# 5 编译，不会联网恢复包。

## 测试

- Node.js 18 或更高版本。
- `linkedom` `0.18.13`，只用于本地 DOM 测试，版本锁定在 `package-lock.json`。

## 可选 v2rayN 控制组件

- Git。
- .NET 8 SDK。
- v2rayN `7.12.7`，固定提交 `a46a4ad7c1219f5f5d558c7c1c738cd287a697ea`。

`build-v2rayn-bridge.ps1` 从官方仓库获取固定版本、应用 `v2rayn-bridge` 中的补丁并构建独立控制版。这个过程不读取或修改用户正在运行的 v2rayN 目录。

## 窗体与资源

项目没有 Visual Studio Designer 生成的 `.Designer.cs` 或 `.resx`。所有 WinForms 窗体与控件直接在 C# 文件中创建：

- 主窗口：`MainForm.cs` 与 `MainForm.*.cs`
- 首次密码设置：`PasswordSetupDialog.cs`
- 实例选择：`InstancePicker.cs`
- 附件设置：`AttachmentDialog.cs`
- 候选图集：`GalleryWindow.cs`
- 对话窗口：`ConversationWindow.cs`、`SavedConversationWindow.cs`

网页资源是根目录中的 JavaScript 与 HTML 文件，构建时复制到输出目录的 `assets`。
