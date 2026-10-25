<#
.SYNOPSIS
    Builds EdgeDrop installer and portable zip package for GitHub Release.
#>

$ErrorActionPreference = "Stop"

Write-Host "==> [1/4] Publishing EdgeDrop (Release win-x64)..." -ForegroundColor Cyan
dotnet publish EdgeDrop/EdgeDrop.csproj -c Release -r win-x64 --self-contained false -o publish

# Remove debugging symbols from release bundle
Remove-Item -Path "publish\*.pdb" -Force -ErrorAction SilentlyContinue

if (!(Test-Path "release")) {
    New-Item -ItemType Directory -Path "release" | Out-Null
}

Write-Host "==> [2/4] Compiling Inno Setup installer..." -ForegroundColor Cyan
$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (Get-Command iscc -ErrorAction SilentlyContinue) {
    iscc EdgeDrop_Setup.iss
} elseif (Test-Path $iscc) {
    & $iscc EdgeDrop_Setup.iss
} else {
    Write-Warning "Inno Setup compiler (ISCC.exe) not found. Skipping installer generation."
}

Write-Host "==> [3/4] Creating Portable ZIP archive..." -ForegroundColor Cyan
Compress-Archive -Path "publish\*" -DestinationPath "release\EdgeDrop_v1.0.0_Portable.zip" -Force

Write-Host "==> [4/4] Calculating SHA-256 checksums..." -ForegroundColor Cyan
$files = Get-ChildItem -Path "release" -File | Where-Object { $_.Extension -in '.exe', '.zip' }
Get-FileHash -Path $files.FullName -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash)  $($_.Path | Split-Path -Leaf)" } |
    Out-File -Encoding utf8 "release\SHA256SUMS.txt"

Write-Host "`nRelease build completed successfully!" -ForegroundColor Green
Get-ChildItem -Path "release" | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
