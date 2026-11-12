---
id: fin-m2
status: active
project: Pipeline
filter: '/*/*/TimeInStatusTableTests/*'
file: src/Telltale.Reporting/SprintReport.cs
expectedMatches: 1
find: |-
      private static string Days(long suppressed, double? days) => Figure(suppressed, days?.ToString("F1", CultureInfo.InvariantCulture));
replace: |-
      private static string Days(long suppressed, double? days) => Figure(suppressed, (days is { } exact ? Math.Truncate(exact * 10) / 10 : (double?)null)?.ToString("F1", CultureInfo.InvariantCulture));
---
# fin-m2: a duration is cut to one decimal place instead of rounded

Claim (B14): a median that is not a round number is shown rounded to one decimal place (1.375 reads 1.4). `A_median_that_is_not_a_round_number_is_shown_to_one_decimal_place` passed when written.
- Verified 2026-11-12 at 5c4f1d9: red, 1 failed of 28 (Pipeline, filter /*/*/TimeInStatusTableTests/*); restored.
