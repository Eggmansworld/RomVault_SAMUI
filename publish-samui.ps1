# Builds a release of SAM-UI (Eggman Edition) and installs it to C:\Utilities\RVWorld-SAMUI.
#   - self-contained win-x64 single exe (no .NET install needed) + Native\ (libopenmpt) + Tunes\
#   - zip goes to .\artifacts\SAM-UI_v<version>_win-x64.zip (the file to attach to a GitHub release)
#   - the zip is then extracted over C:\Utilities\RVWorld-SAMUI (your settings file and any
#     tunes you added are kept)
# Run from PowerShell:  .\publish-samui.ps1
$ErrorActionPreference = 'Stop'
$root    = $PSScriptRoot
$proj    = Join-Path $root 'SAM-UI-Avalonia\SAM-UI-Avalonia.csproj'
$install = 'C:\Utilities\RVWorld-SAMUI'

[xml]$x  = Get-Content $proj
$version = ($x.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
$out     = Join-Path $root "artifacts\publish"
$zip     = Join-Path $root "artifacts\SAM-UI_v${version}_win-x64.zip"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force (Split-Path $zip) | Out-Null

Write-Host "Publishing SAM-UI v$version ..."
dotnet publish $proj -c Release -r win-x64 --self-contained true -o $out -nologo `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true -p:EnableCompressionInSingleFile=true -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# native debug symbols shipped by the SkiaSharp/HarfBuzz packages - not needed
Get-ChildItem $out -Filter *.pdb -Recurse | Remove-Item -Force
if (-not (Test-Path (Join-Path $out 'Native\libopenmpt.dll'))) { throw "Native\libopenmpt.dll missing from publish output" }

# no settings file in a release: every user starts on the built-in defaults
Remove-Item (Join-Path $out 'SAM-UI.settings.json') -ErrorAction SilentlyContinue

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
Write-Host "Zip: $zip"

if (Get-Process SAM-UI-Avalonia -ErrorAction SilentlyContinue) { throw "SAM-UI is running - close it and run this again to install." }
New-Item -ItemType Directory -Force $install | Out-Null
Get-ChildItem $install -Filter *.pdb | Remove-Item -Force   # leftovers from older builds
Expand-Archive -Path $zip -DestinationPath $install -Force
Write-Host "Installed to $install"
