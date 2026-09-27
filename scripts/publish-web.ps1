param(
    [ValidatePattern('^(\./|/([A-Za-z0-9_.~-]+/)*)$')]
    [string]$BasePath = './'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$output = Join-Path $projectRoot 'dist/web'
dotnet publish (Join-Path $projectRoot 'src/RushHour.Web/RushHour.Web.csproj') -c Release -o $output
if ($LASTEXITCODE -ne 0) { throw 'Web publish failed.' }
$index = Join-Path $output 'wwwroot/index.html'
$html = [IO.File]::ReadAllText($index).Replace('<base href="./" />', "<base href=`"$BasePath`" />")
[IO.File]::WriteAllText($index, $html, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $output 'wwwroot/.nojekyll'), '')
Write-Host "Published static site: $output/wwwroot (base: $BasePath)"
