using Cshubben.Core.Features.Pipeline;
using Xunit;
using static Cshubben.Core.Tests.Features.Pipeline.PipelineTestData;

namespace Cshubben.Core.Tests.Features.Pipeline;

public class TimelineTests
{
    [Fact]
    public void No_checks_is_the_empty_timeline()
    {
        var t = PipelineTimeline.Build(Array.Empty<CheckRun>(), Now);
        Assert.Equal(PipelineState.None, t.Overall);
        Assert.Equal("No checks reported yet", t.Summary);
        Assert.Empty(t.Stages);
    }

    [Fact]
    public void All_successful_checks_pass_with_wall_clock()
    {
        var t = PipelineTimeline.Build(new[]
        {
            Check("build", RunState.Success, 1.0, 120, 1),
            Check("test", RunState.Success, 0.99, 300, 2),
        }, Now);
        Assert.Equal(PipelineState.Passing, t.Overall);
        Assert.Equal(2, t.Passed);
        Assert.StartsWith("All 2 checks passed in ", t.Summary);
    }

    [Fact]
    public void A_failing_check_makes_the_pipeline_fail_and_names_it()
    {
        var t = PipelineTimeline.Build(new[]
        {
            Check("build", RunState.Success, 1.0, 60, 1),
            Check("integration", RunState.Failure, 0.9, 90, 2, 77, "Run tests"),
        }, Now);
        Assert.Equal(PipelineState.Failing, t.Overall);
        Assert.Equal("integration", t.FirstFailing!.Name);
        Assert.Equal("Run tests", t.FirstFailing.FailingStep);
        Assert.Equal("1 of 2 checks failing (integration)", t.Summary);
    }

    [Fact]
    public void Failing_summary_shortens_long_lists()
    {
        var t = PipelineTimeline.Build(new[]
        {
            Check("a", RunState.Failure, 1.0, 10, 1),
            Check("b", RunState.Failure, 0.9, 10, 2),
            Check("c", RunState.Failure, 0.8, 10, 3),
        }, Now);
        Assert.Equal("3 of 3 checks failing (a, b +1 more)", t.Summary);
    }

    [Fact]
    public void Running_check_gives_running_state_and_progress_text()
    {
        var t = PipelineTimeline.Build(new[]
        {
            Check("build", RunState.Success, 1.0, 60, 1),
            Check("test", RunState.Running, 0.5, null, 2),
        }, Now);
        Assert.Equal(PipelineState.Running, t.Overall);
        Assert.Equal("1 of 2 checks done, 1 still running", t.Summary);
    }

    [Fact]
    public void Queued_only_gives_queued_state()
    {
        var t = PipelineTimeline.Build(new[] { Check("build", RunState.Queued, 0.1, null, 1) }, Now);
        Assert.Equal(PipelineState.Queued, t.Overall);
        Assert.Contains("waiting to start", t.Summary);
    }

    [Fact]
    public void Failure_wins_over_running()
    {
        var t = PipelineTimeline.Build(new[]
        {
            Check("a", RunState.Running, 0.5, null, 1),
            Check("b", RunState.Failure, 0.8, 30, 2),
        }, Now);
        Assert.Equal(PipelineState.Failing, t.Overall);
    }

    [Fact]
    public void Stages_are_ordered_by_start_time()
    {
        var t = PipelineTimeline.Build(new[]
        {
            Check("late", RunState.Success, 0.2, 10, 1),
            Check("early", RunState.Success, 2.0, 10, 2),
            Check("middle", RunState.Success, 1.0, 10, 3),
        }, Now);
        Assert.Equal(new[] { "early", "middle", "late" }, t.Stages.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Retry_that_passes_is_flaky()
    {
        var t = PipelineTimeline.Build(new[]
        {
            Check("tests", RunState.Failure, 1.0, 100, 1),
            Check("tests", RunState.Success, 0.8, 100, 2),
        }, Now);
        var stage = Assert.Single(t.Stages);
        Assert.True(stage.Flaky);
        Assert.Equal(2, stage.Attempts);
        Assert.Equal(RunState.Success, stage.State);
        Assert.Equal(1, t.FlakyCount);
        Assert.Contains("1 flaky", t.Summary);
    }

    [Fact]
    public void Retry_that_still_fails_is_not_flaky()
    {
        var t = PipelineTimeline.Build(new[]
        {
            Check("tests", RunState.Failure, 1.0, 100, 1),
            Check("tests", RunState.Failure, 0.8, 100, 2),
        }, Now);
        var stage = Assert.Single(t.Stages);
        Assert.False(stage.Flaky);
        Assert.Equal(2, stage.Attempts);
        Assert.Equal(RunState.Failure, stage.State);
    }

    [Fact]
    public void A_single_pass_is_not_flaky()
    {
        var t = PipelineTimeline.Build(new[] { Check("tests", RunState.Success, 1, 10, 1) }, Now);
        Assert.False(t.Stages[0].Flaky);
    }

    [Fact]
    public void Newest_attempt_decides_the_state_regardless_of_input_order()
    {
        var t = PipelineTimeline.Build(new[]
        {
            Check("tests", RunState.Success, 0.5, 100, 2),
            Check("tests", RunState.Failure, 1.5, 100, 1),
        }, Now);
        Assert.Equal(RunState.Success, t.Stages[0].State);
        Assert.True(t.Stages[0].Flaky);
    }

    [Fact]
    public void Failed_run_ids_are_distinct_and_only_from_failed_stages()
    {
        var t = PipelineTimeline.Build(new[]
        {
            Check("a", RunState.Failure, 1.0, 10, 1, 500),
            Check("b", RunState.Failure, 0.9, 10, 2, 500),
            Check("c", RunState.Failure, 0.8, 10, 3, 600),
            Check("d", RunState.Success, 0.7, 10, 4, 700),
            Check("e", RunState.Failure, 0.6, 10, 5, null),
        }, Now);
        Assert.Equal(new long[] { 500, 600 }, t.FailedRunIds.ToArray());
    }

    [Fact]
    public void Longest_is_the_longest_finished_duration()
    {
        var t = PipelineTimeline.Build(new[]
        {
            Check("a", RunState.Success, 1.0, 30, 1),
            Check("b", RunState.Success, 0.9, 200, 2),
        }, Now);
        Assert.Equal(TimeSpan.FromSeconds(200), t.Longest);
    }

    [Fact]
    public void Wall_clock_while_running_ends_now()
    {
        var t = PipelineTimeline.Build(new[] { Check("a", RunState.Running, 0.5, null, 1) }, Now);
        Assert.Equal(TimeSpan.FromMinutes(30), t.WallClock);
    }

    [Fact]
    public void Skipped_only_checks_are_not_passing()
    {
        var t = PipelineTimeline.Build(new[] { Check("deploy", RunState.Skipped, 1, 0, 1) }, Now);
        Assert.Equal(PipelineState.None, t.Overall);
    }
}
