---
id: fin-m3
status: control
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
---
# fin-m3 (control): a field the staleness check forgot is still refused, by the whole-record comparison

Claim (final review R5): the check of minimumGroupSize is made never to fire (the raw digests are equal by then), which is what a field added to DerivedSource and forgotten in RequireCurrentDerivedDataAsync looks like. The comparison of the two records whole still refuses report and query, so the test that nothing is shown until derive has run passes: the record is expected to survive. fin-m4 is the same mutation with the comparison gone, and goes red.
