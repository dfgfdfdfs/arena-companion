param([string]$OutputDirectory,[switch]$NoDefaultPassword)
$ErrorActionPreference = 'Stop'
$taskBase = $PSScriptRoot
$taskOutput = if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $taskBase '成品'}
$taskVendor = Join-Path $taskBase 'vendor\webview2'
New-Item -ItemType Directory -Path $taskOutput,(Join-Path $taskOutput 'assets') -Force | Out-Null
$taskDefaults=Join-Path $taskBase 'account-defaults\account.dpapi'
$taskOutputDefaults=Join-Path $taskOutput 'assets\account-defaults'
if($NoDefaultPassword) {
    if(Test-Path -LiteralPath $taskOutputDefaults){Remove-Item -LiteralPath $taskOutputDefaults -Recurse -Force}
} elseif(Test-Path -LiteralPath $taskDefaults) {
    New-Item -ItemType Directory -Path (Join-Path $taskOutput 'assets\account-defaults') -Force | Out-Null
    Copy-Item -LiteralPath $taskDefaults -Destination (Join-Path $taskOutput 'assets\account-defaults\account.dpapi') -Force
}
Copy-Item -LiteralPath (Join-Path $taskVendor 'lib\net462\Microsoft.Web.WebView2.Core.dll'),(Join-Path $taskVendor 'lib\net462\Microsoft.Web.WebView2.WinForms.dll'),(Join-Path $taskVendor 'runtimes\win-x64\native\WebView2Loader.dll') -Destination $taskOutput -Force
Copy-Item -LiteralPath (Join-Path $taskBase 'PageBridge.js'),(Join-Path $taskBase 'ConversationRecovery.js'),(Join-Path $taskBase 'AuthBridge.js'),(Join-Path $taskBase 'welcome.html'),(Join-Path $taskBase 'demo.html') -Destination (Join-Path $taskOutput 'assets') -Force
Copy-Item -LiteralPath (Join-Path $taskBase 'Invoke-Arena.ps1') -Destination $taskOutput -Force
Copy-Item -LiteralPath (Join-Path $taskBase 'README.md') -Destination $taskOutput -Force
Copy-Item -LiteralPath (Join-Path $taskBase 'CandidateBridge.js'),(Join-Path $taskBase 'gallery.html') -Destination (Join-Path $taskOutput 'assets') -Force
$taskReferences = @('System.dll','System.Core.dll','System.Security.dll','System.Windows.Forms.dll','System.Drawing.dll','System.Web.Extensions.dll','Microsoft.VisualBasic.dll',(Join-Path $taskOutput 'Microsoft.Web.WebView2.Core.dll'),(Join-Path $taskOutput 'Microsoft.Web.WebView2.WinForms.dll'))
$taskArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/langversion:5',('/win32manifest:' + (Join-Path $taskBase 'app.manifest')),('/out:' + (Join-Path $taskOutput 'Arena筛选助手.exe')))
foreach($taskReference in $taskReferences){$taskArgs += '/reference:' + $taskReference}
$taskArgs += @('RetryController.Cooldown.cs','RateLimitTracker.cs') | ForEach-Object {Join-Path $taskBase $_}
$taskArgs += @('ReplacementHandoff.cs') | ForEach-Object {Join-Path $taskBase $_}
$taskArgs += @('InstanceContext.cs','InstanceManager.cs','InstancePicker.cs','AccountReplacement.cs','MainForm.AccountReplacement.cs') | ForEach-Object {Join-Path $taskBase $_}
$taskArgs += @('V2rayNControl.cs','IpCycle.cs','NetworkStatusFormatter.cs','MainForm.NetworkSwitch.cs') | ForEach-Object {Join-Path $taskBase $_}
$taskArgs += @('Program.cs','MainForm.cs','MainForm.Control.cs','MainForm.TaskSettings.cs','RetryController.cs','BrowserScript.cs','ConversationRecovery.cs','WebPage.cs','AccountStore.cs','PasswordPolicy.cs','PasswordSetupDialog.cs','AuthPages.cs','AuthFlow.cs','ControlPipe.cs','TaskSettings.cs','AttachmentDialog.cs','AttachmentUpload.cs','CandidatePage.cs','CandidateStore.cs','CandidateCollector.cs','HtmlImageRenderer.cs','GalleryWindow.cs','ConversationWindow.cs','MainForm.Gallery.cs','SavedConversationStore.cs','SavedConversationWindow.cs') | ForEach-Object {Join-Path $taskBase $_}
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' @taskArgs
if($LASTEXITCODE -ne 0){throw 'Build failed'}
Get-Item -LiteralPath (Join-Path $taskOutput 'Arena筛选助手.exe') | Select-Object FullName,Length


