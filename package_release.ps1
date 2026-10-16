$ErrorActionPreference = 'Stop'
Write-Host "Cleaning dist/InstallerBundle..."
if (Test-Path "dist\InstallerBundle") {
    Remove-Item -Recurse -Force "dist\InstallerBundle"
}
New-Item -ItemType Directory -Force -Path "dist\InstallerBundle\Payload" | Out-Null

$appReleaseDir = "src\DiscordNative.App\bin\x64\Release\net8.0-windows10.0.19041.0"
$installerReleaseDir = "src\DiscordNative.Installer\bin\x64\Release\net8.0-windows10.0.19041.0"

Write-Host "Copying App release files..."
Copy-Item -Recurse -Force "$appReleaseDir\*" "dist\InstallerBundle\Payload\"

Write-Host "Copying Installer release files..."
Copy-Item -Force "$installerReleaseDir\DiscordNative.Installer.exe" "dist\InstallerBundle\Setup.exe"

# Also copy dependencies required by DiscordNative.Installer if any
Get-ChildItem -Path $installerReleaseDir -File | Where-Object { $_.Name -ne "DiscordNative.Installer.exe" -and $_.Name -ne "app.ico" } | ForEach-Object {
    Copy-Item -Force $_.FullName "dist\InstallerBundle\"
}

Write-Host "Packaging Portable Zip..."
if (Test-Path "dist\Diswin-v0.3.3-win-x64-portable.zip") {
    Remove-Item -Force "dist\Diswin-v0.3.3-win-x64-portable.zip"
}
Compress-Archive -Path "dist\InstallerBundle\Payload\*" -DestinationPath "dist\Diswin-v0.3.3-win-x64-portable.zip"

Write-Host "Packaging Setup Zip..."
if (Test-Path "dist\Diswin-Setup-v0.3.3-win-x64.zip") {
    Remove-Item -Force "dist\Diswin-Setup-v0.3.3-win-x64.zip"
}
Compress-Archive -Path "dist\InstallerBundle\*" -DestinationPath "dist\Diswin-Setup-v0.3.3-win-x64.zip"

Write-Host "Done! Artifacts:"
Get-ChildItem "dist\*.zip" | Select-Object Name, Length, LastWriteTime
