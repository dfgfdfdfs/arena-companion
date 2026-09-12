param(
    [string]$Version='v2026.09.12.2',
    [string]$OutputDirectory,
    [switch]$SourceOnly
)
$ErrorActionPreference='Stop'
if($Version-notmatch '^v?\d{4}\.\d{2}\.\d{2}(\.\d+)?$'){throw '版本格式应为 vYYYY.MM.DD 或 vYYYY.MM.DD.N'}
$taskRoot=[IO.Path]::GetFullPath($PSScriptRoot)
$taskVersion=$Version.TrimStart('v')
$taskOutput=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $taskRoot 'artifacts\releases'}
New-Item -ItemType Directory -Path $taskOutput -Force|Out-Null
if((git -C $taskRoot status --porcelain --untracked-files=no)){throw '有尚未提交的源码改动，请先提交再发布'}
$taskSourceZip=Join-Path $taskOutput ('Arena-companion-v'+$taskVersion+'-source.zip')
$taskBinaryZip=Join-Path $taskOutput ('Arena-companion-v'+$taskVersion+'-win-x64.zip')
$taskChecksums=Join-Path $taskOutput 'SHA256SUMS.txt'
foreach($taskFile in @($taskSourceZip,$taskBinaryZip,$taskChecksums)){if(Test-Path -LiteralPath $taskFile){throw ('输出文件已存在：'+$taskFile)}}
$taskArchiveArgs=@('archive','--format=zip',('--prefix=arena-companion-v'+$taskVersion+'-source/'),('--output='+$taskSourceZip),'HEAD')
& git -C $taskRoot @taskArchiveArgs
if($LASTEXITCODE-ne0){throw '源码归档失败'}
$taskFiles=@($taskSourceZip)
if(-not $SourceOnly){
    & (Join-Path $taskRoot 'package-distribution.ps1') -OutputZip $taskBinaryZip
    if($LASTEXITCODE-ne0){throw '独立分发版打包失败'}
    $taskFiles+=$taskBinaryZip
}
$taskFiles|ForEach-Object {(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLower()+'  '+[IO.Path]::GetFileName($_)}|Set-Content -LiteralPath $taskChecksums -Encoding utf8
[pscustomobject]@{version='v'+$taskVersion;source=$taskSourceZip;binary=if($SourceOnly){$null}else{$taskBinaryZip};checksums=$taskChecksums}
