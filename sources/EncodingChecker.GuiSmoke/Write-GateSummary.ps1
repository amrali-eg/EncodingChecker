param(
    [Parameter(Mandatory = $true)][string] $OutputDirectory,
    [Parameter(Mandatory = $true)][int] $SuiteExitCode,
    [string] $SummaryPath = $env:GITHUB_STEP_SUMMARY
)

$ErrorActionPreference = 'Stop'
$passed = $false
$message = 'GUI smoke test failed: the run did not produce a readable result.'
try {
    $report = Get-Content -LiteralPath (Join-Path $OutputDirectory 'gui-smoke-report.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($SuiteExitCode -eq 0 -and $report.Outcome -eq 'Passed' -and @($report.Phases).Count -gt 0) {
        $passed = $true
        $message = 'GUI smoke test passed: all selected checks completed.'
    } elseif ($SuiteExitCode -eq 2 -and $report.Outcome -eq 'Inconclusive') {
        $message = 'GUI smoke test inconclusive: the desktop or build could not be tested. Approval remains blocked; read the evidence before retrying.'
    } elseif ($SuiteExitCode -eq 1 -and $report.Outcome -eq 'Failed') {
        $message = 'GUI smoke test failed: a check, cleanup, or evidence collection failed. Read the report for the cause.'
    } else {
        $message = 'GUI smoke test failed: the exit code and report do not establish a completed passing run.'
    }
} catch {
    if ($SuiteExitCode -eq 2) {
        $message = 'GUI smoke test inconclusive: it could not start and no evidence report was available. Approval remains blocked; read the suite output.'
    } else {
        $message = 'GUI smoke test failed: no usable report was available. Read the suite output for the setup or reporting error.'
    }
}

Write-Output $message
if ($SummaryPath) {
    Add-Content -LiteralPath $SummaryPath -Value $message -Encoding UTF8
}
if (-not $passed) {
    Write-Output ('::error::' + $message)
    exit 1
}
exit 0
