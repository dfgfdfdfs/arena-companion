$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'test.ps1')
& (Join-Path $PSScriptRoot 'test-gallery.ps1')
$taskCompiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
foreach($taskTest in @('InstanceTests','AccountReplacementTests','AccountDefaultsTests')) {
    $taskSources=@('InstanceContext.cs',($taskTest+'.cs'))
    if($taskTest -eq 'AccountReplacementTests'){$taskSources+=@('AccountReplacement.cs','AccountStore.cs','TaskSettings.cs')}
    if($taskTest -eq 'AccountDefaultsTests'){$taskSources+=@('AccountStore.cs','PasswordPolicy.cs')}
    $taskArgs=@('/nologo','/langversion:5','/reference:System.Security.dll','/reference:System.Web.Extensions.dll',('/out:'+(Join-Path $PSScriptRoot ($taskTest+'.exe'))))
    $taskArgs+=$taskSources | ForEach-Object {Join-Path $PSScriptRoot $_}
    & $taskCompiler @taskArgs
    if($LASTEXITCODE -ne 0){throw 'Multi-instance test build failed'}
    & (Join-Path $PSScriptRoot ($taskTest+'.exe'))
    if($LASTEXITCODE -ne 0){throw 'Multi-instance tests failed'}
}
