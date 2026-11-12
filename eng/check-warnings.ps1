# Compares the compiler warnings in a build log with the baseline file. Called by the repo toolkit's full check as
# -BuildLog <log> -Baseline <file> -RepoRoot <root>. Exits 0 when every warning is in the baseline.
# A warning is identified by "<repo-relative file>: <code>", so line numbers can move without failing the check.
param(
    [Parameter(Mandatory)][string]$BuildLog,
    [Parameter(Mandatory)][string]$Baseline,
    [Parameter(Mandatory)][string]$RepoRoot
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepoRoot).Path.TrimEnd('\', '/')
$found = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($line in Get-Content -LiteralPath $BuildLog)
{
    if ($line -match '^\s*(?:\d+>)?(?<file>.*?)(?:\(\d+(?:,\d+)*\))?\s*:\s*warning\s+(?<code>[A-Za-z]+\d+)\s*:')
    {
        $file = $Matches.file.Trim()
        if ($file.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase))
        {
            $file = $file.Substring($root.Length).TrimStart('\', '/')
        }

        [void]$found.Add(($file -replace '\\', '/') + ': ' + $Matches.code)
    }
}

$known = @()
if (Test-Path -LiteralPath $Baseline -PathType Leaf)
{
    $known = @(Get-Content -LiteralPath $Baseline | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
}

$new = @($found | Where-Object { $known -cnotcontains $_ })
"warnings: $($found.Count) found, $($known.Count) in baseline, $($new.Count) new"
foreach ($entry in $new)
{
    "  new warning $entry"
}

exit ([int]($new.Count -gt 0))
