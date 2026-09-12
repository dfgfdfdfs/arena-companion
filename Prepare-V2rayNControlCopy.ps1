param(
    [string]$SourceDirectory,
    [string]$DestinationDirectory=(Join-Path ([Environment]::GetFolderPath('Desktop')) 'v2rayN-Arena控制版'),
    [string]$RuntimeDirectory=(Join-Path $PSScriptRoot 'v2rayN控制组件-runtime')
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
if(-not$SourceDirectory) {
    $taskDialog=[Windows.Forms.FolderBrowserDialog]::new()
    $taskDialog.Description='请选择现有 v2rayN 文件夹（里面应有 v2rayN.exe、bin、guiConfigs）'
    if($taskDialog.ShowDialog()-ne[Windows.Forms.DialogResult]::OK){return}
    $SourceDirectory=$taskDialog.SelectedPath
}
$taskSource=[IO.Path]::GetFullPath($SourceDirectory)
$taskDestination=[IO.Path]::GetFullPath($DestinationDirectory)
$taskRuntime=[IO.Path]::GetFullPath($RuntimeDirectory)
foreach($taskRequired in @('v2rayN.exe','bin','guiConfigs')) {
    if(-not(Test-Path -LiteralPath (Join-Path $taskSource $taskRequired))){throw '所选目录不是完整的 v2rayN：缺少 '+$taskRequired}
}
if(-not(Test-Path -LiteralPath (Join-Path $taskRuntime 'v2rayN.exe'))){throw '控制组件运行时不完整'}
if($taskDestination-eq$taskSource-or$taskDestination.StartsWith($taskSource+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw '控制版必须建立在另一个独立目录'}
if(Test-Path -LiteralPath $taskDestination){throw '目标目录已经存在；请改用一个新的空目录'}
$taskRunning=Get-CimInstance Win32_Process -Filter "Name='v2rayN.exe'"|Where-Object {$_.ExecutablePath-and[IO.Path]::GetFullPath($_.ExecutablePath).StartsWith($taskSource+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)}
if($taskRunning){throw '所选 v2rayN 正在运行。请先关闭它再复制；脚本不会停止或修改正在工作的代理'}
Copy-Item -LiteralPath $taskSource -Destination $taskDestination -Recurse
Get-ChildItem -LiteralPath $taskRuntime -File|Copy-Item -Destination $taskDestination -Force
$taskShortcut=Join-Path ([Environment]::GetFolderPath('Desktop')) 'v2rayN Arena控制版.lnk'
$taskShell=New-Object -ComObject WScript.Shell
$taskLink=$taskShell.CreateShortcut($taskShortcut)
$taskLink.TargetPath=Join-Path $taskDestination 'v2rayN.exe'
$taskLink.WorkingDirectory=$taskDestination
$taskLink.Description='带 Arena 本机切换接口的 v2rayN'
$taskLink.Save()
Write-Host '控制版已准备完成。原目录未修改：' -ForegroundColor Green
Write-Host $taskDestination
Write-Host '关闭其他 v2rayN 后，通过桌面“v2rayN Arena控制版”启动。'
