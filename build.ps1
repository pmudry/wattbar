# Builds WattBar into .\dist\WattBar.exe (single framework-dependent exe).
# Uses the user-local SDK if the machine-wide dotnet has none.
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dotnet = "dotnet"
if (-not (dotnet --list-sdks 2>$null)) {
    $local = Join-Path $env:LOCALAPPDATA "dotnet\dotnet.exe"
    if (Test-Path $local) { $dotnet = $local } else { throw "No .NET SDK found. Run: winget install Microsoft.DotNet.SDK.10" }
}
& $dotnet publish (Join-Path $root "src\WattBar.csproj") -c Release -o (Join-Path $root "dist") --nologo
if ($LASTEXITCODE -eq 0) { Write-Host "-> $(Join-Path $root 'dist\WattBar.exe')" }
