# Kraken Unlocker - Build, Obfuscate & Publish
# Usage: cd KrakenUnlocker ; .\build_release.ps1

$publishDir = "bin\Release\net8.0-windows10.0.22621.0\win-x64\publish"

Write-Host "=== Kraken Unlocker Build ===" -ForegroundColor Cyan

Get-Process -Name "KrakenXboxUnlocker" -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "`nPublishing self-contained single EXE..." -ForegroundColor Yellow
dotnet publish -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true 2>&1 | Select-String "error CS|Build succeeded|FAILED|->|KrakenXboxUnlocker" | Select-Object -Last 10

if ($LASTEXITCODE -ne 0) { Write-Host "Build failed!" -ForegroundColor Red; exit 1 }

$exe = "$publishDir\KrakenXboxUnlocker.exe"
if (Test-Path $exe) {
    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "`nDone! $exe ($size MB)" -ForegroundColor Green

    # Generate SHA256 Hash File
    $hashPath = "$publishDir\KrakenXboxUnlocker.sha256"
    $hash = (Get-FileHash -Path $exe -Algorithm SHA256).Hash.ToLower()
    Set-Content -Path $hashPath -Value $hash -NoNewline
    Write-Host "Generated Hash: $hash" -ForegroundColor Yellow
} else {
    Write-Host "Publish failed!" -ForegroundColor Red
}
