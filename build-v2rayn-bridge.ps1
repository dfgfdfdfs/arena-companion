param(
    [string]$OutputDirectory=(Join-Path $PSScriptRoot 'v2rayN控制组件'),
    [string]$SourceDirectory
)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath($PSScriptRoot)
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
if(-not $taskOutput.StartsWith($taskRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw '输出目录必须位于仓库内'}
$taskCommit='a46a4ad7c1219f5f5d558c7c1c738cd287a697ea'
$taskSource=if($SourceDirectory){[IO.Path]::GetFullPath($SourceDirectory)}else{Join-Path $taskRoot 'artifacts\upstream\v2rayN-7.12.7'}
if(-not(Test-Path -LiteralPath (Join-Path $taskSource '.git'))){
    New-Item -ItemType Directory -Path (Split-Path -Parent $taskSource) -Force|Out-Null
    & git clone --filter=blob:none --no-checkout https://github.com/2dust/v2rayN.git $taskSource
    if($LASTEXITCODE-ne0){throw '无法获取 v2rayN 上游源码'}
}
if((git -C $taskSource status --porcelain)){throw 'v2rayN 构建检出含有未提交改动，请使用干净目录'}
& git -C $taskSource checkout --detach $taskCommit
if($LASTEXITCODE-ne0){throw '无法检出固定的 v2rayN 版本'}
Copy-Item -LiteralPath (Join-Path $taskRoot 'v2rayn-bridge\ArenaControlServer.cs') -Destination (Join-Path $taskSource 'v2rayN\v2rayN\ArenaControlServer.cs') -Force
& git -C $taskSource apply --check (Join-Path $taskRoot 'v2rayn-bridge\MainWindow.patch')
if($LASTEXITCODE-ne0){throw 'v2rayN 主窗口补丁与固定版本不匹配'}
& git -C $taskSource apply --whitespace=nowarn (Join-Path $taskRoot 'v2rayn-bridge\MainWindow.patch')
if($LASTEXITCODE-ne0){throw '应用 v2rayN 控制桥补丁失败'}
if(Test-Path -LiteralPath $taskOutput){Remove-Item -LiteralPath $taskOutput -Recurse -Force}
$taskRuntime=Join-Path $taskOutput 'v2rayN控制组件-runtime'
New-Item -ItemType Directory -Path $taskRuntime -Force|Out-Null
& dotnet publish (Join-Path $taskSource 'v2rayN\v2rayN\v2rayN.csproj') -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true -o $taskRuntime
if($LASTEXITCODE-ne0){throw 'v2rayN 控制组件构建失败'}
Copy-Item -LiteralPath (Join-Path $taskRoot 'Prepare-V2rayNControlCopy.ps1') -Destination $taskOutput
Copy-Item -LiteralPath (Join-Path $taskSource 'LICENSE') -Destination (Join-Path $taskOutput 'GPL-3.0.txt')
$taskCorresponding=Join-Path $taskOutput '对应源码-v2rayN-7.12.7-ArenaBridge'
New-Item -ItemType Directory -Path $taskCorresponding -Force|Out-Null
Get-ChildItem -LiteralPath $taskSource -Recurse -File|Where-Object {$_.FullName-notmatch '\\.git\\|\\bin\\|\\obj\\'}|ForEach-Object {
    $taskRelative=$_.FullName.Substring($taskSource.Length+1)
    $taskTarget=Join-Path $taskCorresponding $taskRelative
    New-Item -ItemType Directory -Path (Split-Path -Parent $taskTarget) -Force|Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $taskTarget -Force
}
@'
v2rayN Arena 本机控制组件（基于 v2rayN 7.12.7）

先正常退出准备复制的 v2rayN，再运行 Prepare-V2rayNControlCopy.ps1。
脚本会建立独立控制版，不覆盖原代理目录，也不会停止正在运行的代理。
本机接口仅允许当前 Windows 用户连接，不开放网络端口，不返回订阅地址或节点密钥。
'@|Set-Content -LiteralPath (Join-Path $taskOutput '说明.txt') -Encoding utf8
Get-Item -LiteralPath $taskOutput
