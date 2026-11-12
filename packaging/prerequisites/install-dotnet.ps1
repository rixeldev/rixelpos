# RixelPOS .NET SDK bootstrap for Windows 10/11.
# RixelPOS is published self-contained and does NOT require .NET at runtime.
# Use this for source builds, development or optional SDK installation.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File install-dotnet.ps1 -Channel 8.0 [-Quiet]
param(
    [ValidateSet("8.0", "10.0")]
    [string]$Channel = "8.0",
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

function Test-DotNetSdk {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { return $false }
    $sdks = & dotnet --list-sdks 2>$null
    return [bool]($sdks | Where-Object { $_ -match '^(8|10)\.' })
}

if (Test-DotNetSdk) {
    if (-not $Quiet) {
        Write-Host "==> .NET SDK already installed:" -ForegroundColor Green
        & dotnet --list-sdks
    }
    exit 0
}

Write-Host "==> Installing .NET SDK $Channel" -ForegroundColor Cyan
$wingetId = if ($Channel -eq "10.0") { "Microsoft.DotNet.SDK.10" } else { "Microsoft.DotNet.SDK.8" }

if (Get-Command winget -ErrorAction SilentlyContinue) {
    & winget install --id $wingetId --exact --silent --accept-package-agreements --accept-source-agreements
    if (Test-DotNetSdk) {
        if (-not $Quiet) { & dotnet --list-sdks }
        exit 0
    }
}

Write-Host "==> winget unavailable or incomplete. Using dotnet-install.ps1." -ForegroundColor Yellow
$tmp = Join-Path $env:TEMP ("rixelpos-dotnet-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
try {
    $script = Join-Path $tmp "dotnet-install.ps1"
    Invoke-WebRequest -UseBasicParsing "https://dot.net/v1/dotnet-install.ps1" -OutFile $script
    $installDir = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"
    & $script -Channel $Channel -InstallDir $installDir -NoPath
    if (-not (Test-DotNetSdk)) {
        $env:DOTNET_ROOT = $installDir
        $env:PATH = "$installDir;$env:PATH"
        $userPath = [Environment]::GetEnvironmentVariable("PATH", "User")
        if ($null -eq $userPath) { $userPath = "" }
        if (-not $userPath.Contains($installDir)) {
            [Environment]::SetEnvironmentVariable("PATH", "$installDir;$userPath", "User")
        }
        if (-not [Environment]::GetEnvironmentVariable("DOTNET_ROOT", "User")) {
            [Environment]::SetEnvironmentVariable("DOTNET_ROOT", $installDir, "User")
        }
    }
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-DotNetSdk) {
    Write-Host "==> .NET SDK ready." -ForegroundColor Green
    if (-not $Quiet) { & dotnet --list-sdks }
    exit 0
}

Write-Host "!! .NET SDK installation could not be verified. Download it from https://dotnet.microsoft.com/download" -ForegroundColor Red
exit 1
