param(
    [string]$OutputDirectory = (Join-Path $env:TEMP ("redbool-socket-check-" + [guid]::NewGuid().ToString('N'))),
    [string]$MonoPath = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$helper = Join-Path $projectRoot 'Assets\RedboolVision\RedboolSocketOwnership.cs'
$fixture = Join-Path $PSScriptRoot 'SocketOwnershipCheck.cs'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Native Windows .NET Framework compiler not found.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$executable = Join-Path $OutputDirectory 'SocketOwnershipCheck.exe'
$helperHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $helper).Hash.ToLowerInvariant()
& $compiler /nologo /target:exe /platform:x64 /optimize+ /reference:System.Runtime.Serialization.dll "/out:$executable" $helper $fixture
if ($LASTEXITCODE -ne 0) { throw "C# compile failed: $LASTEXITCODE" }
& $executable --output (Join-Path $OutputDirectory 'framework.json') --helper-sha256 $helperHash
if ($LASTEXITCODE -ne 0) { throw "Native Framework socket check failed: $LASTEXITCODE" }

# A locally installed standalone or Unity Mono can run the same compiled helper.
# The standard hosted runner has no required Mono install; absence is explicit.
if (-not $MonoPath) {
    $monoCommand = Get-Command mono.exe -ErrorAction SilentlyContinue
    if ($monoCommand) { $MonoPath = $monoCommand.Source }
}
if ($MonoPath) {
    if (-not (Test-Path -LiteralPath $MonoPath)) { throw 'Requested Mono executable does not exist.' }
    & $MonoPath $executable --output (Join-Path $OutputDirectory 'mono.json') --helper-sha256 $helperHash
    if ($LASTEXITCODE -ne 0) { throw "Native Mono socket check failed: $LASTEXITCODE" }
} else {
    @{ status='SKIPPED'; reason='No standalone mono.exe available; Framework success is not Unity Mono scene evidence.' } |
        ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $OutputDirectory 'mono-unavailable.json')
}
Write-Output "Socket evidence: $OutputDirectory"
