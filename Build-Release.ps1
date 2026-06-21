[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent,
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$project = Join-Path $PSScriptRoot "src\NeonShield\NeonShield.csproj"
$output = Join-Path $PSScriptRoot "release\$Runtime"
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "release"))
$resolvedOutput = [IO.Path]::GetFullPath($output)

if (-not $resolvedOutput.StartsWith(
        $releaseRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsicherer Release-Zielpfad: $resolvedOutput"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK wurde nicht gefunden. Installiere das .NET 10 SDK und starte den Build erneut."
}

if (-not (Test-Path $project)) {
    throw "Projektdatei wurde nicht gefunden: $project"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$projectXml = Get-Content -Raw $project
    $Version = [string]$projectXml.Project.PropertyGroup.Version
}

$Version = $Version.Trim().TrimStart("v")
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') {
    throw "Ungültige Versionsnummer: $Version"
}

$numericVersion = $Version.Split("-")[0]
$assemblyVersion = "$numericVersion.0"

$runningRelease = Get-Process -ErrorAction SilentlyContinue |
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
    $processDescriptions = $runningRelease |
        ForEach-Object { "$($_.ProcessName) (PID $($_.Id))" }
    throw "Im Release-Ordner laufen noch Prozesse: $($processDescriptions -join ', '). Beende den Scan beziehungsweise NeonShield und starte den Build erneut."
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

$arguments = @(
    "publish",
    $project,
    "--configuration", "Release",
    "--runtime", $Runtime,
    "--output", $output,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:Version=$Version",
    "-p:AssemblyVersion=$assemblyVersion",
    "-p:FileVersion=$assemblyVersion",
    "-p:InformationalVersion=$Version",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
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
Write-Host "NeonShield $Version wurde erstellt:" -ForegroundColor Green
Write-Host (Join-Path $output "NeonShield.exe")
