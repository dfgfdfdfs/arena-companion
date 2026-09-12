$ErrorActionPreference='Stop'
$taskOutput=Join-Path $PSScriptRoot '成品'
$taskReferences=@('System.dll','System.Core.dll','System.Security.dll','System.Windows.Forms.dll','System.Drawing.dll','System.Web.Extensions.dll',(Join-Path $taskOutput 'Microsoft.Web.WebView2.Core.dll'),(Join-Path $taskOutput 'Microsoft.Web.WebView2.WinForms.dll'))
$taskArgs=@('/nologo','/target:exe','/platform:x64','/langversion:5',('/out:'+(Join-Path $taskOutput 'GalleryTests.exe')))
foreach($taskReference in $taskReferences){$taskArgs+='/reference:'+$taskReference}
$taskArgs+=@('GalleryTests.cs','GalleryWindow.cs','CandidateStore.cs','BrowserScript.cs','CandidatePage.cs','CandidateCollector.cs','HtmlImageRenderer.cs','SavedConversationStore.cs','AccountStore.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' @taskArgs
if($LASTEXITCODE -ne 0){throw 'Gallery tests failed to compile'}
& (Join-Path $taskOutput 'GalleryTests.exe')
if($LASTEXITCODE -ne 0){throw 'Gallery tests failed'}
