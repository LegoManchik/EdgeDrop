<#
.SYNOPSIS
    Builds EdgeDrop installer, portable zip package, and standalone CLI for GitHub Release.
#>

$ErrorActionPreference = "Stop"

Write-Host "==> [1/5] Publishing EdgeDrop GUI (Release win-x64)..." -ForegroundColor Cyan
dotnet publish EdgeDrop/EdgeDrop.csproj -c Release -r win-x64 --self-contained false -o publish

Write-Host "==> [2/5] Publishing EdgeDrop CLI (Release win-x64)..." -ForegroundColor Cyan
dotnet publish EdgeDrop.Cli/EdgeDrop.Cli.csproj -c Release -r win-x64 --self-contained false -o publish

# Standalone CLI package
dotnet publish EdgeDrop.Cli/EdgeDrop.Cli.csproj -c Release -r win-x64 --self-contained false -o publish_cli

# Create 'edgedrop.exe' alias copy so users can type either 'edgedrop' or 'edgedrop-cli'
Copy-Item "publish\edgedrop-cli.exe" -Destination "publish\edgedrop.exe" -Force
Copy-Item "publish_cli\edgedrop-cli.exe" -Destination "publish_cli\edgedrop.exe" -Force

# Create 1-click 'add_to_path.cmd' for portable zip users
$addToPathCmd = @"
@echo off
setlocal
echo Adding EdgeDrop to User PATH environment variable...
set "TARGET_DIR=%~dp0"
set "TARGET_DIR=%TARGET_DIR:~0,-1%"

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "`$dir = '%TARGET_DIR%';" ^
  "`$path = [Environment]::GetEnvironmentVariable('Path', 'User');" ^
  "if (`$path -split ';' -notcontains `$dir) {" ^
  "    [Environment]::SetEnvironmentVariable('Path', `$path.TrimEnd(';') + ';' + `$dir, 'User');" ^
  "    Write-Host '[OK] EdgeDrop successfully added to your PATH!' -ForegroundColor Green;" ^
  "    Write-Host 'You can now open a new PowerShell or Command Prompt and type: edgedrop' -ForegroundColor Cyan;" ^
  "} else {" ^
  "    Write-Host '[INFO] EdgeDrop is already in your PATH.' -ForegroundColor Yellow;" ^
  "}"
pause
"@
Set-Content -Path "publish\add_to_path.cmd" -Value $addToPathCmd -Encoding ASCII
Set-Content -Path "publish_cli\add_to_path.cmd" -Value $addToPathCmd -Encoding ASCII

# Remove debugging symbols
Remove-Item -Path "publish\*.pdb" -Force -ErrorAction SilentlyContinue
Remove-Item -Path "publish_cli\*.pdb" -Force -ErrorAction SilentlyContinue

if (!(Test-Path "release")) {
    New-Item -ItemType Directory -Path "release" | Out-Null
}

Write-Host "==> [3/5] Compiling Inno Setup installer..." -ForegroundColor Cyan
$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (Get-Command iscc -ErrorAction SilentlyContinue) {
    iscc EdgeDrop_Setup.iss
} elseif (Test-Path $iscc) {
    & $iscc EdgeDrop_Setup.iss
} else {
    Write-Warning "Inno Setup compiler (ISCC.exe) not found. Skipping installer generation."
}

Write-Host "==> [4/5] Creating Portable ZIP archives..." -ForegroundColor Cyan
Compress-Archive -Path "publish\*" -DestinationPath "release\EdgeDrop_v1.1.0_Portable.zip" -Force
Compress-Archive -Path "publish_cli\*" -DestinationPath "release\EdgeDrop_CLI_v1.1.0_win-x64.zip" -Force
Remove-Item -Path "publish_cli" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "==> [5/5] Calculating SHA-256 checksums..." -ForegroundColor Cyan
$files = Get-ChildItem -Path "release" -File | Where-Object { $_.Extension -in '.exe', '.zip' }
Get-FileHash -Path $files.FullName -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash)  $($_.Path | Split-Path -Leaf)" } |
    Out-File -Encoding utf8 "release\SHA256SUMS.txt"

Write-Host "`nRelease build completed successfully!" -ForegroundColor Green
Get-ChildItem -Path "release" | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
