# Builds the solution and writes a compact, greppable result file.
# Usage: powershell -ExecutionPolicy Bypass -File scripts/build.ps1 [-Project <path>]
param(
    [string]$Project = "PersonalResearchLab.sln"
)

$ErrorActionPreference = "Continue"
$env:Path = "C:\Program Files\dotnet;" + $env:Path
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$log = Join-Path $root "build-output.log"
dotnet build $Project --nologo 2>&1 | Out-File -FilePath $log -Encoding utf8

$content = Get-Content $log
$errors = $content | Select-String -Pattern ": error " | ForEach-Object { $_.Line.Trim() } | Sort-Object -Unique
$summary = $content | Select-String -Pattern "Build succeeded|Build FAILED|Warning\(s\)|Error\(s\)" | ForEach-Object { $_.Line.Trim() }

Write-Output "=== SUMMARY ==="
$summary | ForEach-Object { Write-Output $_ }
Write-Output "=== ERRORS ($($errors.Count)) ==="
$errors | Select-Object -First 40 | ForEach-Object { Write-Output $_ }
