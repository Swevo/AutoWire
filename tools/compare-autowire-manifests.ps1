[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Baseline,

    [Parameter(Mandatory = $true)]
    [string]$Candidate,

    [Parameter(Mandatory = $false)]
    [string]$ReportPath = ".\autowire-manifest-diff.md"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Baseline)) { throw "Baseline file not found: $Baseline" }
if (-not (Test-Path -LiteralPath $Candidate)) { throw "Candidate file not found: $Candidate" }

$baseLines = Get-Content -LiteralPath $Baseline | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$candLines = Get-Content -LiteralPath $Candidate | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

$baseSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$candSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

foreach ($line in $baseLines) { $baseSet.Add($line) | Out-Null }
foreach ($line in $candLines) { $candSet.Add($line) | Out-Null }

$removed = @()
$added = @()

foreach ($line in $baseSet) {
    if (-not $candSet.Contains($line)) { $removed += $line }
}
foreach ($line in $candSet) {
    if (-not $baseSet.Contains($line)) { $added += $line }
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# AutoWire Manifest Diff Report") | Out-Null
$lines.Add("") | Out-Null
$lines.Add("**Baseline:** $Baseline  ") | Out-Null
$lines.Add("**Candidate:** $Candidate  ") | Out-Null
$lines.Add("**Baseline count:** $($baseSet.Count)  ") | Out-Null
$lines.Add("**Candidate count:** $($candSet.Count)  ") | Out-Null
$lines.Add("**Added:** $($added.Count)  ") | Out-Null
$lines.Add("**Removed:** $($removed.Count)  ") | Out-Null
$lines.Add("") | Out-Null

$lines.Add("## Added registrations") | Out-Null
$lines.Add("") | Out-Null
if ($added.Count -eq 0) {
    $lines.Add("_None._") | Out-Null
}
else {
    $lines.Add('```text') | Out-Null
    foreach ($line in ($added | Sort-Object)) { $lines.Add($line) | Out-Null }
    $lines.Add('```') | Out-Null
}
$lines.Add("") | Out-Null

$lines.Add("## Removed registrations") | Out-Null
$lines.Add("") | Out-Null
if ($removed.Count -eq 0) {
    $lines.Add("_None._") | Out-Null
}
else {
    $lines.Add('```text') | Out-Null
    foreach ($line in ($removed | Sort-Object)) { $lines.Add($line) | Out-Null }
    $lines.Add('```') | Out-Null
}

[System.IO.File]::WriteAllLines($ReportPath, $lines)
Write-Host "Manifest diff report written to $ReportPath"
