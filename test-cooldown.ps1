$ErrorActionPreference='Stop'
$taskOutput=Join-Path $PSScriptRoot '成品'
$taskArgs=@('/nologo','/target:exe','/platform:x64','/langversion:5',('/out:'+(Join-Path $taskOutput 'RateLimitTests.exe')))
foreach($taskReference in @('System.dll','System.Core.dll','System.Windows.Forms.dll','System.Drawing.dll','System.Web.Extensions.dll',(Join-Path $taskOutput 'Microsoft.Web.WebView2.Core.dll'),(Join-Path $taskOutput 'Microsoft.Web.WebView2.WinForms.dll'))){$taskArgs+='/reference:'+$taskReference}
$taskArgs+=@('RateLimitTests.cs','RateLimitTracker.cs','BrowserScript.cs','WebPage.cs','RetryController.cs','RetryController.Cooldown.cs','AttachmentUpload.cs','TaskSettings.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' @taskArgs
if($LASTEXITCODE -ne 0){throw 'Cooldown tests failed to compile'}
& (Join-Path $taskOutput 'RateLimitTests.exe')
if($LASTEXITCODE -ne 0){throw 'Cooldown tests failed'}
