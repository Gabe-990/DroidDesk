[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\DroidDesk\DroidDesk.csproj'
$output = Join-Path $root 'artifacts\publish'
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
dotnet publish $project -c $Configuration -r $RuntimeIdentifier --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $output
Write-Host "Published DroidDesk to $output"
