<#
    Builds Tsuru.exe.

    Uses the C# compiler that ships with the .NET Framework, so nothing has to
    be installed: the resulting executable runs on any Windows machine with
    .NET Framework 4.x, which Windows 10 and 11 include out of the box.

    Usage:  powershell -ExecutionPolicy Bypass -File build.ps1
#>

[CmdletBinding()]
param(
    [switch] $Run,      # launch the app once it is built
    [switch] $Symbols   # emit debug symbols and skip optimisation
)

$ErrorActionPreference = 'Stop'

$root    = Split-Path -Parent $MyInvocation.MyCommand.Definition
$srcDir  = Join-Path $root 'src'
$outDir  = Join-Path $root 'build'
$objDir  = Join-Path $outDir 'obj'
$exePath = Join-Path $outDir 'Tsuru.exe'
$icoPath = Join-Path $objDir 'app.ico'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw "Could not find the .NET Framework C# compiler (csc.exe)."
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
New-Item -ItemType Directory -Force -Path $objDir | Out-Null

# A running tray instance holds a lock on the executable.
$running = Get-Process -Name 'Tsuru' -ErrorAction SilentlyContinue |
           Where-Object { $_.Path -eq $exePath }
if ($running) {
    Write-Host 'Stopping the running instance...' -ForegroundColor DarkYellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 400
}

$sources = Get-ChildItem -Path $srcDir -Filter *.cs -Recurse | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw "No sources found in $srcDir" }

# Ship-alongside assets live in the repo under assets\ and are copied next to
# the executable, so build\ stays disposable and nothing tracked is lost by
# deleting it.
foreach ($folder in 'fonts', 'assets') {
    $from = Join-Path $root "assets\$folder"
    if (Test-Path $from) {
        $to = Join-Path $outDir $folder
        New-Item -ItemType Directory -Force -Path $to | Out-Null
        Copy-Item "$from\*" $to -Recurse -Force
    }
}

$refs = @(
    '-r:System.dll'
    '-r:System.Core.dll'
    '-r:System.Drawing.dll'
    '-r:System.Windows.Forms.dll'
)

$common = @('-nologo', '-platform:anycpu', '-warn:4', '-nowarn:1591')

# The wordmark is embedded so the shipped executable carries its own icon
# artwork and needs no loose files beside it.
$logo = Join-Path $root 'ico.png'
if (Test-Path $logo) {
    $common += "-resource:$logo,Tsuru.logo.png"
} else {
    Write-Host 'ico.png not found - falling back to the drawn glyph.' -ForegroundColor DarkYellow
}
if ($Symbols) { $common += @('-debug+', '-define:DEBUG') } else { $common += '-optimize+' }

function Invoke-Csc([string[]] $arguments) {
    $output = & $csc @arguments
    if ($LASTEXITCODE -ne 0) {
        $output | ForEach-Object { Write-Host $_ }
        throw "Compilation failed (exit $LASTEXITCODE)."
    }
    $output | Where-Object { $_ -match 'warning' } | ForEach-Object { Write-Host $_ -ForegroundColor DarkYellow }
}

# --- pass 1: a console build used only to render the application icon --------
Write-Host 'Generating icon...' -ForegroundColor Cyan
$iconHost = Join-Path $objDir 'iconhost.exe'
Invoke-Csc ($common + $refs + @('-target:exe', "-out:$iconHost") + $sources)

& $iconHost '--export-icon' $icoPath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $icoPath)) {
    throw "Icon generation failed."
}

# --- pass 2: the real, windowed build ----------------------------------------
Write-Host 'Compiling Tsuru.exe...' -ForegroundColor Cyan

$manifest = Join-Path $root 'app.manifest'
$final = $common + $refs + @(
    '-target:winexe'
    "-out:$exePath"
    "-win32icon:$icoPath"
    "-win32manifest:$manifest"
) + $sources

Invoke-Csc $final

$size = [math]::Round((Get-Item $exePath).Length / 1KB, 1)
Write-Host "Built $exePath ($size KB)" -ForegroundColor Green

if ($Run) {
    Get-Process -Name 'Tsuru' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Process $exePath
    Write-Host 'Tsuru started - check the notification area.' -ForegroundColor Green
}
