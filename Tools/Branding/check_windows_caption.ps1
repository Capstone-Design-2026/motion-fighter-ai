param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$out = (Resolve-Path $OutputDirectory).Path
$core = Join-Path $repo 'Assets/Branding/Runtime/WindowsCaption.cs'
$bootstrap = Join-Path $repo 'Assets/Branding/Runtime/DesktopCaptionBootstrap.cs'
$definition = Get-Content (Join-Path $repo 'Assets/Branding/Runtime/Redbool.DesktopPresentation.asmdef') -Raw | ConvertFrom-Json
if ($definition.autoReferenced -ne $false -or $definition.references.Count -ne 0 -or
    $definition.includePlatforms.Count -ne 1 -or $definition.includePlatforms[0] -ne 'WindowsStandalone64') {
    throw 'Presentation assembly isolation changed'
}
$wrapper = Get-Content $bootstrap -Raw
if ($wrapper -match '\b(Update|LateUpdate|FixedUpdate)\s*\(' -or $wrapper -match '\b(PlayerPrefs|persistentDataPath|RedboolDemoSettings)\b' -or
    $wrapper -notmatch 'attempt < 8' -or $wrapper -notmatch 'WaitForSecondsRealtime\(0\.25f\)' -or
    $wrapper -notmatch 'AlwaysLinkAssembly') { throw 'Caption wrapper boundary or retry budget changed' }
$csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exe = Join-Path $out 'WindowsCaptionNativeCheck.exe'
& $csc /nologo /target:exe /platform:x64 /optimize+ /utf8output /codepage:65001 "/out:$exe" $core (Join-Path $PSScriptRoot 'WindowsCaptionNativeCheck.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native caption fixture compilation failed' }
& $exe (Join-Path $out 'windows-caption-check.json')
$nativeExit = $LASTEXITCODE
@{
    bootstrap_boundaries_checked = $true
    compiler = $csc
    core_sha256 = (Get-FileHash $core -Algorithm SHA256).Hash.ToLowerInvariant()
    bootstrap_sha256 = (Get-FileHash $bootstrap -Algorithm SHA256).Hash.ToLowerInvariant()
    exe_sha256 = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    native_exit_code = $nativeExit
    unity_player_lifecycle_tested = $false
} | ConvertTo-Json | Set-Content (Join-Path $out 'provenance.json') -Encoding utf8
if ($nativeExit -ne 0) { throw 'Native caption checks failed' }
