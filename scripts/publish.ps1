# 发布便携版到 .\dist（自带运行时，单个 exe）：.\scripts\publish.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Push-Location $root
try {
    dotnet publish src/MidScrollAnywhere.App/MidScrollAnywhere.App.csproj `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none `
        -o dist --nologo

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    Write-Host ''
    Write-Host '构建完成，自检中…' -ForegroundColor Cyan
    & ./dist/MidScrollAnywhere.exe --selftest
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
