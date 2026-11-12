param(
    [string]$Version = "1.0.0",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$Out = Join-Path $Root "dist\windows"
$Publish = Join-Path $Out "RixelPOS"

Write-Host "== RixelPOS $Version ($Runtime) ==" -ForegroundColor Cyan
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Path $Publish -Force | Out-Null

dotnet publish (Join-Path $Root "src\RixelPOS.Desktop\RixelPOS.Desktop.csproj") `
    -c Release -r $Runtime --self-contained true `
    -p:DebugType=None -p:DebugSymbols=false `
    -o $Publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$zip = Join-Path $Out "RixelPOS-$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $Publish "*") -DestinationPath $zip -Force
Write-Host "Portable: $zip" -ForegroundColor Green

$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
if ($null -eq $iscc) {
    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if ($iscc) {
    $iss = Join-Path $PSScriptRoot "installer.iss"
    Write-Host "Building installer with $iscc" -ForegroundColor Cyan
    & $iscc "/DAppVersion=$Version" "/O$Out" $iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
    Write-Host "Installer built in $Out" -ForegroundColor Green
} else {
    Write-Host "Inno Setup 6 (iscc.exe) not found. Portable ZIP was generated; install Inno Setup to build the .exe installer." -ForegroundColor Yellow
}

Write-Host "Signing (optional):" -ForegroundColor Cyan
Write-Host '  signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f $env:CERT_PATH /p $env:CERT_PASSWORD dist\windows\RixelPOS-Setup-*.exe'
