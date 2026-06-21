[CmdletBinding()]
param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "src\NeonShield\NeonShield.csproj"
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$projectXml = Get-Content -Raw $project
    $Version = [string]$projectXml.Project.PropertyGroup.Version
}

$Version = $Version.Trim().TrimStart("v")
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') {
    throw "Ungültige Versionsnummer: $Version"
}

& (Join-Path $PSScriptRoot "Build-Release.ps1") -Runtime win-x64 -Version $Version
if ($LASTEXITCODE -ne 0) {
    throw "Der Anwendungs-Build ist fehlgeschlagen."
}

$compilerCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
) | Where-Object { $_ -and (Test-Path $_) }

$compiler = $compilerCandidates | Select-Object -First 1
if (-not $compiler) {
    throw @"
Inno Setup 6 wurde nicht gefunden.
Installiere es von https://jrsoftware.org/isdl.php und starte Build-Installer.ps1 erneut.
"@
}

$script = Join-Path $PSScriptRoot "installer\NeonShield.iss"
& $compiler "/DAppVersion=$Version" $script
if ($LASTEXITCODE -ne 0) {
    throw "Der Installer-Build ist fehlgeschlagen."
}

Write-Host ""
Write-Host "Installer erstellt:" -ForegroundColor Green
Get-ChildItem (Join-Path $PSScriptRoot "release\installer") -Filter "NeonShield-Setup-*.exe" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 -ExpandProperty FullName
