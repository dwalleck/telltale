@{
    SchemaVersion   = 1
    Solution        = 'Telltale.slnx'
    SourcePaths     = @('src', 'tests')
    LogDir          = '.ipe-logs'
    WorktreeDir     = '.worktrees'
    DrivePool       = @('W', 'X', 'Y', 'Z')
    MutationSpecDir = '.scratch/{feature}/mutations'
    UseSubst        = $false
    TestProjects    = @(
        @{ Name = 'Core'; Path = 'tests/Telltale.Core.Tests/Telltale.Core.Tests.csproj'; Kind = 'tunit'; NeedsSql = $false; AllowEmpty = $false }
        @{ Name = 'Pipeline'; Path = 'tests/Telltale.Pipeline.Tests/Telltale.Pipeline.Tests.csproj'; Kind = 'tunit'; NeedsSql = $false; AllowEmpty = $false }
    )
    # No project uses SQL Server (the store is SQLite). The toolkit requires this section; nothing reads it
    # while every project has NeedsSql = $false.
    Sql = @{
        EnvVar                = 'TELLTALE_UNUSED_SQL'
        ConnectionFile        = '.repo-toolkit.unused-sql.txt'
        Server                = '127.0.0.1,1'
        StartCommand          = @('true')
        LeakedDatabasePattern = '^telltale_unused_[0-9a-f]{32}$'
    }
    FormatExclusions = @()
    Warnings  = @{ Script = 'eng/check-warnings.ps1'; Baseline = 'eng/warnings-baseline.txt' }
    Commit    = @{ Types = @('feat', 'fix', 'test', 'chore', 'docs', 'refactor'); DenyNames = @('HANDOFF.md', 'CODE-REVIEW.md'); DenySegments = @('.vs') }
    Timeouts  = @{ BuildSeconds = 600; XunitSeconds = 600; TunitSeconds = 600; TunitInner = '5m'; FormatSeconds = 300;
                   GitSeconds = 120; SubstSeconds = 30; SqlStartSeconds = 120; SqlProbeSeconds = 60; CleanupSeconds = 300;
                   FocusedSeconds = 600; MutationHangSeconds = 300 }
}
