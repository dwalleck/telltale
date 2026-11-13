---
id: fin-m1
status: active
project: Pipeline
filter: '/*/*/AnomalyBoundedStaysTests/*'
file: src/Telltale.Core/Intervals/BriefVisits.cs
expectedMatches: 1
find: |-
          !stay.BoundedByAnAnomaly &&
replace: |-
          (!stay.BoundedByAnAnomaly || stay.End > stay.Start) &&
---
# fin-m1: a stay bounded by a changelog anomaly is a brief visit again when it has a length

Claim: a stay whose start or end comes from a changelog anomaly is never a brief visit, whatever its length (decision bv-D15; final review R1 and R2). The mutation is the rule as it was before the final fix pass: only a stay of no length is left out. The core tests in `StaysBoundedByAnAnomalyTests` ran red first; the two pipeline tests were written after the rule, so this record stands in for them.
