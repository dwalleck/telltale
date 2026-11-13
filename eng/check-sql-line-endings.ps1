#!/usr/bin/env pwsh
# Checks the line endings of the tracked .sql files: LF only, no byte-order mark, a final newline.
# The toolkit's own line-endings check (Test-LineEndings) reads a fixed list of extensions that does not
# include .sql, so the embedded scripts were never checked. Their text is also what identifies the derived
# objects (SqliteStore.DerivedObjectsVersion), so it must be the same bytes on every machine.
# Called by Invoke-FullCheck as an ExtraChecks script: -Base <sha> -RepoRoot <root>. Exit 0 passes.
param(
    [string]$Base,
    [Parameter(Mandatory)][string]$RepoRoot
)

$ErrorActionPreference = 'Stop'
$files = @(git -C $RepoRoot ls-files -- '*.sql')
if ($LASTEXITCODE -ne 0) { Write-Error "git ls-files failed in $RepoRoot"; exit 1 }

$findings = [System.Collections.Generic.List[string]]::new()
foreach ($file in $files)
{
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $RepoRoot $file))
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $findings.Add("${file}: starts with a byte-order mark") }
    if ($bytes -contains 13) { $findings.Add("${file}: holds a carriage return (CRLF or CR line endings)") }
    if ($bytes.Length -gt 0 -and $bytes[-1] -ne 10) { $findings.Add("${file}: does not end with a newline") }
}

if ($findings.Count -gt 0)
{
    $findings | ForEach-Object { Write-Output $_ }
    Write-Output "sql line endings: FAIL ($($findings.Count) finding(s) in $($files.Count) file(s))"
    exit 1
}

Write-Output "sql line endings: pass ($($files.Count) file(s): LF, no BOM, final newline)"
exit 0
