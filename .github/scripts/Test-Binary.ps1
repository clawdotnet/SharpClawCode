param(
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][ValidateSet('win-x64','win-arm64','linux-x64','linux-arm64','osx-x64','osx-arm64')][string]$Runtime,
    [Parameter(Mandatory)][string]$Version
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
$platform = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
if ("$platform-$architecture" -ne $Runtime) { throw "Smoke must execute on a matching runner: expected $Runtime, got $platform-$architecture." }
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('sharpclaw-binary-smoke-' + [Guid]::NewGuid().ToString('N'))
$previousBinary = $env:SHARPCLAW_TEST_BINARY
try {
    $extract = Join-Path $scratch 'extract'
    $workspace = Join-Path $scratch 'workspace'
    New-Item -ItemType Directory -Path $extract,$workspace -Force | Out-Null
    $archivePath = (Resolve-Path $Archive).Path
    $checksumPath = "$archivePath.sha256"
    if (!(Test-Path $checksumPath)) { throw 'Binary archive checksum is missing.' }
    $expected = (Get-Content $checksumPath -Raw).Split(' ')[0]
    if ((Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Binary archive checksum mismatch.' }
    if ($IsWindows) { Expand-Archive $archivePath $extract }
    else {
        & tar -xzf $archivePath -C $extract
        if ($LASTEXITCODE -ne 0) { throw 'Archive extraction failed.' }
    }
    $executableName = if ($IsWindows) { 'sharpclaw.exe' } else { 'sharpclaw' }
    $binary = Join-Path $extract $executableName
    $files = @(Get-ChildItem $extract -File | Select-Object -ExpandProperty Name | Sort-Object)
    if (($files -join ',') -ne ((@($executableName,'LICENSE','README.md') | Sort-Object) -join ',')) { throw 'Archive must contain exactly executable, LICENSE, and README.md.' }
    # An empty PATH and isolated runtime roots prove the native bundle starts without resolving an ambient dotnet host.
    $start = [System.Diagnostics.ProcessStartInfo]::new($binary)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.WorkingDirectory = $workspace
    $start.ArgumentList.Add('--output-format'); $start.ArgumentList.Add('json'); $start.ArgumentList.Add('version')
    $start.Environment['PATH'] = ''
    foreach ($key in @('DOTNET_ROOT','DOTNET_ROOT_X64','DOTNET_ROOT_ARM64')) { $start.Environment[$key] = (Join-Path $scratch 'no-dotnet') }
    $start.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $process = [System.Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(30000)) { $process.Kill($true); throw 'Self-contained startup timed out.' }
    $text = $stdout.GetAwaiter().GetResult(); $errorText = $stderr.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { throw "No-runtime version failed: $errorText" }
    $process.Dispose()
    $versionResult = $text | ConvertFrom-Json
    if ($versionResult.succeeded -ne $true -or $versionResult.data.InformationalVersion -ne $Version) { throw "Binary did not report release identity $Version`: $text" }
    [System.IO.File]::WriteAllText((Join-Path $workspace 'note.txt'), 'offline binary SQLite index smoke')
    & $binary --cwd $workspace --output-format json verify --permission-mode dangerFullAccess
    if ($LASTEXITCODE -ne 0) { throw 'Non-.NET verification skip failed.' }
    & $binary --cwd $workspace --output-format json index refresh
    if ($LASTEXITCODE -ne 0) { throw 'SQLite index refresh failed.' }
    & $binary --cwd $workspace --output-format json index stats
    if ($LASTEXITCODE -ne 0) { throw 'SQLite index stats failed.' }
    # Real SDK client tests launch the relocated native binary, exercise both default and elevated calls,
    # and prove bundled SQLite, Roslyn BuildHost, semantic rename/undo, and actual build/TRX test results.
    $env:SHARPCLAW_TEST_BINARY = $binary
    & dotnet test (Join-Path $repositoryRoot 'tests/SharpClaw.Code.IntegrationTests/SharpClaw.Code.IntegrationTests.csproj') --configuration Release --filter 'FullyQualifiedName~InboundMcpStdioTests' --disable-build-servers
    if ($LASTEXITCODE -ne 0) { throw 'Relocated binary MCP/semantic/build/test smoke failed.' }
    Write-Host "Binary smoke passed: $Runtime $Version"
} finally {
    $env:SHARPCLAW_TEST_BINARY = $previousBinary
    if (Test-Path $scratch) { Remove-Item $scratch -Recurse -Force }
}
