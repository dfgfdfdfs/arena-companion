param(
    [Parameter(Mandatory=$true)][string]$Command,
    [hashtable]$Data=@{},
    [string]$DataDirectory=(Join-Path $env:LOCALAPPDATA 'Arena筛选助手')
)
$ErrorActionPreference='Stop'
$taskEndpoint=Get-Content -Raw -LiteralPath (Join-Path $DataDirectory 'control-endpoint.json') | ConvertFrom-Json
$taskClient=[System.IO.Pipes.NamedPipeClientStream]::new('.', $taskEndpoint.pipe, [System.IO.Pipes.PipeDirection]::InOut)
try {
    $taskClient.Connect(5000)
    $taskUtf8=[System.Text.UTF8Encoding]::new($false)
    $taskWriter=[System.IO.StreamWriter]::new($taskClient,$taskUtf8,1024,$true)
    $taskWriter.AutoFlush=$true
    $Data.command=$Command
    $taskWriter.WriteLine(($Data | ConvertTo-Json -Compress -Depth 8))
    $taskReader=[System.IO.StreamReader]::new($taskClient,$taskUtf8,$false,1024,$true)
    $taskRead=$taskReader.ReadLineAsync()
    $taskWait=if($Command -eq 'account.replace'){60000}else{25000}
    if(-not $taskRead.Wait($taskWait)){throw '软件接口响应超时；请先读状态，不要重复提交'}
    $taskRead.Result | ConvertFrom-Json
} finally {$taskClient.Dispose()}
