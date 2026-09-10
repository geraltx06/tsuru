<#
    Builds the app, then compiles installer\Tsuru.iss into dist\.

    Needs Inno Setup 6. If it is missing:
        winget install --id JRSoftware.InnoSetup --silent

    Usage:  powershell -ExecutionPolicy Bypass -File make-installer.ps1
#>

[CmdletBinding()]
param(
    [switch] $SkipBuild        # compile the installer against the existing build\
)

$ErrorActionPreference = 'Stop'

$root      = Split-Path -Parent $MyInvocation.MyCommand.Definition
$script    = Join-Path $root 'installer\Tsuru.iss'
$distDir   = Join-Path $root 'dist'
$exePath   = Join-Path $root 'build\Tsuru.exe'

function Find-ISCC {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return $c } }

    $onPath = Get-Command 'iscc' -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    return $null
}

if (-not $SkipBuild) {
    Write-Host 'Building Tsuru.exe...' -ForegroundColor Cyan
    & powershell -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}

if (-not (Test-Path $exePath)) { throw "Missing $exePath - run build.ps1 first." }

$iscc = Find-ISCC
if (-not $iscc) {
    throw @"
Inno Setup 6 was not found.

Install it with:
    winget install --id JRSoftware.InnoSetup --silent

then run this script again.
"@
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

Write-Host "Compiling installer with $iscc" -ForegroundColor Cyan
& $iscc $script
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed (exit $LASTEXITCODE)." }

Get-ChildItem $distDir -Filter '*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1 |
    ForEach-Object {
        Write-Host ("Built {0} ({1:N0} KB)" -f $_.FullName, ($_.Length / 1KB)) -ForegroundColor Green
    }
