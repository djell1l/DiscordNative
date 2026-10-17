$ErrorActionPreference = 'Stop'
$version = "0.4.5"
$appReleaseDir = "src\DiscordNative.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"
$installerReleaseDir = "src\DiscordNative.Installer\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"

# Restore NuGet packages
Write-Host "0. Restoring NuGet packages..." -ForegroundColor Cyan
dotnet restore .\DiscordNative.sln

# Dynamically locate Appx tools and MSBuild (prioritizing VS 2022 on GitHub Actions & local machines)
$appxTools = Get-ChildItem "$env:ProgramFiles\Microsoft Visual Studio\2022" -Recurse -Filter Microsoft.Build.Packaging.Pri.Tasks.dll -ErrorAction SilentlyContinue |
    Select-Object -First 1 | ForEach-Object { $_.DirectoryName + "\" }

if (-not $appxTools) {
    $appxTools = Get-ChildItem "$env:ProgramFiles\Microsoft Visual Studio" -Recurse -Filter Microsoft.Build.Packaging.Pri.Tasks.dll -ErrorAction SilentlyContinue |
        Select-Object -First 1 | ForEach-Object { $_.DirectoryName + "\" }
}

$msbuild = $null
if ($appxTools) {
    $env:AppxMSBuildToolsPath = $appxTools
    $vsDir = $appxTools
    for ($i = 0; $i -lt 4; $i++) { $vsDir = Split-Path $vsDir }
    $candidateMsBuild = Join-Path $vsDir "Current\Bin\MSBuild.exe"
    if (Test-Path $candidateMsBuild) {
        $msbuild = $candidateMsBuild
    }
}

if (-not $msbuild -or -not (Test-Path $msbuild)) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $msbuild = if (Test-Path $vswhere) {
        & $vswhere -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
    } else { $null }
}

if (-not $msbuild -or -not (Test-Path $msbuild)) {
    $candidates = @(
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
    )
    $msbuild = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $msbuild -or -not (Test-Path $msbuild)) {
    throw "MSBuild.exe introuvable. Assurez-vous que Visual Studio 2022 ou les Build Tools sont installés."
}

$csc = $null
$roslynCsc = Join-Path (Split-Path $msbuild) "Roslyn\csc.exe"
if (Test-Path $roslynCsc) {
    $csc = $roslynCsc
} else {
    $csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
}

Write-Host "Utilisation de MSBuild : $msbuild" -ForegroundColor Cyan
Write-Host "Utilisation de CSC     : $csc" -ForegroundColor Cyan

Write-Host "1. Building DiscordNative.App (Release x64 Self-Contained)..."
& $msbuild "src\DiscordNative.App\DiscordNative.App.csproj" /p:Configuration=Release /p:Platform=x64 /m /v:minimal

# Bundle Discord's official Krisp binary + neural models if present locally so Krisp works standalone anywhere
$localDiscordKrisp = Get-ChildItem "$env:LOCALAPPDATA\Discord*\app-*\modules\discord_krisp*\discord_krisp\discord_krisp.node" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
if ($localDiscordKrisp) {
    $krispSrcDir = $localDiscordKrisp.DirectoryName
    $krispDestDir = Join-Path $appReleaseDir "krisp"
    New-Item -ItemType Directory -Force -Path $krispDestDir | Out-Null
    Copy-Item -Force "$krispSrcDir\discord_krisp.node" "$krispDestDir\"
    Copy-Item -Force "$krispSrcDir\*.kef" "$krispDestDir\"
    Write-Host "   -> Bundled official Discord Krisp engine & neural models from $krispSrcDir"
}

Write-Host "2. Building DiscordNative.Installer (WinUI 3 Setup.exe)..."
& $msbuild "src\DiscordNative.Installer\DiscordNative.Installer.csproj" /p:Configuration=Release /p:Platform=x64 /m /v:minimal

# Clean up satellite culture clutter folders
$knownLangs = @("fr-FR", "fr", "en-US", "en")
Get-ChildItem -Directory $appReleaseDir | Where-Object { $_.Name -match "^[a-z]{2}(-[A-Z]{2,4})?$" -and $_.Name -notin $knownLangs } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "3. Preparing Deduplicated Installer Bundle..."
if (Test-Path "dist\InstallerBundle") {
    Remove-Item -Recurse -Force "dist\InstallerBundle"
}
New-Item -ItemType Directory -Force -Path "dist\InstallerBundle\AppXbf" | Out-Null

# Copy all App files first (contains shared .NET 8 + WinUI 3 runtime)
Copy-Item -Recurse -Force "$appReleaseDir\*" "dist\InstallerBundle\"

# Save the App's App.xbf before overwriting with Installer's App.xbf
Copy-Item -Force "$appReleaseDir\App.xbf" "dist\InstallerBundle\AppXbf\App.xbf"

# Overlay Installer-specific files (Setup.exe, Setup.dll, Setup.pri, App.xbf, MainWindow.xbf, etc.)
Copy-Item -Force "$installerReleaseDir\Setup.*" "dist\InstallerBundle\"
Copy-Item -Force "$installerReleaseDir\App.xbf" "dist\InstallerBundle\App.xbf"
Copy-Item -Force "$installerReleaseDir\MainWindow.xbf" "dist\InstallerBundle\MainWindow.xbf"
Copy-Item -Force "$installerReleaseDir\AppIcon.png" "dist\InstallerBundle\AppIcon.png"

Write-Host "4. Packaging Portable Zip..."
$portableZip = "dist\Diswin-v$version-win-x64-portable.zip"
if (Test-Path $portableZip) { Remove-Item -Force $portableZip }
Compress-Archive -Path "$appReleaseDir\*" -DestinationPath $portableZip -CompressionLevel Optimal

Write-Host "5. Building Single-File WinUI 3 Setup.exe..."
$bundleZip = "dist\bundle_temp.zip"
if (Test-Path $bundleZip) { Remove-Item -Force $bundleZip }
Compress-Archive -Path "dist\InstallerBundle\*" -DestinationPath $bundleZip -CompressionLevel Optimal

$stubExe = "dist\stub_temp.exe"
if (Test-Path $stubExe) { Remove-Item -Force $stubExe }
& $csc /target:winexe /optimize+ /platform:x64 /win32icon:"src\DiscordNative.Installer\app.ico" /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /out:$stubExe "src\DiscordNative.Installer\Bootstrapper.cs"

$finalSetupExe = "dist\Diswin-Setup-v$version-win-x64.exe"
if (Test-Path $finalSetupExe) { Remove-Item -Force $finalSetupExe }

$stubBytes = [System.IO.File]::ReadAllBytes((Resolve-Path $stubExe).Path)
$zipInfo = Get-Item $bundleZip
$zipLength = [int64]$zipInfo.Length
$lenBytes = [System.BitConverter]::GetBytes($zipLength)
$magicBytes = [System.Text.Encoding]::ASCII.GetBytes("DISWINSFX")

$outStream = [System.IO.File]::OpenWrite((Join-Path (Get-Location).Path $finalSetupExe))
try {
    $outStream.Write($stubBytes, 0, $stubBytes.Length)
    $inZip = [System.IO.File]::OpenRead($zipInfo.FullName)
    try {
        $inZip.CopyTo($outStream)
    } finally {
        $inZip.Close()
    }
    $outStream.Write($lenBytes, 0, 8)
    $outStream.Write($magicBytes, 0, 9)
} finally {
    $outStream.Close()
}

Remove-Item -Force $bundleZip -ErrorAction SilentlyContinue
Remove-Item -Force $stubExe -ErrorAction SilentlyContinue
Remove-Item -Force "dist\Diswin-Setup-v$version-win-x64.zip" -ErrorAction SilentlyContinue

Write-Host "Done! Artifacts in dist\:"
Get-ChildItem "dist\Diswin*v$version*" | Select-Object Name, @{Name="SizeMB";Expression={[math]::Round($_.Length/1MB, 2)}}, LastWriteTime
