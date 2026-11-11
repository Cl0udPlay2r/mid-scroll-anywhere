# Upload (or re-upload) the release asset using curl.exe.
# PowerShell's HTTP stack sometimes gets "connection closed while sending" on large
# uploads; curl streams with a proper Content-Length and retries.
#   .\scripts\upload-asset.ps1
param(
    [string]$Tag = 'v0.1.0',
    [string]$Repo = 'Cl0udPlay2r/mid-scroll-anywhere',
    [string]$Asset = 'dist/MidScrollAnywhere.exe'
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
        throw 'No GitHub credential in the credential store.'
    }

    $token = $line.Substring(9)
    $headers = @{ Authorization = "token $token"; 'User-Agent' = 'mid-scroll-anywhere-release'; Accept = 'application/vnd.github+json' }

    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/tags/$Tag" -Headers $headers -TimeoutSec 120
    $assetName = Split-Path -Leaf $Asset

    $existing = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/$($release.id)/assets" -Headers $headers -TimeoutSec 120
    foreach ($old in @($existing | Where-Object { $_.name -eq $assetName })) {
        Write-Host "deleting previous asset $($old.id)..."
        Invoke-RestMethod -Method Delete -Uri "https://api.github.com/repos/$Repo/releases/assets/$($old.id)" -Headers $headers -TimeoutSec 120 | Out-Null
    }

    $sizeMb = [math]::Round((Get-Item $Asset).Length / 1MB, 1)
    Write-Host "uploading $assetName ($sizeMb MB) to $($release.html_url) ..."

    $uploadUrl = "https://uploads.github.com/repos/$Repo/releases/$($release.id)/assets?name=$assetName"
    & curl.exe --silent --show-error --fail-with-body --location `
        --request POST `
        --header "Authorization: token $token" `
        --header 'User-Agent: mid-scroll-anywhere-release' `
        --header 'Content-Type: application/octet-stream' `
        --data-binary "@$Asset" `
        --max-time 3600 --retry 3 --retry-delay 5 `
        --ssl-no-revoke `
        $uploadUrl 2>&1 | Out-String | Write-Verbose

    if ($LASTEXITCODE -ne 0) {
        throw "curl failed with exit code $LASTEXITCODE"
    }

    $final = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/tags/$Tag" -Headers $headers -TimeoutSec 120
    foreach ($a in $final.assets) {
        Write-Host ("asset: {0} ({1} MB) {2}" -f $a.name, [math]::Round($a.size / 1MB, 1), $a.browser_download_url)
    }

    Write-Host "sha256: " + (Get-FileHash $Asset -Algorithm SHA256).Hash.ToLowerInvariant()
}
finally {
    Pop-Location
}
