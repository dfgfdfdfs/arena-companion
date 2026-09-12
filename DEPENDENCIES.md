# 依赖与构建环境

## 运行环境

- Windows x64。
- .NET Framework 4.6.2 或以上兼容版本。
- Microsoft Edge WebView2 Runtime。Windows 11 通常已安装；缺失时从微软官方下载 Evergreen Runtime。

## 编译依赖

- Microsoft WebView2 SDK `1.0.4191.47`。
- 仓库内保留构建需要的三个 SDK 文件：
  - `vendor/webview2/lib/net462/Microsoft.Web.WebView2.Core.dll`
  - `vendor/webview2/lib/net462/Microsoft.Web.WebView2.WinForms.dll`
  - `vendor/webview2/runtimes/win-x64/native/WebView2Loader.dll`
- 相应第三方条款位于 `vendor/webview2/LICENSE.txt` 和 `vendor/webview2/NOTICE.txt`。

`build.ps1` 使用 Windows 自带的 .NET Framework 64 位 C# 编译器，按 C# 5 编译，不会联网恢复包。也可以在 Visual Studio 中打开 `ArenaCompanion.sln`，使用 `Release | x64` 构建。

## 测试依赖

- Node.js 18 或更高版本。
- `linkedom` `0.18.13`，只用于本地 DOM 单元测试，锁定在 `package-lock.json`。
- WebView2 流程测试仍需要 WebView2 Runtime。

运行 `npm ci` 后，可执行 README 中列出的测试脚本。测试页由本地拦截器响应，不使用真实 Arena 账号。

## 窗体与资源组织

本项目没有 Visual Studio Designer 生成的 `.Designer.cs` 或 `.resx`。所有 WinForms 窗体和控件均直接在下列 C# 文件中创建：

- 主窗口：`MainForm.cs` 及 `MainForm.*.cs`。
- 首次账号设置：`AccountSetupDialog.cs`。
- 实例选择：`InstancePicker.cs`。
- 附件设置：`AttachmentDialog.cs`。
- 候选图集：`GalleryWindow.cs`。
- 对话窗口：`ConversationWindow.cs`、`SavedConversationWindow.cs`。

网页资源是根目录中的 `*.js` 与 `*.html`，构建时复制到输出目录的 `assets` 文件夹。
