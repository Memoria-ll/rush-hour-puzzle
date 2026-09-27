param([string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $projectRoot "dist\$Runtime"
dotnet publish (Join-Path $projectRoot 'src\RushHour.Desktop\RushHour.Desktop.csproj') -c Release -r $Runtime --self-contained true -o $publishDir -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$portableExe = Join-Path $projectRoot 'dist\RushHourGarage.exe'
Copy-Item -LiteralPath (Join-Path $publishDir 'RushHourGarage.exe') -Destination $portableExe -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\quick-start.txt') -Destination (Join-Path $projectRoot 'dist\README.txt') -Force
Write-Host "Ready: $portableExe"
