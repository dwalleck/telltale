# 01: The SQL line-endings check reads every file git lists, whatever the starting folder and the file name

**What to build:** `eng/check-sql-line-endings.ps1` reports pass or fail for every tracked `.sql` file. Today it can stop with an exception instead, in two cases. CI runs the script since PR 4, so the second case would fail a build with a file-not-found error and give no line-ending finding.

1. **A relative `-RepoRoot` is resolved two ways.** `git -C $RepoRoot` resolves it against PowerShell's location. `[System.IO.File]::ReadAllBytes((Join-Path $RepoRoot $file))` resolves it against the .NET process's current directory. `Set-Location` and `Push-Location` change only the first. Seen in pwsh 7.7: start pwsh in the home folder, `Set-Location` (or `Push-Location`) into the repository, then run `./eng/check-sql-line-endings.ps1 -RepoRoot .`. git lists the files, and `ReadAllBytes` looks for `<home>\src\Telltale.Storage.Sqlite\Sql\Derived\anomaly.sql`, so the script stops with `Could not find a part of the path`. CI is not affected today, because pwsh starts in the workspace there, and `Invoke-FullCheck` passes the root it was given.
2. **git quotes some paths.** With `core.quotePath` at its default of true, `git ls-files` prints a path that has a non-ASCII character or a control byte in quotes, with escape sequences. The script passes that text to `ReadAllBytes` unchanged. Seen in a scratch repository with one tracked file `café.sql`: git lists it as `"caf\303\251.sql"`, and the script stops with `The filename, directory name, or volume label syntax is incorrect`. pwsh exits 1, so a CI step fails, but the message gives no line-ending reason.

The fix the review suggested, for each case: resolve the root once at the top, as `eng/check-warnings.ps1` does (`$root = (Resolve-Path -LiteralPath $RepoRoot).Path`), and use that for both git and the file reads. List the files with `git -c core.quotePath=false ls-files -z -- '*.sql'`, and split the output on NUL.

Background: the review of PR 4, findings 7 and 8 (2026-11-20). Both problems were in the script before PR 4. PR 4 made them matter by running the script in CI. Both failures were reproduced locally on 2026-11-20.

**Blocked by:** None (can start immediately).

**Status:** ready-for-agent

## Acceptance criteria

- [ ] Started from another folder, then moved into the repository with `Set-Location` or `Push-Location`, `./eng/check-sql-line-endings.ps1 -RepoRoot .` prints the same pass line as when it is run from the repository root
- [ ] In a scratch repository whose one tracked file is `café.sql` with LF endings, no BOM and a final newline, the script passes, and it counts one file
- [ ] In the same scratch repository, a `café.sql` that holds a CR is reported by name as holding a carriage return, and the script exits 1
- [ ] A path with a space in it is read and checked
- [ ] The pass line and the finding lines are worded as they are today, so `Invoke-FullCheck` and the CI step read them unchanged
- [ ] The script still exits 1 with its own message when `git ls-files` fails
- [ ] The scratch repositories are created under a temporary folder and removed afterwards; nothing is added to this repository's tracked `.sql` files

## Comments
