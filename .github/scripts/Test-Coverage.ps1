param(
    [Parameter(Mandatory = $true)]
    [string]$CoverageRoot,
    [double]$MinimumLineCoverage = 38
)

$ErrorActionPreference = "Stop"
$reports = @(Get-ChildItem $CoverageRoot -Filter "coverage.cobertura.xml" -Recurse)
if ($reports.Count -eq 0) { throw "No Cobertura reports were found under '$CoverageRoot'." }

$linesCovered = 0L
$linesValid = 0L
foreach ($report in $reports) {
    [xml]$coverage = Get-Content $report.FullName
    $linesCovered += [long]$coverage.coverage.'lines-covered'
    $linesValid += [long]$coverage.coverage.'lines-valid'
}

if ($linesValid -eq 0) { throw "Coverage reports did not contain any measurable lines." }
$percentage = 100.0 * $linesCovered / $linesValid
Write-Host ("Line coverage: {0:N2}% ({1}/{2})" -f $percentage, $linesCovered, $linesValid)
if ($percentage -lt $MinimumLineCoverage) {
    throw ("Line coverage {0:N2}% is below the required {1:N2}%." -f $percentage, $MinimumLineCoverage)
}
