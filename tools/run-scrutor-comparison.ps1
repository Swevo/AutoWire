[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Configuration = "Release",

    [Parameter(Mandatory = $false)]
    [string]$BenchmarkProject = ".\benchmarks\AutoWire.Benchmarks\AutoWire.Benchmarks.csproj",

    [Parameter(Mandatory = $false)]
    [string]$OutputReport = ".\benchmarks\AutoWire.Benchmarks\BenchmarkDotNet.Artifacts\scrutor-vs-autowire-summary.md",

    [switch]$SkipBenchmarkRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $BenchmarkProject)) {
    throw "Benchmark project not found: $BenchmarkProject"
}

if (-not $SkipBenchmarkRun) {
    Write-Host "Running benchmark suite..."
    dotnet run -c $Configuration --project $BenchmarkProject -- --exporters json markdown
}

$artifactCandidates = @(
    (Join-Path (Split-Path -Parent $BenchmarkProject) "BenchmarkDotNet.Artifacts\results"),
    (Join-Path (Get-Location) "BenchmarkDotNet.Artifacts\results")
)
$artifactRoot = $artifactCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($artifactRoot)) {
    throw "Benchmark artifacts not found in expected locations: $($artifactCandidates -join ', ')"
}

$markdownResults = @(Get-ChildItem -Path $artifactRoot -File -Filter "*.md" | Sort-Object LastWriteTime -Descending)
if ($markdownResults.Count -eq 0) {
    throw "No markdown benchmark result files found under: $artifactRoot"
}

$latest = $markdownResults[0]
$content = Get-Content -LiteralPath $latest.FullName

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Scrutor vs AutoWire Benchmark Summary") | Out-Null
$lines.Add("") | Out-Null
$lines.Add("**Generated from:** $($latest.FullName)") | Out-Null
$lines.Add("") | Out-Null
foreach ($line in $content) {
    $lines.Add([string]$line) | Out-Null
}

[System.IO.Directory]::CreateDirectory((Split-Path -Parent $OutputReport)) | Out-Null
[System.IO.File]::WriteAllLines($OutputReport, $lines)
Write-Host "Benchmark summary written to $OutputReport"

Write-Host ""
Write-Host "Tip: for AOT/publish-size comparison, run:"
Write-Host "  dotnet publish .\samples\Api\AutoWire.Sample.Api.csproj -c Release -r win-x64 -p:PublishAot=true"
