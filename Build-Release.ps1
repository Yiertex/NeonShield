[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$project = Join-Path $PSScriptRoot "src\NeonShield\NeonShield.csproj"
$output = Join-Path $PSScriptRoot "release\$Runtime"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK wurde nicht gefunden. Installiere das .NET 10 SDK und starte den Build erneut."
}

if (-not (Test-Path $project)) {
    throw "Projektdatei wurde nicht gefunden: $project"
}

$runningRelease = Get-Process -Name "NeonShield" -ErrorAction SilentlyContinue |
    Where-Object {
        try {
            $_.Path -and
            ([IO.Path]::GetFullPath($_.Path)).StartsWith(
                [IO.Path]::GetFullPath($output),
                [StringComparison]::OrdinalIgnoreCase)
        } catch {
            $false
        }
    }

if ($runningRelease) {
    $processIds = ($runningRelease.Id -join ", ")
    throw "NeonShield läuft noch aus dem Release-Ordner (Prozess-ID: $processIds). Schließe die Anwendung und starte den Build erneut."
}

$arguments = @(
    "publish",
    $project,
    "--configuration", "Release",
    "--runtime", $Runtime,
    "--output", $output,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true"
)

if ($FrameworkDependent) {
    $arguments += "--self-contained"
    $arguments += "false"
} else {
    $arguments += "--self-contained"
    $arguments += "true"
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Der Release-Build ist fehlgeschlagen."
}

Write-Host ""
Write-Host "NeonShield wurde erstellt:" -ForegroundColor Green
Write-Host (Join-Path $output "NeonShield.exe")
