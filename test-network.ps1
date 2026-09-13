$ErrorActionPreference='Stop'
$taskCompiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskIpOutput=Join-Path $PSScriptRoot 'IpCycleTests.exe'
& $taskCompiler /nologo /langversion:5 /reference:System.Core.dll /reference:System.Web.Extensions.dll ('/out:'+$taskIpOutput) (Join-Path $PSScriptRoot 'V2rayNControl.cs') (Join-Path $PSScriptRoot 'IpCycle.cs') (Join-Path $PSScriptRoot 'IpCycleTests.cs')
if($LASTEXITCODE-ne0){throw 'IP cycle tests failed to compile'}
& $taskIpOutput
if($LASTEXITCODE-ne0){throw 'IP cycle tests failed'}
$taskStatusOutput=Join-Path $PSScriptRoot 'NetworkStatusFormatterTests.exe'
& $taskCompiler /nologo /langversion:5 ('/out:'+$taskStatusOutput) (Join-Path $PSScriptRoot 'V2rayNControl.cs') (Join-Path $PSScriptRoot 'NetworkStatusFormatter.cs') (Join-Path $PSScriptRoot 'NetworkStatusFormatterTests.cs')
if($LASTEXITCODE-ne0){throw 'Network status tests failed to compile'}
& $taskStatusOutput
if($LASTEXITCODE-ne0){throw 'Network status tests failed'}
