[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Path = ".",

    [Parameter(Mandatory = $false)]
    [string]$ReportPath = ".\autowire-scrutor-migration-report.md",

    [switch]$WhatIf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-ScanBlocks {
    param([string]$Text)

    $blocks = New-Object System.Collections.Generic.List[object]
    $needle = "Scan("
    $start = 0

    while ($true) {
        $idx = $Text.IndexOf($needle, $start, [System.StringComparison]::Ordinal)
        if ($idx -lt 0) { break }

        $servicesIdx = $Text.LastIndexOf("services.", $idx, [System.StringComparison]::Ordinal)
        if ($servicesIdx -lt 0) {
            $start = $idx + $needle.Length
            continue
        }

        $parenStart = $Text.IndexOf("(", $idx, [System.StringComparison]::Ordinal)
        if ($parenStart -lt 0) { break }

        $depth = 0
        $endIdx = -1
        for ($i = $parenStart; $i -lt $Text.Length; $i++) {
            $ch = $Text[$i]
            if ($ch -eq '(') { $depth++ }
            elseif ($ch -eq ')') {
                $depth--
                if ($depth -eq 0) {
                    $semi = $Text.IndexOf(";", $i, [System.StringComparison]::Ordinal)
                    if ($semi -gt $i) { $endIdx = $semi; break }
                }
            }
        }

        if ($endIdx -lt 0) {
            $start = $idx + $needle.Length
            continue
        }

        $blockText = $Text.Substring($servicesIdx, $endIdx - $servicesIdx + 1)
        $blocks.Add([PSCustomObject]@{
            Start = $servicesIdx
            End = $endIdx
            Text = $blockText
        }) | Out-Null

        $start = $endIdx + 1
    }

    return $blocks
}

function Get-LifetimeFromScan {
    param([string]$ScanText)

    if ($ScanText -match '\.WithScopedLifetime\s*\(') { return "Scoped" }
    if ($ScanText -match '\.WithSingletonLifetime\s*\(') { return "Singleton" }
    if ($ScanText -match '\.WithTransientLifetime\s*\(') { return "Transient" }
    return $null
}

function Get-MarkerTypeFromScan {
    param([string]$ScanText)

    if ($ScanText -match '\.FromAssemblyOf<(?<marker>[^>]+)>\s*\(') {
        return $Matches["marker"].Trim()
    }

    return $null
}

function Get-AssemblySourceHint {
    param([string]$ScanText)

    if ($ScanText -match '\.FromAssemblyOf<(?<marker>[^>]+)>\s*\(') {
        return "FromAssemblyOf<$($Matches['marker'].Trim())>"
    }
    if ($ScanText -match '\.FromAssembliesOf<(?<markers>[^>]+)>\s*\(') {
        return "FromAssembliesOf<$($Matches['markers'].Trim())>"
    }
    if ($ScanText -match '\.FromCallingAssembly\s*\(') {
        return "FromCallingAssembly()"
    }
    return "FromAssembly*(unknown)"
}

function Get-ScanClassChains {
    param([string]$ScanText)

    $chains = New-Object System.Collections.Generic.List[object]
    $matches = [System.Text.RegularExpressions.Regex]::Matches(
        $ScanText,
        '\.AddClasses\s*\((?<filter>.*?)\)\s*(?<body>.*?)(?=\.AddClasses\s*\(|\)\s*;|$)',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)

    foreach ($m in $matches) {
        $filter = $m.Groups["filter"].Value.Trim()
        $body = $m.Groups["body"].Value

        $asMode = "Unknown"
        $asType = $null
        if ($body -match '\.AsImplementedInterfaces\s*\(') {
            $asMode = "AsImplementedInterfaces"
        }
        elseif ($body -match '\.AsSelf\s*\(') {
            $asMode = "AsSelf"
        }
        elseif ($body -match '\.As<(?<asType>[^>]+)>\s*\(') {
            $asMode = "AsGeneric"
            $asType = $Matches["asType"].Trim()
        }
        elseif ($body -match '\.As\s*\(\s*typeof\((?<asType>[^)]+)\)\s*\)') {
            $asMode = "AsTypeof"
            $asType = $Matches["asType"].Trim()
        }

        $lifetime = $null
        if ($body -match '\.WithScopedLifetime\s*\(') { $lifetime = "Scoped" }
        elseif ($body -match '\.WithSingletonLifetime\s*\(') { $lifetime = "Singleton" }
        elseif ($body -match '\.WithTransientLifetime\s*\(') { $lifetime = "Transient" }

        $chains.Add([PSCustomObject]@{
            Filter = $filter
            AsMode = $asMode
            AsType = $asType
            Lifetime = $lifetime
            Body = $body.Trim()
        }) | Out-Null
    }

    return $chains
}

function Convert-ScanBlock {
    param([string]$ScanText)

    if (-not ($ScanText -match '\.AddClasses\s*\(')) {
        return [PSCustomObject]@{
            Replacement = $null
            Reason = "No AddClasses() chains were found in this Scan() block."
        }
    }

    $chains = @(Get-ScanClassChains -ScanText $ScanText)
    if ($chains.Count -eq 0) {
        return [PSCustomObject]@{
            Replacement = $null
            Reason = "Could not parse AddClasses() chains in this Scan() block."
        }
    }

    $unsupported = @()
    foreach ($chain in $chains) {
        if ([string]::IsNullOrWhiteSpace($chain.Lifetime)) {
            $unsupported += "Missing explicit With*Lifetime() for one AddClasses() chain."
        }
        if ($chain.AsMode -eq "Unknown") {
            $unsupported += "Unsupported As*() mapping in one AddClasses() chain."
        }
    }
    if ($unsupported.Count -gt 0) {
        return [PSCustomObject]@{
            Replacement = $null
            Reason = ($unsupported | Select-Object -Unique) -join " "
        }
    }

    $sourceHint = Get-AssemblySourceHint -ScanText $ScanText
    $marker = Get-MarkerTypeFromScan -ScanText $ScanText
    $markerType = if ([string]::IsNullOrWhiteSpace($marker)) { "YourMarkerType" } else { $marker }

    $chainLines = New-Object System.Collections.Generic.List[string]
    $chainIndex = 1
    foreach ($chain in $chains) {
        $asDisplay = switch ($chain.AsMode) {
            "AsImplementedInterfaces" { "AsImplementedInterfaces" }
            "AsSelf" { "AsSelf" }
            "AsGeneric" { "As<$($chain.AsType)>" }
            "AsTypeof" { "As(typeof($($chain.AsType)))" }
            default { $chain.AsMode }
        }
        $chainLines.Add("//   $chainIndex) lifetime=$($chain.Lifetime), as=$asDisplay, filter=$($chain.Filter)") | Out-Null
        $chainIndex++
    }

    $chainSummary = ($chainLines -join [Environment]::NewLine)

    $replacement = @"
// AutoWire migration note:
// - This Scrutor Scan() chain was auto-converted.
// - Verify parity with RegistrationSummary manifest diff in CI.
// - Source selector: $sourceHint
$chainSummary
// - Suggested starting point:
//   [assembly: AutoWireScan(""Your.Namespace"", AssemblyOf = typeof($markerType), Lifetime = ""Scoped"")]
// - Then replace broad scanning with explicit attributes ([Scoped]/[Singleton]/[Transient]) for exact parity.
services.AddAutoWireServices();
"@

    return [PSCustomObject]@{
        Replacement = $replacement
        Reason = $null
    }
}

$root = Resolve-Path -LiteralPath $Path
$files = Get-ChildItem -Path $root -Recurse -File -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

$converted = New-Object System.Collections.Generic.List[object]
$skipped = New-Object System.Collections.Generic.List[object]

foreach ($file in $files) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $blocks = @(Get-ScanBlocks -Text $text)
    if ($blocks.Count -eq 0) { continue }

    $updated = $text
    $offset = 0
    $fileConverted = 0

    foreach ($block in $blocks) {
        $conversion = Convert-ScanBlock -ScanText $block.Text
        if ($null -eq $conversion.Replacement) {
            $skipped.Add([PSCustomObject]@{
                File = $file.FullName
                Snippet = $block.Text
                Reason = if ([string]::IsNullOrWhiteSpace($conversion.Reason)) { "Unsupported Scan() shape." } else { $conversion.Reason }
            }) | Out-Null
            continue
        }
        $replacement = $conversion.Replacement

        $start = $block.Start + $offset
        $len = ($block.End - $block.Start + 1)
        $before = $updated.Substring($start, $len)
        $updated = $updated.Remove($start, $len).Insert($start, $replacement)
        $offset += ($replacement.Length - $len)
        $fileConverted++

        $converted.Add([PSCustomObject]@{
            File = $file.FullName
            Before = $before
            After = $replacement
        }) | Out-Null
    }

    if ($fileConverted -gt 0 -and -not $WhatIf) {
        [System.IO.File]::WriteAllText($file.FullName, $updated)
    }
}

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# AutoWire Scrutor Migration Report") | Out-Null
$reportLines.Add("") | Out-Null
$reportLines.Add("**Path:** $root  ") | Out-Null
$reportLines.Add("**Converted blocks:** $($converted.Count)  ") | Out-Null
$reportLines.Add("**Unsupported blocks:** $($skipped.Count)  ") | Out-Null
$reportLines.Add("**Mode:** $(if ($WhatIf) { 'WhatIf (no files modified)' } else { 'Apply' })") | Out-Null
$reportLines.Add("") | Out-Null

$reportLines.Add("## Converted") | Out-Null
$reportLines.Add("") | Out-Null
if ($converted.Count -eq 0) {
    $reportLines.Add("_No supported Scrutor Scan() blocks found._") | Out-Null
}
else {
    $i = 1
    foreach ($item in $converted) {
        $reportLines.Add("### $i. $($item.File)") | Out-Null
        $reportLines.Add("") | Out-Null
        $reportLines.Add("**Before**") | Out-Null
        $reportLines.Add('```csharp') | Out-Null
        $reportLines.Add($item.Before) | Out-Null
        $reportLines.Add('```') | Out-Null
        $reportLines.Add("") | Out-Null
        $reportLines.Add("**After**") | Out-Null
        $reportLines.Add('```csharp') | Out-Null
        $reportLines.Add($item.After) | Out-Null
        $reportLines.Add('```') | Out-Null
        $reportLines.Add("") | Out-Null
        $i++
    }
}

$reportLines.Add("## Could not convert") | Out-Null
$reportLines.Add("") | Out-Null
if ($skipped.Count -eq 0) {
    $reportLines.Add("_None._") | Out-Null
}
else {
    $i = 1
    foreach ($item in $skipped) {
        $reportLines.Add("### $i. $($item.File)") | Out-Null
        $reportLines.Add("") | Out-Null
        $reportLines.Add("Reason: $($item.Reason)") | Out-Null
        $reportLines.Add("") | Out-Null
        $reportLines.Add('```csharp') | Out-Null
        $reportLines.Add($item.Snippet) | Out-Null
        $reportLines.Add('```') | Out-Null
        $reportLines.Add("") | Out-Null
        $i++
    }
}

[System.IO.File]::WriteAllLines($ReportPath, $reportLines)
Write-Host "Migration report written to $ReportPath"
