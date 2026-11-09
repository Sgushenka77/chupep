param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root "artifacts\LoadLens-$Runtime"
$selfContainedValue = if ($SelfContained) { 'true' } else { 'false' }

Write-Host "Публикация LoadLens ($Runtime, self-contained=$selfContainedValue)..." -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src\LoadLens.App\LoadLens.App.csproj') `
    -c Release `
    -r $Runtime `
    --self-contained $selfContainedValue `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $out

Write-Host "Готово: $out" -ForegroundColor Green
