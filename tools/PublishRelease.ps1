param(
    [string]$ClientOutput = (Join-Path $PSScriptRoot '..\publish'),
    [string]$ServerOutput = (Join-Path $PSScriptRoot '..\publish-server')
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$clientDirectory = [IO.Path]::GetFullPath($ClientOutput)
$serverDirectory = [IO.Path]::GetFullPath($ServerOutput)

dotnet publish (Join-Path $projectRoot 'Dust.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $clientDirectory
if ($LASTEXITCODE -ne 0) { throw 'Dust client publishing failed.' }

dotnet publish (Join-Path $projectRoot 'OnlineServer\Dust.OnlineServer.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $serverDirectory
if ($LASTEXITCODE -ne 0) { throw 'Dust Online Server publishing failed.' }

Copy-Item -LiteralPath (Join-Path $projectRoot 'ONLINE.md') `
    -Destination (Join-Path $serverDirectory 'ONLINE.md') -Force

Write-Host "Client: $clientDirectory\Dust.exe"
Write-Host "Server: $serverDirectory\Dust.OnlineServer.exe"
