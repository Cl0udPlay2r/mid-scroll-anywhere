# 跑单元测试：.\scripts\test.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Push-Location $root
try {
    dotnet test --nologo
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
