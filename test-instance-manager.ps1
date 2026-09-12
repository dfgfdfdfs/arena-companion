$ErrorActionPreference='Stop'
$taskBase=$PSScriptRoot
$taskOutput=Join-Path $taskBase 'InstanceManagerTests.exe'
$taskCompiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $taskCompiler /nologo /langversion:5 /reference:System.dll /reference:System.Core.dll /reference:System.Security.dll /reference:System.Web.Extensions.dll /reference:Microsoft.VisualBasic.dll ('/out:'+$taskOutput) (Join-Path $taskBase 'InstanceContext.cs') (Join-Path $taskBase 'InstanceManager.cs') (Join-Path $taskBase 'InstanceManagerTests.cs')
if($LASTEXITCODE -ne 0){throw 'Instance manager tests failed to compile'}
& $taskOutput
if($LASTEXITCODE -ne 0){throw 'Instance manager tests failed'}
