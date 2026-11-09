param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host 'Запуск LoadLens...' -ForegroundColor Cyan
dotnet run --project (Join-Path $root 'src\LoadLens.App\LoadLens.App.csproj') -c Release
