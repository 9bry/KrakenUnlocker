param(
    [switch]$SkipBuild,
    [switch]$SkipObfuscate
)

$ErrorActionPreference = "Stop"
$projectDir = $PSScriptRoot
$outputDir = "$projectDir\publish_test"
$obfDir = "$projectDir\obfuscated"
$confuserEx = "$projectDir\tools\ConfuserEx2\Confuser.CLI.exe"
$releaseDir = "$projectDir\release"

Write-Host "=== KrakenXboxUnlocker Build Pipeline ===" -ForegroundColor Cyan

# Step 1: Clean
if (Test-Path $outputDir) { Remove-Item $outputDir -Recurse -Force }
if (Test-Path $obfDir) { Remove-Item $obfDir -Recurse -Force }

# Step 2: Publish (folder output, not single-file)
if (-not $SkipBuild) {
    Write-Host "`n[1/5] Publishing..." -ForegroundColor Yellow
    dotnet publish "$projectDir\KrakenUnlocker\KrakenUnlocker.csproj" `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        -o $outputDir
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
    Write-Host "  Published to $outputDir" -ForegroundColor Green
}

# Step 3: Obfuscate
if (-not $SkipObfuscate) {
    Write-Host "`n[2/5] Obfuscating with ConfuserEx2..." -ForegroundColor Yellow
    if (!(Test-Path $confuserEx)) { throw "ConfuserEx2 CLI not found at $confuserEx" }

    & $confuserEx -n -o="$obfDir" "$outputDir\KrakenXboxUnlocker.dll" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Obfuscation failed" }

    # Replace original DLL with obfuscated version
    Copy-Item "$obfDir\KrakenXboxUnlocker.dll" "$outputDir\KrakenXboxUnlocker.dll" -Force
    Write-Host "  Obfuscated DLL applied" -ForegroundColor Green
}

# Step 4: Generate SHA256 hash
$exePath = "$outputDir\KrakenXboxUnlocker.exe"
if (Test-Path $exePath) {
    Write-Host "`n[3/5] Generating SHA256 hash..." -ForegroundColor Yellow
    $hash = (Get-FileHash $exePath -Algorithm SHA256).Hash.ToLower()
    $hashFile = "$outputDir\KrakenXboxUnlocker.exe.sha256"
    Set-Content -Path $hashFile -Value $hash -NoNewline
    Write-Host "  Hash: $hash" -ForegroundColor Green
}

# Step 5: Copy to release directory
Write-Host "`n[4/5] Copying to release directory..." -ForegroundColor Yellow
if (!(Test-Path $releaseDir)) { New-Item -ItemType Directory -Path $releaseDir -Force }
Copy-Item "$outputDir\KrakenXboxUnlocker.exe" "$releaseDir\" -Force
Copy-Item "$outputDir\KrakenXboxUnlocker.dll" "$releaseDir\" -Force
if (Test-Path "$outputDir\KrakenXboxUnlocker.exe.sha256") {
    Copy-Item "$outputDir\KrakenXboxUnlocker.exe.sha256" "$releaseDir\" -Force
}

# Step 6: Zip for distribution
Write-Host "`n[5/5] Creating distribution zip..." -ForegroundColor Yellow
$zipPath = "$releaseDir\KrakenXboxUnlocker.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# Copy publish output into a named folder, then zip it
$zipSourceDir = "$releaseDir\dist"
if (Test-Path $zipSourceDir) { Remove-Item $zipSourceDir -Recurse -Force }
$innerDir = "$zipSourceDir\KrakenXboxUnlocker"
Copy-Item $outputDir $innerDir -Recurse

Compress-Archive -Path "$zipSourceDir\KrakenXboxUnlocker" -DestinationPath $zipPath -Force
Remove-Item $zipSourceDir -Recurse -Force

Write-Host "  Zip created: $zipPath" -ForegroundColor Green

Write-Host "`n=== Build Complete ===" -ForegroundColor Cyan
Write-Host "Release: $releaseDir\"
Write-Host "Zip: $zipPath"
