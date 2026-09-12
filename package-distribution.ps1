param(
    [Parameter(Mandatory=$true)][string]$PersonalDirectory,
    [Parameter(Mandatory=$true)][string]$DistributionDirectory,
    [Parameter(Mandatory=$true)][string]$ZipPath
)
$ErrorActionPreference='Stop'
$taskPersonal=[IO.Path]::GetFullPath($PersonalDirectory)
$taskDistribution=[IO.Path]::GetFullPath($DistributionDirectory)
if(-not (Test-Path -LiteralPath (Join-Path $taskPersonal 'Arena筛选助手.exe'))){throw '个人版目录无效'}
if(Test-Path -LiteralPath $taskDistribution){throw '独立分发目录必须是全新目录，不能覆盖既有版本'}
if(Test-Path -LiteralPath $ZipPath){throw '压缩包路径已经存在，请使用新文件名'}
New-Item -ItemType Directory -Path $taskDistribution | Out-Null
Copy-Item -Path (Join-Path $taskPersonal '*') -Destination $taskDistribution -Recurse -Force
$taskDefault=Join-Path $taskDistribution 'assets\account-defaults\account.dpapi'
if(Test-Path -LiteralPath $taskDefault){Remove-Item -LiteralPath $taskDefault -Force}
$taskPersonalFiles=Get-ChildItem -LiteralPath $taskPersonal -File -Recurse
foreach($taskFile in $taskPersonalFiles) {
    $taskRelative=$taskFile.FullName.Substring($taskPersonal.Length).TrimStart('\')
    if($taskRelative -eq 'assets\account-defaults\account.dpapi'){continue}
    $taskCopy=Join-Path $taskDistribution $taskRelative
    if(-not (Test-Path -LiteralPath $taskCopy)){throw '独立分发版缺少文件：'+$taskRelative}
    if((Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $taskCopy -Algorithm SHA256).Hash){throw '独立分发版文件与个人版不一致：'+$taskRelative}
}
Compress-Archive -Path (Join-Path $taskDistribution '*') -DestinationPath $ZipPath -CompressionLevel Optimal
[pscustomobject]@{
    PersonalExeSha256=(Get-FileHash -LiteralPath (Join-Path $taskPersonal 'Arena筛选助手.exe') -Algorithm SHA256).Hash
    DistributionExeSha256=(Get-FileHash -LiteralPath (Join-Path $taskDistribution 'Arena筛选助手.exe') -Algorithm SHA256).Hash
    DefaultPasswordIncluded=(Test-Path -LiteralPath $taskDefault)
    DistributionDirectory=$taskDistribution
    ZipPath=[IO.Path]::GetFullPath($ZipPath)
}
