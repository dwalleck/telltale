using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.HarnessSteps;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The treatment <c>exclude</c> for cycle time, end to end (GLOSSARY: Treatment, Cycle time; owner decisions
/// D83, D85, D86, D87, D90, D91, D94 and D101): the owner writes <c>briefVisitTreatment</c> with
/// <c>cycleTime</c> as <c>exclude</c>, and after <c>derive</c> a delivered issue whose cycle starts with a brief
/// visit has no cycle time. It is still delivered.
/// Sprint 41 runs from Monday 5 January 2026, 09:00, to Monday the 19th, 10:00. Its team is five people, each
/// with an issue that stays in New; the tests add the issues that are delivered. Development is listed with a
/// threshold of five minutes unless a test says otherwise.
/// </summary>
public class BriefVisitsLeftOutOfCycleTimeTests
{
    /// <summary>The cycle time rows of every sprint, in sprint and bucket order. Days are rounded to a thousandth.</summary>
    private const string BucketsSql =
        "SELECT sprint_id, point_bucket, issue_count, ROUND(median_business_days, 3) AS median_business_days, ROUND(p85_business_days, 3) AS p85_business_days, ROUND(median_calendar_days, 3) AS median_calendar_days, ROUND(p85_calendar_days, 3) AS p85_calendar_days, suppressed FROM cycle_time_per_sprint_and_bucket ORDER BY sprint_id, point_bucket;";

    private const string BucketsHead =
        "| sprint_id | point_bucket | issue_count | median_business_days | p85_business_days | median_calendar_days | p85_calendar_days | suppressed |\n| --- | --- | --- | --- | --- | --- | --- | --- |\n";

    [Test]
    public async Task A_delivered_issue_whose_cycle_starts_with_a_brief_visit_has_no_cycle_time_and_is_still_delivered()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);

        // Four people's 3-point issues were caught up: a cycle of two days that starts with twenty seconds in
        // Development. Three more did four business days in Development, from Tuesday to the next Monday.
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
        AddFourDaysInDevelopmentForThreePeople(pipeline.Jira);
        ListDevelopment(pipeline);

        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var counted = await QueryAsync(pipeline, BucketsSql);
        LeaveBriefVisitsOutOfCycleTime(pipeline);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);

        // Counted, the seven are in the bucket and the four pull its median down to two days. Left out, the
        // bucket is the three whose cycle is believable, in business and in calendar days.
        await Assert.That(counted).IsEqualTo(BucketsHead + "| 41 | 3 | 7 | 2 | 4 | 2 | 6 | 0 |\n");
        await Assert.That(await QueryAsync(pipeline, BucketsSql)).IsEqualTo(BucketsHead + "| 41 | 3 | 3 | 4 | 4 | 6 | 6 | 0 |\n");
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: 7\n");
        await Assert.That(report).Contains(ReportText.CycleTimeHeader + "| 3 | 3 | 4.0 | 4.0 | 6.0 | 6.0 |\n");
    }

    [Test]
    [Arguments("count", "count", "| Fake Development | 7 | 0.0 | 12.0 |", "| 3 | 7 | 2.0 | 4.0 | 2.0 | 6.0 |", "which are in the cycle time figures: 4")]
    [Arguments("exclude", "count", "| Fake Development" + ReportText.LeftOut.Mark + " | 3 | 4.0 | 12.0 |", "| 3 | 7 | 2.0 | 4.0 | 2.0 | 6.0 |", "which are in the cycle time figures: 4")]
    [Arguments("count", "exclude", "| Fake Development | 7 | 0.0 | 12.0 |", "| 3 | 3 | 4.0 | 4.0 | 6.0 | 6.0 |", "which have no cycle time: 4")]
    [Arguments("exclude", "exclude", "| Fake Development" + ReportText.LeftOut.Mark + " | 3 | 4.0 | 12.0 |", "| 3 | 3 | 4.0 | 4.0 | 6.0 | 6.0 |", "which have no cycle time: 4")]
    public async Task Each_of_the_four_combinations_of_the_two_parts_gives_its_own_time_in_status_row_cycle_time_bucket_and_footer(
        string timeInStatus, string cycleTime, string developmentRow, string bucketRow, string cycleLineEnding)
    {
        // The catch-up sequence, four times: twenty seconds in Development start a cycle of two days. Three
        // more issues did four business days in Development.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
        AddFourDaysInDevelopmentForThreePeople(pipeline.Jira);
        ListDevelopment(pipeline);
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = timeInStatus, ["cycleTime"] = cycleTime };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        // The delivered count, the brief visit count and the number of cycles that start with a brief visit
        // are the same in all four (D85, D87, D93).
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: 7\n");
        await Assert.That(report).Contains("\n" + developmentRow + "\n");
        await Assert.That(report).Contains(ReportText.CycleTimeHeader + bucketRow + "\n");
        // Left out of time in status, the status's line goes on to give the four that are not in its row.
        await Assert.That(report).EndsWith(
            "\n- Fake Development: 4 issues had a visit shorter than 5 minutes" + (timeInStatus == "exclude" ? ReportText.LeftOutOfTheRow("4") : "") + "\n"
            + "- Delivered issues whose cycle starts with a brief visit, " + cycleLineEnding + "\n");
        await Assert.That(await QueryAsync(pipeline, CycleStartsSql)).IsEqualTo(CycleStartsHead + "| 41 | 4 | 0 |\n");
    }

    [Test]
    public async Task An_issue_with_no_cycle_time_keeps_its_delivery_its_delivery_sprint_its_reopen_count_and_its_point_bucket()
    {
        // Besides the usual seven, a caught-up issue that was reopened once and done again, and one delivered
        // after the sprint, in no sprint that has started. The reopened one was estimated at 3 points when its
        // cycle started on Wednesday the 7th and re-estimated to 5 on Thursday, before it was done.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
        AddFourDaysInDevelopmentForThreePeople(pipeline.Jira);
        Scenarios.CaughtUp(pipeline.Jira, "FAKE-5", People.Five).Estimated(3)
            .Reestimated(Local(2026, 1, 8, 10), 5)
            .Moved(Local(2026, 1, 12, 10), Workflow.Review)
            .Moved(Local(2026, 1, 13, 10), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-6", Local(2026, 1, 5, 9)).AssignedTo(People.One).InSprints(41)
            .Moved(Local(2026, 1, 7, 10), Workflow.Development)
            .Moved(Local(2026, 1, 7, 10).AddSeconds(20), Workflow.Review)
            .Moved(Local(2026, 1, 20, 10), Workflow.Done);
        ListDevelopment(pipeline);
        const string DeliveriesSql = "SELECT issue_id, cycle_end, delivered, delivery_sprint_id, reopen_count, starts_with_brief_visit, point_bucket FROM issue_cycle ORDER BY issue_id;";

        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var deliveries = await pipeline.QueryAsync<(string, string?, long, string?, long, long, double?)>(DeliveriesSql);
        var delivered = await QueryAsync(pipeline, "SELECT * FROM delivered_per_sprint;");
        LeaveBriefVisitsOutOfCycleTime(pipeline);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);

        // Every issue's delivery and point bucket are what they were, the reopened one's reopen count is one,
        // and the delivered count, which is what seat value counts of issues, is eight for the sprint. The
        // re-estimated issue is in the bucket of its estimate at the start of its cycle, 3, under either
        // treatment (owner decision D102), though under exclude it has no cycle time.
        await Assert.That(deliveries.Single(issue => issue.Item1 == pipeline.Jira.IdOf("FAKE-5")).Item7).IsEqualTo(3d);
        await Assert.That(await pipeline.QueryAsync<(string, string?, long, string?, long, long, double?)>(DeliveriesSql)).IsEquivalentTo(deliveries);
        await Assert.That(await QueryAsync(pipeline, "SELECT * FROM delivered_per_sprint;")).IsEqualTo(delivered);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 8\n");
        var leftOut = await pipeline.QueryAsync<(string, long, string?, long, double?)>(
            "SELECT issue_id, delivered, delivery_sprint_id, reopen_count, point_bucket FROM issue_cycle WHERE delivered = 1 AND business_days IS NULL ORDER BY reopen_count, delivery_sprint_id;");
        await Assert.That(leftOut.Select(issue => issue.Item1)).IsEquivalentTo(new[] { "FAKE-1", "FAKE-2", "FAKE-3", "FAKE-4", "FAKE-5", "FAKE-6" }.Select(pipeline.Jira.IdOf));
        await Assert.That(leftOut.Single(issue => issue.Item1 == pipeline.Jira.IdOf("FAKE-5"))).IsEqualTo((pipeline.Jira.IdOf("FAKE-5"), 1L, "41", 1L, (double?)3d));
        await Assert.That(leftOut.Single(issue => issue.Item1 == pipeline.Jira.IdOf("FAKE-6"))).IsEqualTo((pipeline.Jira.IdOf("FAKE-6"), 1L, (string?)null, 0L, (double?)null));
    }

    [Test]
    public async Task Only_a_cycle_whose_first_stay_is_a_brief_visit_is_left_out()
    {
        // Seven delivered issues, with Development listed under five minutes and Review not listed.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        var jira = pipeline.Jira;
        var wednesday = Local(2026, 1, 7, 10);

        // Left out: caught up, twenty seconds in Development and then two days in Review.
        Scenarios.CaughtUp(jira, "FAKE-1", People.One);

        // Kept: two days in Review first, and twenty seconds in Development only later in the cycle.
        jira.AddIssue("FAKE-2", Local(2026, 1, 5, 9)).AssignedTo(People.Two).InSprints(41)
            .Moved(Local(2026, 1, 6, 9), Workflow.Review)
            .Moved(Local(2026, 1, 8, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9).AddSeconds(20), Workflow.Done);

        // Kept: the first stay is bounded by a changelog anomaly. Five seconds after the move into
        // Development a change says Review to QE, so nothing says when the issue left Development.
        jira.AddIssue("FAKE-3", Local(2026, 1, 5, 9)).AssignedTo(People.Three).InSprints(41)
            .Moved(wednesday, Workflow.Development)
            .AddHistory(wednesday.AddSeconds(5), "status", "status", Workflow.Review.Id, "Fake Review", Workflow.QE.Id, "Fake QE")
            .NowIn(Workflow.QE)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);

        // Left out: created in Development and moved on twenty seconds later.
        jira.AddIssue("FAKE-4", wednesday, createdIn: Workflow.Development).AssignedTo(People.Four).InSprints(41)
            .Moved(wednesday.AddSeconds(20), Workflow.Review)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);

        // Left out (D91): moved into Development and straight back out, and the work started three days later.
        jira.AddIssue("FAKE-5", Local(2026, 1, 5, 9)).AssignedTo(People.Five).InSprints(41)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 6, 9).AddSeconds(10), Workflow.New)
            .Moved(Local(2026, 1, 9, 9), Workflow.Development)
            .Moved(Local(2026, 1, 13, 9), Workflow.Done);

        // Without a cycle time under either treatment: delivered without ever entering a work status.
        jira.AddIssue("FAKE-6", Local(2026, 1, 5, 9)).AssignedTo(People.One).InSprints(41)
            .Moved(Local(2026, 1, 5, 9).AddSeconds(20), Workflow.Done);

        // Kept: its first stay in a work status is twenty seconds in Review, which is not listed.
        jira.AddIssue("FAKE-7", Local(2026, 1, 5, 9)).AssignedTo(People.Two).InSprints(41)
            .Moved(wednesday, Workflow.Review)
            .Moved(wednesday.AddSeconds(20), Workflow.Development)
            .Moved(wednesday.AddSeconds(40), Workflow.QE)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);
        ListDevelopment(pipeline);
        const string WithoutACycleTimeSql = "SELECT issue_id FROM issue_cycle WHERE delivered = 1 AND cycle_start IS NULL AND calendar_days IS NULL AND business_days IS NULL;";
        const string CycleTimesSql = "SELECT SUM(issue_count) AS cycle_times FROM cycle_time_per_sprint_and_bucket;";

        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var withoutCounted = await pipeline.QueryAsync<string>(WithoutACycleTimeSql);
        var cycleTimesCounted = await QueryAsync(pipeline, CycleTimesSql);
        LeaveBriefVisitsOutOfCycleTime(pipeline);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);

        await Assert.That(withoutCounted).IsEquivalentTo(new[] { jira.IdOf("FAKE-6") });
        await Assert.That(cycleTimesCounted).IsEqualTo("| cycle_times |\n| --- |\n| 6 |\n");
        await Assert.That(await pipeline.QueryAsync<string>(WithoutACycleTimeSql)).IsEquivalentTo(new[] { "FAKE-1", "FAKE-4", "FAKE-5", "FAKE-6" }.Select(jira.IdOf));
        await Assert.That(await QueryAsync(pipeline, CycleTimesSql)).IsEqualTo("| cycle_times |\n| --- |\n| 3 |\n");
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: 7\n");
        await Assert.That(report).EndsWith("\n" + ReportText.CyclesThatStartWithBriefVisitAndHaveNoCycleTime("3"));
    }

    [Test]
    public async Task The_baseline_cycle_time_figures_are_of_the_cycles_that_remain_in_every_baseline_sprint()
    {
        // Sprints 41 and 42 are the baseline. Sprint 41 has the four caught-up issues, two days each, and
        // three of four days. In sprint 42 three people's issues did three days in Development.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprints41And42();
        pipeline.Jira.AddIdleTeam(Local(2026, 1, 2, 9), 41, 42);
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
        AddFourDaysInDevelopmentForThreePeople(pipeline.Jira);
        foreach (var (person, number) in People.Team.Skip(2).Select((person, index) => (person, index + 21)))
        {
            Scenarios.Delivered(pipeline.Jira, 42, $"FAKE-{number}", points: 3, person, days: 3);
        }

        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");
        ListDevelopment(pipeline);
        const string BaselineSql = "SELECT point_bucket, median_business_days, p85_business_days, suppressed FROM baseline_cycle_time_per_bucket;";
        const string BaselineHead = "| point_bucket | median_business_days | p85_business_days | suppressed |\n| --- | --- | --- | --- |\n";

        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var counted = await QueryAsync(pipeline, BaselineSql);
        LeaveBriefVisitsOutOfCycleTime(pipeline);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "42"]);

        // Counted, ten cycles are pooled: 2, 2, 2, 2, 3, 3, 3, 4, 4, 4. Left out, six: 3, 3, 3, 4, 4, 4.
        await Assert.That(counted).IsEqualTo(BaselineHead + "| 3 | 3 | 4 | 0 |\n");
        await Assert.That(await QueryAsync(pipeline, BaselineSql)).IsEqualTo(BaselineHead + "| 3 | 3.5 | 4 | 0 |\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains(ReportText.CycleTimeHeaderWithBaseline + "| 3 | 3 | 3.0 | 3.0 | 3.0 | 3.0 | 3.5 | 4.0 |\n");
    }

    [Test]
    [Arguments("count", "count", false)]
    [Arguments("exclude", "count", true)]
    [Arguments("count", "exclude", true)]
    [Arguments("exclude", "exclude", true)]
    public async Task The_method_note_says_of_the_baseline_that_brief_visits_are_left_out_of_it_when_either_metric_leaves_them_out(string timeInStatus, string cycleTime, bool leftOut)
    {
        // Sprint 41 is the baseline, and the report is sprint 42's.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprints41And42();
        pipeline.Jira.AddIdleTeam(Local(2026, 1, 2, 9), 41, 42);
        AddFourDaysInDevelopmentForThreePeople(pipeline.Jira);
        pipeline.Config["baselineSprintIds"] = new JsonArray("41");
        ListDevelopment(pipeline);
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = timeInStatus, ["cycleTime"] = cycleTime };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "42"]);

        // While both metrics count brief visits the sentence is what it was before a treatment could be chosen.
        const string Pooled = "\n- The baseline columns pool every issue of the baseline sprints, a list fixed in the configuration (sprints 41), each a completed sprint";
        var report = await pipeline.ReadReportAsync(42);
        await Assert.That(report).Contains(leftOut
            ? Pooled + ", with brief visits left out as they are in this report's own figures (see the paragraph on brief visits below). A baseline figure reads "
            : Pooled + ". A baseline figure reads ");
    }

    [Test]
    public async Task A_derive_that_leaves_cycles_out_changes_no_raw_table()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
        AddFourDaysInDevelopmentForThreePeople(pipeline.Jira);
        ListDevelopment(pipeline);
        LeaveBriefVisitsOutOfCycleTime(pipeline);
        await pipeline.RunAllAsync(["backfill"]);
        var before = BlocksOf(await pipeline.ReadStoreContentAsync());

        await pipeline.RunAllAsync(["derive"]);

        // Before the derive the store holds the raw tables and nothing derived. derived_object is the list of
        // what a rebuild created, so it is the one table of these that a derive fills.
        var after = BlocksOf(await pipeline.ReadStoreContentAsync());
        await Assert.That(before.Keys).Contains("issue");
        await Assert.That(before.Keys).Contains("change");
        await Assert.That(before.Keys).DoesNotContain("issue_cycle");
        await Assert.That(after.Keys).Contains("issue_cycle");
        foreach (var name in before.Keys.Except(["derived_object"]))
        {
            await Assert.That(after[name]).IsEqualTo(before[name]);
        }
    }

    [Test]
    public async Task The_line_under_the_heading_names_cycle_time_and_names_both_metrics_when_both_were_adjusted()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourDaysInDevelopmentForThreePeople(pipeline.Jira);
        ListDevelopment(pipeline);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);
        var counted = await pipeline.ReadReportAsync(41);
        LeaveBriefVisitsOutOfCycleTime(pipeline);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);
        var cycleTimeLeftOut = await pipeline.ReadReportAsync(41);
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude", ["cycleTime"] = "exclude" };
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);
        var bothLeftOut = await pipeline.ReadReportAsync(41);

        await Assert.That(counted).StartsWith(Heading + "Delivered issues: ");
        await Assert.That(cycleTimeLeftOut).StartsWith(
            Heading
            + ReportText.LeftOut.LineForCycleTime
            + "Delivered issues: ");
        await Assert.That(bothLeftOut).StartsWith(
            Heading
            + ReportText.LeftOut.LineForBoth
            + "Delivered issues: ");
    }

    [Test]
    [Arguments("cycles left out and cycles kept", "| Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) |\n| --- | ---: | ---: | ---: | ---: | ---: |\n| 3 | 3 | 4.0 | 4.0 | 6.0 | 6.0 |\n")]
    [Arguments("nothing left out", "| Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) |\n| --- | ---: | ---: | ---: | ---: | ---: |\n| 3 | 3 | 4.0 | 4.0 | 6.0 | 6.0 |\n")]
    [Arguments("every cycle left out", "No issue delivered in this sprint has a cycle time.\n")]
    [Arguments("a sprint short of a team", "suppressed\n")]
    public async Task The_cycle_time_table_carries_one_note_under_it_whatever_it_holds(string sprint, string table)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        if (sprint != "a sprint short of a team")
        {
            pipeline.Jira.AddIdleTeam(Local(2026, 1, 2, 9), 41);
        }

        if (sprint != "nothing left out")
        {
            AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
        }

        if (sprint is "cycles left out and cycles kept" or "nothing left out")
        {
            AddFourDaysInDevelopmentForThreePeople(pipeline.Jira);
        }

        ListDevelopment(pipeline);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);
        var counted = await pipeline.ReadReportAsync(41);
        LeaveBriefVisitsOutOfCycleTime(pipeline);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);

        // The note is the table's last line, once, and a report that counts every cycle has none (D101).
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\n## Cycle time by point bucket\n\n" + table + NoteUnderTheTable + "\n## Time in status\n");
        await Assert.That(report.Split(NoteUnderTheTable).Length).IsEqualTo(2);
        await Assert.That(counted).DoesNotContain("has no cycle time and is not in this table");
    }

    [Test]
    public async Task Nothing_else_in_the_report_differs_but_the_line_the_note_the_footer_lines_ending_and_the_method_note_sentences_on_cycle_time()
    {
        // Nobody's cycle starts with a brief visit, so every figure is the same under either treatment.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourDaysInDevelopmentForThreePeople(pipeline.Jira);
        ListDevelopment(pipeline);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);
        var counted = await pipeline.ReadReportAsync(41);
        LeaveBriefVisitsOutOfCycleTime(pipeline);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);
        var cycleTimeLeftOut = await pipeline.ReadReportAsync(41);

        // The report with the line under the heading and the note taken away, and the footer line's ending
        // and the method note's sentences put back as they read under count, is the report under count. The
        // note differs in the sentence on what cycle time is and, in its paragraph on brief visits, in what is
        // done with them, in whether the issues the footer's last line counts have a cycle time, and in the
        // sentence on when leaving out was added.
        await Assert.That(counted).Contains(CycleTimeSentence);
        await Assert.That(counted).Contains(ReportText.Treatment.NothingIsDone);
        await Assert.That(counted).Contains(ReportText.Treatment.TheyAreInTheFigures);
        await Assert.That(cycleTimeLeftOut).Contains(CycleTimeSentence + CycleTimeSentenceLeftOut);
        await Assert.That(cycleTimeLeftOut).Contains(WhatIsDoneWithCycleTimeAlone);
        await Assert.That(cycleTimeLeftOut).Contains(ReportText.Treatment.TheyHaveNoCycleTime);
        await Assert.That(cycleTimeLeftOut).Contains(ReportText.Treatment.WhenLeavingOutWasAdded("cycle time is"));
        var putBack = cycleTimeLeftOut
            .Replace(ReportText.LeftOut.LineForCycleTime, "")
            .Replace(NoteUnderTheTable, "")
            .Replace(CycleTimeSentenceLeftOut, "")
            .Replace(WhatIsDoneWithCycleTimeAlone, ReportText.Treatment.NothingIsDone)
            .Replace(ReportText.Treatment.TheyHaveNoCycleTime, ReportText.Treatment.TheyAreInTheFigures)
            .Replace(ReportText.Treatment.WhenLeavingOutWasAdded("cycle time is"), "")
            .Replace(ReportText.CyclesThatStartWithBriefVisitAndHaveNoCycleTime("suppressed"), ReportText.CyclesThatStartWithBriefVisit("suppressed"));
        await Assert.That(putBack).IsEqualTo(counted);
    }

    [Test]
    public async Task With_both_metrics_left_out_the_report_is_the_one_for_time_in_status_alone_plus_what_cycle_time_adds()
    {
        // Nobody's cycle starts with a brief visit, so every figure is the same under either treatment.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourDaysInDevelopmentForThreePeople(pipeline.Jira);
        ListDevelopment(pipeline);
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude" };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);
        var timeInStatusLeftOut = await pipeline.ReadReportAsync(41);
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude", ["cycleTime"] = "exclude" };
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);
        var bothLeftOut = await pipeline.ReadReportAsync(41);

        // The paragraph on brief visits says what is done to each metric, and names the setting once. Cycle
        // time adds its sentences to it: what is left out, the reason for a cycle, that the issues the footer's
        // last line counts have no cycle time, and cycle time among the metrics measured another way.
        await Assert.That(timeInStatusLeftOut).Contains(
            ReportText.Treatment.IsASetting + ReportText.Treatment.LeftOutOfTimeInStatusAlone + ReportText.Treatment.TimeInStatusIsLeftOut + ReportText.Treatment.Why + ReportText.Treatment.WhatLeavingOutDoesNotDo);
        await Assert.That(bothLeftOut).Contains(
            ReportText.Treatment.IsASetting + ReportText.Treatment.LeftOutOfBoth + ReportText.Treatment.TimeInStatusIsLeftOut + ReportText.Treatment.CycleTimeIsLeftOut
            + ReportText.Treatment.Why + ReportText.Treatment.WhyForACycle + ReportText.Treatment.WhatLeavingOutDoesNotDo);
        await Assert.That(bothLeftOut.Split(ReportText.Treatment.IsASetting).Length).IsEqualTo(2);
        var putBackToTimeInStatus = bothLeftOut
            .Replace(ReportText.LeftOut.LineForBoth, ReportText.LeftOut.LineForTimeInStatus)
            .Replace(NoteUnderTheTable, "")
            .Replace(CycleTimeSentenceLeftOut, "")
            .Replace(ReportText.Treatment.LeftOutOfBoth, ReportText.Treatment.LeftOutOfTimeInStatusAlone)
            .Replace(ReportText.Treatment.CycleTimeIsLeftOut, "")
            .Replace(ReportText.Treatment.WhyForACycle, "")
            .Replace(ReportText.Treatment.TheyHaveNoCycleTime, ReportText.Treatment.TheyAreInTheFigures)
            .Replace(ReportText.Treatment.WhenLeavingOutWasAdded("time in status and cycle time are"), ReportText.Treatment.WhenLeavingOutWasAdded("time in status is"))
            .Replace(ReportText.CyclesThatStartWithBriefVisitAndHaveNoCycleTime("suppressed"), ReportText.CyclesThatStartWithBriefVisit("suppressed"));
        await Assert.That(putBackToTimeInStatus).IsEqualTo(timeInStatusLeftOut);
    }

    private const string Heading = "# Fake Sprint 41 (2026-01-05 to 2026-01-19)\n\n";

    /// <summary>The note under the cycle time table when cycles that start with a brief visit are left out (D101), with the empty line that parts it from the table.</summary>
    private const string NoteUnderTheTable = ReportText.LeftOut.NoteUnderTheCycleTimeTable;

    /// <summary>The method note's sentences on what cycle time is, as they read under either treatment.</summary>
    private const string CycleTimeSentence =
        "- Cycle time runs from an issue's first entry into a work status to its last entry into a done status, for delivered issues only. It belongs to the sprint in which the issue was delivered.";

    /// <summary>What follows them when cycles are left out.</summary>
    private const string CycleTimeSentenceLeftOut =
        " A delivered issue whose cycle starts with a brief visit has no cycle time (see the paragraph on brief visits below).";

    /// <summary>What the paragraph on brief visits says is done with them when cycle time alone leaves them out, in place of the sentence that they are counted and nothing more.</summary>
    private const string WhatIsDoneWithCycleTimeAlone =
        ReportText.Treatment.IsASetting + ReportText.Treatment.LeftOutOfCycleTimeAlone + ReportText.Treatment.CycleTimeIsLeftOut
        + ReportText.Treatment.Why + ReportText.Treatment.WhyForACycle + ReportText.Treatment.WhatLeavingOutDoesNotDo;

    private const string CycleStartsSql = "SELECT sprint_id, issue_count, suppressed FROM cycle_starts_with_brief_visit_per_sprint ORDER BY sprint_id;";

    private const string CycleStartsHead = "| sprint_id | issue_count | suppressed |\n| --- | --- | --- |\n";

    /// <summary>The store's content (<see cref="PipelineHarness.ReadStoreContentAsync"/>) as one block per table or view, by its name; a block starts with the name.</summary>
    private static Dictionary<string, string> BlocksOf(string storeContent) =>
        ("\n" + storeContent).Split("\n## ", StringSplitOptions.RemoveEmptyEntries).ToDictionary(block => block[..block.IndexOf('\n')], block => block + "\n", StringComparer.Ordinal);

    private static void ListDevelopment(PipelineHarness pipeline) =>
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

    private static void LeaveBriefVisitsOutOfCycleTime(PipelineHarness pipeline) =>
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["cycleTime"] = "exclude" };

    /// <summary>Sprint 41 as the harness has it, and a team of five whose issues stay in New through it.</summary>
    private static void AddSprint41WithItsTeam(FakeJira jira)
    {
        jira.AddSprint41();
        jira.AddIdleTeam(Local(2026, 1, 2, 9), 41);
    }

    /// <summary>Four 3-point issues <see cref="Scenarios.CaughtUp"/>, one for each of four people: FAKE-1 to FAKE-4. Each has a cycle of two days, business and calendar.</summary>
    private static void AddFourCaughtUpIssuesOfFourPeople(FakeJira jira)
    {
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            Scenarios.CaughtUp(jira, $"FAKE-{number}", person).Estimated(3);
        }
    }

    /// <summary>
    /// Three 3-point issues of the team's last three people, FAKE-11 to FAKE-13, each in Development from
    /// Tuesday 6 January 09:00 to Monday the 12th and then done: a cycle of four business days and six calendar days.
    /// </summary>
    private static void AddFourDaysInDevelopmentForThreePeople(FakeJira jira)
    {
        foreach (var (person, number) in People.Team.Skip(2).Select((person, index) => (person, index + 11)))
        {
            Scenarios.Delivered(jira, 41, $"FAKE-{number}", points: 3, person, days: 4);
        }
    }
}
