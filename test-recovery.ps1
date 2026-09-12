$ErrorActionPreference='Stop'
node --check (Join-Path $PSScriptRoot 'ConversationRecovery.js')
if($LASTEXITCODE -ne 0){throw 'Recovery syntax failed'}
node --test (Join-Path $PSScriptRoot 'ConversationRecovery.test.cjs')
if($LASTEXITCODE -ne 0){throw 'Recovery guards failed'}
$taskOutput=Join-Path $PSScriptRoot '成品'
$taskArgs=@('/nologo','/target:exe','/platform:x64','/langversion:5',('/out:'+(Join-Path $taskOutput 'RecoveryTests.exe')))
foreach($taskRef in @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll',(Join-Path $taskOutput 'Arena筛选助手.exe'),(Join-Path $taskOutput 'Microsoft.Web.WebView2.Core.dll'),(Join-Path $taskOutput 'Microsoft.Web.WebView2.WinForms.dll'))){$taskArgs+='/reference:'+$taskRef}
$taskArgs+=(Join-Path $PSScriptRoot 'RecoveryTests.cs')
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' @taskArgs
if($LASTEXITCODE -ne 0){throw 'Recovery tests build failed'}
& (Join-Path $taskOutput 'RecoveryTests.exe')
if($LASTEXITCODE -ne 0){throw 'Recovery WebView tests failed'}
