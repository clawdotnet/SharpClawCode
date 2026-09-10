param(
    [string]$Target = "SharpClawCode.sln"
)

$ErrorActionPreference = "Stop"
$reportJson = (& dotnet list $Target package --vulnerable --include-transitive --format json) -join [Environment]::NewLine
if ($LASTEXITCODE -ne 0) { throw "NuGet vulnerability inspection failed." }

$report = $reportJson | ConvertFrom-Json
$vulnerablePackages = @(
    foreach ($project in @($report.projects)) {
        foreach ($framework in @($project.frameworks)) {
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                if ($null -ne $package -and @($package.vulnerabilities).Count -gt 0) {
                    [PSCustomObject]@{
                        Project = $project.path
                        Framework = $framework.framework
                        Package = $package.id
                        ResolvedVersion = $package.resolvedVersion
                        Vulnerabilities = @($package.vulnerabilities)
                    }
                }
            }
        }
    }
)

if ($vulnerablePackages.Count -gt 0) {
    $vulnerablePackages | ConvertTo-Json -Depth 8 | Write-Error
    throw "NuGet reported $($vulnerablePackages.Count) vulnerable package occurrence(s)."
}

Write-Host "NuGet vulnerability audit passed for $($report.projects.Count) projects."
