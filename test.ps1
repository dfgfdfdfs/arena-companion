$ErrorActionPreference='Stop'
$taskCompiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskAuthOutput=Join-Path $PSScriptRoot 'AuthFlowTests.exe'
& $taskCompiler /nologo /langversion:5 /reference:System.Security.dll /reference:System.Web.Extensions.dll ("/out:"+$taskAuthOutput) (Join-Path $PSScriptRoot 'AuthFlow.cs') (Join-Path $PSScriptRoot 'AccountStore.cs') (Join-Path $PSScriptRoot 'AuthFlowTests.cs')
if($LASTEXITCODE -ne 0){throw 'Auth tests failed to compile'}
& $taskAuthOutput
if($LASTEXITCODE -ne 0){throw 'Auth tests failed'}
$taskControllerOutput=Join-Path $PSScriptRoot 'ControllerTests.exe'
& $taskCompiler /nologo /langversion:5 ("/out:"+$taskControllerOutput) (Join-Path $PSScriptRoot 'RetryController.cs') (Join-Path $PSScriptRoot 'RetryController.Cooldown.cs') (Join-Path $PSScriptRoot 'ControllerTests.cs')
if($LASTEXITCODE -ne 0){throw 'Controller tests failed to compile'}
& $taskControllerOutput
if($LASTEXITCODE -ne 0){throw 'Controller tests failed'}
node --check (Join-Path $PSScriptRoot 'AuthBridge.js')
if($LASTEXITCODE -ne 0){throw 'Auth bridge syntax failed'}
node --test (Join-Path $PSScriptRoot 'AuthBridge.test.cjs')
if($LASTEXITCODE -ne 0){throw 'Auth bridge tests failed'}
node --test (Join-Path $PSScriptRoot 'PageBridge.test.cjs')
if($LASTEXITCODE -ne 0){throw 'Page bridge tests failed'}
$taskSettingsOutput=Join-Path $PSScriptRoot 'TaskSettingsTests.exe'
& $taskCompiler /nologo /langversion:5 /reference:System.Web.Extensions.dll ("/out:"+$taskSettingsOutput) (Join-Path $PSScriptRoot 'TaskSettings.cs') (Join-Path $PSScriptRoot 'TaskSettingsTests.cs')
if($LASTEXITCODE -ne 0){throw 'Settings tests failed to compile'}
& $taskSettingsOutput
if($LASTEXITCODE -ne 0){throw 'Settings tests failed'}
