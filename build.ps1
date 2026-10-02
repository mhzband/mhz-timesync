param([ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet build src/Mhz.TimeSync.App -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet run --project tests/Mhz.TimeSync.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    dotnet publish src/Mhz.TimeSync.App -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "artifacts/$Runtime"
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item README.md "artifacts/$Runtime/README.md"
    Compress-Archive -Path "artifacts/$Runtime/*" -DestinationPath "artifacts/MHZ-TimeSync-$Runtime.zip" -Force
} finally { Pop-Location }
