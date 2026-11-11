# Publish a GitHub release with the portable exe attached, and refresh the repo
# description + topics. Token comes from the Git credential store (never printed).
#   .\scripts\gh-release.ps1
param(
    [string]$Tag = 'v0.1.0',
    [string]$Repo = 'Cl0udPlay2r/mid-scroll-anywhere',
    [string]$Asset = 'dist/MidScrollAnywhere.exe',
    [string]$Notes = 'release/notes-v0.1.0.md',
    [string]$TitleFile = 'release/title.txt',
    [string]$DescriptionFile = 'release/description.txt',
    [string]$Branch = 'master'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

try {
    $env:GCM_INTERACTIVE = 'never'
    $env:GIT_TERMINAL_PROMPT = '0'

    $payload = "protocol=https`nhost=github.com`n`n"
    $creds = $payload | git credential fill 2>$null
    $line = $creds | Where-Object { $_ -like 'password=*' } | Select-Object -First 1
    if (-not $line) {
        throw 'No GitHub credential in the credential store; push once with a PAT or set one up first.'
    }

    $token = $line.Substring(9)
    $headers = @{
        Authorization          = "token $token"
        'User-Agent'           = 'mid-scroll-anywhere-release'
        Accept                 = 'application/vnd.github+json'
    }

    # 1. repo description
    if (Test-Path $DescriptionFile) {
        $description = (Get-Content $DescriptionFile -Raw -Encoding utf8).Trim()
        $json = @{ description = $description; homepage = '' } | ConvertTo-Json
        Invoke-RestMethod -Method Patch -Uri "https://api.github.com/repos/$Repo" -Headers $headers `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($json)) -ContentType 'application/json; charset=utf-8' `
            -TimeoutSec 120 | Out-Null
        Write-Host "description updated: $description"
    }

    # 2. topics
    $topics = @(
        'windows', 'autoscroll', 'middle-click', 'mouse', 'scrolling',
        'csharp', 'winforms', 'dotnet', 'chromium', 'firefox',
        'tray-app', 'desktop-app'
    )
    $topicJson = @{ names = $topics } | ConvertTo-Json
    Invoke-RestMethod -Method Put -Uri "https://api.github.com/repos/$Repo/topics" -Headers $headers `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($topicJson)) -ContentType 'application/json; charset=utf-8' `
        -TimeoutSec 120 | Out-Null
    Write-Host "topics updated: $($topics -join ', ')"

    # 3. release
    $title = if (Test-Path $TitleFile) { (Get-Content $TitleFile -Raw -Encoding utf8).Trim() } else { $Tag }
    $body = if (Test-Path $Notes) { (Get-Content $Notes -Raw -Encoding utf8) } else { '' }

    $hash = (Get-FileHash $Asset -Algorithm SHA256).Hash.ToLowerInvariant()
    $body += "`n`n### SHA256`n`````` `n$hash  MidScrollAnywhere.exe`n``````n"

    $releaseBody = @{
        tag_name         = $Tag
        name             = $title
        body             = $body
        draft            = $false
        prerelease       = $false
        target_commitish = $Branch
    } | ConvertTo-Json -Depth 3

    try {
        $release = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$Repo/releases" `
            -Headers $headers -Body ([System.Text.Encoding]::UTF8.GetBytes($releaseBody)) `
            -ContentType 'application/json; charset=utf-8' -TimeoutSec 120
        Write-Host "release created: $($release.html_url)"
    }
    catch {
        $existing = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/tags/$Tag" -Headers $headers -TimeoutSec 60
        $release = $existing
        Write-Host "release already exists, reusing: $($release.html_url)"
    }

    # 4. upload asset
    $assetName = Split-Path -Leaf $Asset
    $sizeMb = [math]::Round((Get-Item $Asset).Length / 1MB, 1)
    Write-Host "uploading $assetName ($sizeMb MB)..."
    $uploadUrl = "https://uploads.github.com/repos/$Repo/releases/$($release.id)/assets?name=$assetName"
    $assetInfo = Invoke-RestMethod -Method Post -Uri $uploadUrl -Headers $headers -InFile $Asset `
        -ContentType 'application/octet-stream' -TimeoutSec 3600

    Write-Host "asset uploaded: $($assetInfo.browser_download_url)"
    Write-Host "sha256: $hash"
    Write-Host "done: $($release.html_url)"
}
finally {
    Pop-Location
}
