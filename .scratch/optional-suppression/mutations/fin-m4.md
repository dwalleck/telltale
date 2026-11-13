---
id: fin-m4
status: active
project: Pipeline
filter: '/*/*/MinimumGroupSizeStalenessTests/After_one_of_the_two_numbers_changes_report_writes_no_file_and_query_prints_no_row_until_derive_has_run'
edits:
  - file: src/Telltale.Pipeline/PipelineSteps.cs
    expectedMatches: 1
    find: |-
              if (derivedFrom.MinimumTeam != current.MinimumTeam
    replace: |-
              if ((derivedFrom.MinimumTeam != current.MinimumTeam
  - file: src/Telltale.Pipeline/PipelineSteps.cs
    expectedMatches: 1
    find: |-
                  || derivedFrom.MinimumAssigneesBehindAFigure != current.MinimumAssigneesBehindAFigure)
    replace: |-
                  || derivedFrom.MinimumAssigneesBehindAFigure != current.MinimumAssigneesBehindAFigure) && derivedFrom.RawDigest != current.RawDigest)
  - file: src/Telltale.Pipeline/PipelineSteps.cs
    expectedMatches: 1
    find: |-
              if (derivedFrom != current)
    replace: |-
              if (derivedFrom != current && derivedFrom.RawDigest != current.RawDigest)
---
# fin-m4: without the whole-record comparison a forgotten field passes stale derived data

Claim (final review R5): fin-m3's mutation with the whole-record comparison made never to fire as well. Report and query then answer from data derived under the old numbers. No field is forgotten today, so no test could run red first against the comparison itself.
