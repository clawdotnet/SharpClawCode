param(
    [string]$Branch = "main"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$repository = (& gh repo view --json nameWithOwner --jq .nameWithOwner).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repository)) {
    throw "Could not resolve the GitHub repository. Authenticate gh and run this script from the checkout."
}

$branchProtectionPath = Join-Path $repositoryRoot ".github/branch-protection.json"
$securitySettingsPath = Join-Path $repositoryRoot ".github/security-settings.json"

& gh api --method PUT "repos/$repository/branches/$Branch/protection" --input $branchProtectionPath --silent
if ($LASTEXITCODE -ne 0) { throw "Could not apply branch protection to '$Branch'." }

& gh api --method PATCH "repos/$repository" --input $securitySettingsPath --silent
if ($LASTEXITCODE -ne 0) { throw "Could not apply repository security settings." }

& gh api --method PUT "repos/$repository/vulnerability-alerts" --silent
if ($LASTEXITCODE -ne 0) { throw "Could not enable vulnerability alerts." }

& gh api --method PUT "repos/$repository/automated-security-fixes" --silent
if ($LASTEXITCODE -ne 0) { throw "Could not enable Dependabot security updates." }

Write-Host "Applied branch protection and repository security settings to $repository ($Branch)."
