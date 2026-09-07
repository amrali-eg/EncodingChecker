[CmdletBinding()]
param(
    [string] $Path = (Join-Path $PSScriptRoot 'DEFECT-BACKLOG.md')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedPath = (Resolve-Path -LiteralPath $Path).Path
$content = [IO.File]::ReadAllText($resolvedPath)
$errors = [Collections.Generic.List[string]]::new()

$countMarker = [regex]::Match(
    $content,
    '<!-- backlog-counts (?<pairs>[^>]+) -->')

if (-not $countMarker.Success) {
    throw 'The backlog-counts marker is missing.'
}

$expected = @{}

foreach ($token in $countMarker.Groups['pairs'].Value.Split(
    ' ',
    [StringSplitOptions]::RemoveEmptyEntries)) {
    $parts = $token.Split('=', 2)
    $value = 0

    if ($parts.Length -ne 2 -or
        -not [int]::TryParse($parts[1], [ref] $value) -or
        $value -lt 0) {
        throw "Invalid count token '$token'."
    }

    $expected[$parts[0]] = $value
}

$startMarker = '<!-- backlog-ledger:start -->'
$endMarker = '<!-- backlog-ledger:end -->'
$start = $content.IndexOf($startMarker, [StringComparison]::Ordinal)
$end = $content.IndexOf($endMarker, [StringComparison]::Ordinal)

if ($start -lt 0 -or $end -le $start) {
    throw 'The canonical ledger markers are missing or out of order.'
}

$ledger = $content.Substring(
    $start + $startMarker.Length,
    $end - ($start + $startMarker.Length))

$rows = foreach ($line in [regex]::Split($ledger, '\r?\n')) {
    if ($line -notmatch '^\|\s*((?:EC|CX|BL)-\d{2})\s*\|') {
        continue
    }

    $cells = @(
        $line.Trim().Trim('|').Split('|') |
            ForEach-Object { $_.Trim() }
    )

    if ($cells.Count -ne 6) {
        $errors.Add("$($Matches[1]) has $($cells.Count) cells; expected 6.")
        continue
    }

    [pscustomobject] @{
        Id      = $cells[0]
        Finding = $cells[1]
        Status  = $cells[2]
        Impact  = $cells[3]
        Reach   = $cells[4]
        Details = $cells[5]
    }
}

# The ledger writes an em-dash where a finding carries no score. Built from its code
# point rather than typed: Windows PowerShell reads a BOM-less .ps1 as the system ANSI
# codepage, so a literal here parses as three bytes and breaks the string containing it.
$EmDash = [string][char]0x2014

$knownStatuses = @(
    'Fixed',
    'Open',
    'Not reproduced',
    'Withdrawn',
    'Not a defect',
    'Decision'
)

foreach ($duplicate in $rows | Group-Object Id | Where-Object Count -gt 1) {
    $errors.Add("Duplicate finding ID: $($duplicate.Name).")
}

foreach ($row in $rows) {
    if ($row.Status -notin $knownStatuses) {
        $errors.Add("$($row.Id) has unknown status '$($row.Status)'.")
    }

    if ($row.Status -eq 'Open' -and
        ($row.Impact -in @('', '-', $EmDash) -or $row.Reach -in @('', '-', $EmDash))) {
        $errors.Add("$($row.Id) is open but lacks impact or reach.")
    }

    $expectedLink = "[$($row.Id)](#$($row.Id.ToLowerInvariant()))"

    if ($row.Details -ne $expectedLink) {
        $errors.Add("$($row.Id) must link to its exact detail heading.")
    }

    $headingPattern = '(?m)^###\s+' + [regex]::Escape($row.Id) + '\s*$'

    if (-not [regex]::IsMatch($content, $headingPattern)) {
        $errors.Add("$($row.Id) has no exact detail heading.")
    }
}

$actual = @{
    total            = @($rows).Count
    fixed            = @($rows | Where-Object Status -eq 'Fixed').Count
    open             = @($rows | Where-Object Status -eq 'Open').Count
    'not-reproduced' = @($rows | Where-Object Status -eq 'Not reproduced').Count
    withdrawn        = @($rows | Where-Object Status -eq 'Withdrawn').Count
    'not-a-defect'   = @($rows | Where-Object Status -eq 'Not a defect').Count
    decision         = @($rows | Where-Object Status -eq 'Decision').Count
}

foreach ($key in $expected.Keys) {
    if (-not $actual.ContainsKey($key)) {
        $errors.Add("The count marker has unknown key '$key'.")
    }
}

foreach ($key in $actual.Keys) {
    if (-not $expected.ContainsKey($key)) {
        $errors.Add("The count marker has no '$key' value.")
        continue
    }

    if ($actual[$key] -ne $expected[$key]) {
        $errors.Add(
            "Count mismatch for ${key}: header $($expected[$key]), ledger $($actual[$key]).")
    }
}

$summary = "**Derived count: $($actual.total) findings $EmDash $($actual.fixed) fixed, " +
    "$($actual.open) open, $($actual['not-reproduced']) not reproduced, " +
    "$($actual.withdrawn) withdrawn, $($actual['not-a-defect']) not a defect, " +
    "and $($actual.decision) design decision.**"
$normalizedContent = [regex]::Replace($content, '\s+', ' ')

if (-not $normalizedContent.Contains($summary)) {
    $errors.Add('The human-readable derived count does not match the ledger.')
}

Write-Output "Defect backlog: $($actual.total) finding(s)"

foreach ($status in $knownStatuses) {
    $count = @($rows | Where-Object Status -eq $status).Count
    Write-Output ("  {0,-16} {1}" -f ($status + ':'), $count)
}

if ($errors.Count -gt 0) {
    foreach ($message in $errors) {
        Write-Error $message
    }

    exit 1
}

Write-Output "PASS: counts, IDs, scores, and detail links reconcile."
