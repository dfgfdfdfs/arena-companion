# v2rayN 7.12.7 Arena 本机控制桥

本目录包含一个新增类和对 `MainWindow.xaml.cs` 的最小补丁：

- `ArenaControlServer.cs`：同一 Windows 用户可访问的命名管道服务。
- `MainWindow.patch`：在 v2rayN 主窗口生命周期内创建并释放服务。

目标上游版本：`2dust/v2rayN` 标签 `7.12.7`，提交 `a46a4ad7c1219f5f5d558c7c1c738cd287a697ea`。

在仓库根目录运行：

```powershell
.\build-v2rayn-bridge.ps1
```

脚本在 `artifacts/upstream` 中建立独立源码检出，应用补丁并构建，不会停止、覆盖或修改用户正在运行的代理目录。

该修改及上游项目适用 GPL-3.0。发布构建时，`package-distribution.ps1` 会把完整的对应源码一并放入分发目录。
