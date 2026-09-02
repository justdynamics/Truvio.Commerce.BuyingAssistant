<#
.SYNOPSIS
    Builds the app and drops the DLL into a local Dynamicweb host for development.

.DESCRIPTION
    Development install (no App Store): builds the project against the host's Dynamicweb
    version, copies Truvio.Commerce.BuyingAssistant.dll (and Anthropic.dll when the host does
    not already ship it) into the host's bin output folder, and optionally restarts the host.
    The app installs its item type and paragraph layout into Files/ on startup.

.PARAMETER HostProject
    Path to the host's Dynamicweb.Host.Suite folder (contains the csproj and bin\).

.PARAMETER DynamicwebVersion
    Dynamicweb package version to compile against (must match the host, e.g. 10.27.9).

.PARAMETER Restart
    Stop the running host (dotnet process serving it) and start it again detached.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$HostProject,
    [string]$DynamicwebVersion = "10.27.9",
    [string]$Configuration = "Debug",
    [switch]$Restart,
    [string]$LaunchProfile = "Dynamicweb.Host.Suite"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src\Truvio.Commerce.BuyingAssistant\Truvio.Commerce.BuyingAssistant.csproj"
$out = Join-Path $root "src\Truvio.Commerce.BuyingAssistant\bin\$Configuration\net8.0"

Write-Host "[deploy] building against Dynamicweb $DynamicwebVersion" -ForegroundColor Cyan
dotnet build $proj -c $Configuration -p:DynamicwebVersion=$DynamicwebVersion | Out-Host
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$binDirs = Get-ChildItem (Join-Path $HostProject "bin") -Directory -Recurse | Where-Object { $_.Name -like "net*" -and (Test-Path (Join-Path $_.FullName "Dynamicweb.dll")) }
if (-not $binDirs) { throw "no host bin folder with Dynamicweb.dll under $HostProject\bin" }

if ($Restart) {
    $csproj = Get-ChildItem $HostProject -Filter *.csproj | Select-Object -First 1
    $procs = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" | Where-Object { $_.CommandLine -like "*$($csproj.BaseName)*" }
    foreach ($p in $procs) { Write-Host "[deploy] stopping host pid $($p.ProcessId)"; Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 3
}

foreach ($dir in $binDirs) {
    Copy-Item (Join-Path $out "Truvio.Commerce.BuyingAssistant.dll") $dir.FullName -Force
    if (-not (Test-Path (Join-Path $dir.FullName "Anthropic.dll"))) {
        Copy-Item (Join-Path $out "Anthropic.dll") $dir.FullName -Force
    }
    Write-Host "[deploy] copied into $($dir.FullName)" -ForegroundColor Green
}

if ($Restart) {
    $csproj = Get-ChildItem $HostProject -Filter *.csproj | Select-Object -First 1
    $log = Join-Path $HostProject "host-start.log"
    $err = Join-Path $HostProject "host-err.log"
    $p = Start-Process dotnet -ArgumentList 'run', '--project', $csproj.FullName, '--no-build', '--launch-profile', $LaunchProfile -WorkingDirectory $HostProject -RedirectStandardOutput $log -RedirectStandardError $err -WindowStyle Hidden -PassThru
    Write-Host "[deploy] host started pid $($p.Id); log $log" -ForegroundColor Green
}
