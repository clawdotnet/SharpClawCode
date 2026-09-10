param(
    [string]$PackageVersion = "0.1.0-preview.1",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$scratchRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("sharpclaw-package-smoke-" + [Guid]::NewGuid().ToString("N"))
$packageOutput = Join-Path $scratchRoot "packages"
$toolPath = Join-Path $scratchRoot "tool"
$consumerPath = Join-Path $scratchRoot "consumer"
$nugetConfigPath = Join-Path $scratchRoot "NuGet.Config"

try {
    New-Item -ItemType Directory -Path $packageOutput -Force | Out-Null
    & dotnet pack (Join-Path $repositoryRoot "SharpClawCode.Packages.slnf") --configuration $Configuration --output $packageOutput -p:PackageVersion=$PackageVersion
    if ($LASTEXITCODE -ne 0) { throw "Package creation failed." }

    $packages = @(Get-ChildItem $packageOutput -Filter "*.nupkg" | Where-Object { $_.Name -notlike "*.symbols.nupkg" })
    $unexpected = @($packages | Where-Object { $_.BaseName -notlike "SharpClaw.Code*" })
    if ($packages.Count -ne 21) { throw "Expected 21 production packages, found $($packages.Count)." }
    if ($unexpected.Count -gt 0) { throw "Unexpected packages: $($unexpected.Name -join ', ')." }

    & dotnet new console --framework net10.0 --output $consumerPath --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Could not create package smoke consumer." }
    & dotnet add (Join-Path $consumerPath "consumer.csproj") package SharpClaw.Code --version $PackageVersion --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Could not add the aggregate SDK package." }

    [System.IO.File]::WriteAllText(
        $nugetConfigPath,
        '<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear /></packageSources></configuration>')
    & dotnet nuget add source $packageOutput --name sharpclaw-local --configfile $nugetConfigPath
    if ($LASTEXITCODE -ne 0) { throw "Could not configure the local package source." }
    & dotnet nuget add source "https://api.nuget.org/v3/index.json" --name nuget.org --configfile $nugetConfigPath
    if ($LASTEXITCODE -ne 0) { throw "Could not configure the NuGet.org package source." }

    & dotnet restore (Join-Path $consumerPath "consumer.csproj") --configfile $nugetConfigPath
    if ($LASTEXITCODE -ne 0) { throw "Could not restore the aggregate SDK package." }
    & dotnet build (Join-Path $consumerPath "consumer.csproj") --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "The aggregate SDK package failed to build in a clean consumer." }

    & dotnet tool install --tool-path $toolPath SharpClaw.Code.Cli --version $PackageVersion --add-source $packageOutput --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { throw "The CLI tool package failed to install." }
    $toolExecutable = if ($IsWindows) { Join-Path $toolPath "sharpclaw.exe" } else { Join-Path $toolPath "sharpclaw" }
    & $toolExecutable version
    if ($LASTEXITCODE -ne 0) { throw "The installed CLI tool failed its version smoke test." }
}
finally {
    if (Test-Path $scratchRoot) {
        Remove-Item $scratchRoot -Recurse -Force
    }
}
