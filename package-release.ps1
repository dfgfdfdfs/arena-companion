param(
    [string]$Version='v2026.09.12.1',
    [string]$OutputDirectory
)
$ErrorActionPreference='Stop'
if($Version -notmatch '^v?\d{4}\.\d{2}\.\d{2}(\.\d+)?$'){throw '版本格式应为 vYYYY.MM.DD 或 vYYYY.MM.DD.N'}
$taskRoot=$PSScriptRoot
$taskVersion=$Version.TrimStart('v')
$taskOutput=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $taskRoot 'artifacts\releases'}
$taskStage=Join-Path $taskOutput ('Arena-companion-v'+$taskVersion+'-win-x64')
$taskBinaryZip=$taskStage+'.zip'
$taskSourceZip=Join-Path $taskOutput ('Arena-companion-v'+$taskVersion+'-source.zip')
$taskChecksums=Join-Path $taskOutput 'SHA256SUMS.txt'

if((git -C $taskRoot status --porcelain --untracked-files=no)){throw '有尚未提交的源码改动；请先提交再打包，以保证源码包可复现'}
$taskResolvedRoot=[IO.Path]::GetFullPath($taskRoot).TrimEnd('\')
$taskResolvedStage=[IO.Path]::GetFullPath($taskStage)
if(-not $taskResolvedStage.StartsWith($taskResolvedRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw '打包暂存目录必须位于仓库内'}
if(Test-Path -LiteralPath $taskStage){throw ('暂存目录已存在，请先检查后自行移除：'+$taskStage)}
foreach($taskFile in @($taskBinaryZip,$taskSourceZip,$taskChecksums)){if(Test-Path -LiteralPath $taskFile){throw ('输出文件已存在：'+$taskFile)}}

& (Join-Path $taskRoot 'build.ps1')
if($LASTEXITCODE -ne 0){throw '程序构建失败'}
$taskBuild=Join-Path $taskRoot '成品'
New-Item -ItemType Directory -Path $taskStage,(Join-Path $taskStage 'assets'),(Join-Path $taskStage 'tools'),(Join-Path $taskStage '第三方许可') -Force|Out-Null
foreach($taskFile in @('Arena筛选助手.exe','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','README.md')){
    Copy-Item -LiteralPath (Join-Path $taskBuild $taskFile) -Destination (Join-Path $taskStage $taskFile)
}
foreach($taskFile in @('AuthBridge.js','CandidateBridge.js','ConversationRecovery.js','PageBridge.js','demo.html','gallery.html','welcome.html')){
    Copy-Item -LiteralPath (Join-Path $taskBuild ('assets\'+$taskFile)) -Destination (Join-Path $taskStage ('assets\'+$taskFile))
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'DEPENDENCIES.md') -Destination $taskStage
Copy-Item -LiteralPath (Join-Path $taskRoot 'Invoke-Arena.ps1') -Destination (Join-Path $taskStage 'tools\Invoke-Arena.ps1')
Copy-Item -LiteralPath (Join-Path $taskRoot 'vendor\webview2\LICENSE.txt') -Destination (Join-Path $taskStage '第三方许可\LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $taskRoot 'vendor\webview2\NOTICE.txt') -Destination (Join-Path $taskStage '第三方许可\NOTICE.txt')

Compress-Archive -LiteralPath $taskStage -DestinationPath $taskBinaryZip
$taskArchiveArgs=@('archive','--format=zip',('--prefix=arena-companion-v'+$taskVersion+'-source/'),('--output='+$taskSourceZip),'HEAD')
& git -C $taskRoot @taskArchiveArgs
if($LASTEXITCODE -ne 0){throw '源码归档失败'}
$taskLines=@($taskBinaryZip,$taskSourceZip)|ForEach-Object {(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLower()+'  '+[IO.Path]::GetFileName($_)}
$taskLines|Set-Content -LiteralPath $taskChecksums -Encoding utf8
[pscustomobject]@{binary=$taskBinaryZip;source=$taskSourceZip;checksums=$taskChecksums;version='v'+$taskVersion}
