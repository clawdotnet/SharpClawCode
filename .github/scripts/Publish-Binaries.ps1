param(
    [Parameter(Mandatory)][ValidateSet('win-x64','win-arm64','linux-x64','linux-arm64','osx-x64','osx-arm64')][string]$Runtime,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')][string]$Version,
    [string]$OutputDirectory = 'artifacts/binaries'
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('sharpclaw-publish-' + [Guid]::NewGuid().ToString('N'))
try {
    $publish = Join-Path $scratch 'publish'
    $stage = Join-Path $scratch 'stage'
    New-Item -ItemType Directory -Path $stage,$outputRoot -Force | Out-Null
    & dotnet publish (Join-Path $repositoryRoot 'src/SharpClaw.Code.Cli/SharpClaw.Code.Cli.csproj') --configuration Release --runtime $Runtime --self-contained true --output $publish --disable-build-servers `
        -p:PackAsTool=false -p:PublishSingleFile=true -p:PublishTrimmed=false -p:PublishAot=false `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true `
        "-p:Version=$Version" "-p:PackageVersion=$Version" "-p:InformationalVersion=$Version" -p:IncludeSourceRevisionInInformationalVersion=false
    if ($LASTEXITCODE -ne 0) { throw "Binary publish failed: $Runtime" }
    $sourceExecutable = if ($Runtime.StartsWith('win-')) { 'SharpClaw.Code.Cli.exe' } else { 'SharpClaw.Code.Cli' }
    $executable = if ($Runtime.StartsWith('win-')) { 'sharpclaw.exe' } else { 'sharpclaw' }
    Copy-Item (Join-Path $publish $sourceExecutable) (Join-Path $stage $executable)
    Copy-Item (Join-Path $repositoryRoot 'LICENSE') $stage
    Copy-Item (Join-Path $repositoryRoot 'README.md') $stage
    $stem = "sharpclaw-$Version-$Runtime"
    if ($Runtime.StartsWith('win-')) {
        $archive = Join-Path $outputRoot "$stem.zip"
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
    } else {
        & chmod +x (Join-Path $stage $executable)
        if ($LASTEXITCODE -ne 0) { throw 'Could not preserve executable permission.' }
        $archive = Join-Path $outputRoot "$stem.tar.gz"
        & tar -czf $archive -C $stage $executable LICENSE README.md
        if ($LASTEXITCODE -ne 0) { throw 'Could not create binary archive.' }
    }
    $checksum = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText("$archive.sha256", "$checksum  $([System.IO.Path]::GetFileName($archive))`n")
    Write-Host "Created $archive"
} finally {
    if (Test-Path $scratch) { Remove-Item $scratch -Recurse -Force }
}
