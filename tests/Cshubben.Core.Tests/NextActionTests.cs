using Cshubben.Core.Data;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Xunit;

namespace Cshubben.Core.Tests;

public class NextActionTests
{
    private static readonly HubSnapshot Demo = new DemoData(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero)).Build();

    private static NextAction Of(string id)
    {
        var pr = Demo.Find(id)!;
        Demo.ReviewInfos.TryGetValue(id, out var info);
        return NextAction.For(pr, info);
    }

    [Theory]
    [InlineData("a1", NextActionKind.Merge)]
    [InlineData("a3", NextActionKind.AddressChanges)]
    [InlineData("a4", NextActionKind.FixCi)]
    [InlineData("a5", NextActionKind.ResolveConflicts)]
    [InlineData("a6", NextActionKind.ResolveThreads)]
    [InlineData("a7", NextActionKind.MarkReady)]
    [InlineData("a8", NextActionKind.WaitForReview)]
    public void Authored_prs_get_the_expected_action(string id, NextActionKind expected) =>
        Assert.Equal(expected, Of(id).Kind);

    [Theory]
    [InlineData("r1", NextActionKind.StartReview)]
    [InlineData("r3", NextActionKind.ReReview)]
    [InlineData("r4", NextActionKind.NothingToDo)]
    [InlineData("r5", NextActionKind.WaitForAuthor)]
    public void Review_queue_prs_get_the_expected_action(string id, NextActionKind expected) =>
        Assert.Equal(expected, Of(id).Kind);

    [Fact]
    public void Conflicts_beat_failing_ci()
    {
        var pr = Demo.Find("a1")! with { Merge = MergeState.Conflicting, Ci = CiState.Failure };
        Assert.Equal(NextActionKind.ResolveConflicts, NextAction.For(pr).Kind);
    }

    [Fact]
    public void Pending_ci_blocks_merge()
    {
        var pr = Demo.Find("a1")! with { Ci = CiState.Pending };
        Assert.Equal(NextActionKind.WaitForCi, NextAction.For(pr).Kind);
    }

    [Fact]
    public void One_thread_uses_singular_label() =>
        Assert.Equal("Resolve 1 thread", NextAction.For(Demo.Find("a6")!).Label);

    [Fact]
    public void Only_actionable_kinds_are_marked_actionable()
    {
        Assert.True(Of("a1").IsActionable);
        Assert.False(Of("a8").IsActionable);
        Assert.False(Of("r4").IsActionable);
    }
}
