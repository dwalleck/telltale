---
id: fin-m11
status: active
project: Pipeline
filter: '/*/*/(BackfillRebuildTests|PrivacyTests)/*'
file: src/Telltale.Storage.Sqlite/SqliteStore.cs
expectedMatches: 1
find: |-
              await connection.ExecuteAsync(Command(cancellationToken, "PRAGMA secure_delete = ON;"));
replace: |-
              await connection.ExecuteAsync(Command(cancellationToken, "PRAGMA secure_delete = OFF;"));
---
# fin-m11: deleted rows stay readable in the file

Claim (C6): a connection that writes overwrites what it deletes. One of the three byte-level tests ran red first; this shows which of them need the setting.
- Verified 2026-11-12 at 5c4f1d9: red, 1 failed of 28 (Pipeline, filter /*/*/(BackfillRebuildTests|PrivacyTests)/*); restored.
- Verified 2026-11-12 at 741dd98: red, 1 failed of 28 (Pipeline, filter /*/*/(BackfillRebuildTests|PrivacyTests)/*); restored.
- Verified 2026-11-12 at a198e06: red, 1 failed of 28 (Pipeline, filter /*/*/(BackfillRebuildTests|PrivacyTests)/*); restored.
