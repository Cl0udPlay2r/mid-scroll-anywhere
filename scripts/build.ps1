# 一步构建：.\scripts\build.ps1 [-Release]
param([switch]$Release)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$config = if ($Release) { 'Release' } else { 'Debug' }

Push-Location $root
try {
    dotnet build -c $config --nologo
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
