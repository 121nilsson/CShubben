using System.Net;
using System.Text.Json;
using Cshubben.Core.Features.Linear;
using Xunit;

namespace Cshubben.Core.Tests.Integrations;

public class LinearStateTests
{
    private const string Key = "lin_api_SECRETKEY123";

    private static LinearClient Client(FakeHandler handler) =>
        new(new HttpClient(handler), () => Key, (_, _) => Task.CompletedTask);

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private const string StatesJson =
        "{\"nodes\":[" +
        "{\"id\":\"s1\",\"name\":\"Backlog\",\"type\":\"backlog\",\"position\":0}," +
        "{\"id\":\"s2\",\"name\":\"In Progress\",\"type\":\"started\",\"position\":2}," +
        "{\"id\":\"s4\",\"name\":\"Shipped\",\"type\":\"completed\",\"position\":6}," +
        "{\"id\":\"s3\",\"name\":\"Done\",\"type\":\"completed\",\"position\":3}," +
        "{\"id\":\"s5\",\"name\":\"Canceled\",\"type\":\"canceled\",\"position\":7}," +
        "{\"id\":\"\",\"name\":\"Broken\",\"type\":\"completed\",\"position\":1}]}";

    [Fact]
    public void Team_states_query_passes_the_team_id_and_asks_for_type_and_position()
    {
        var request = LinearQueries.TeamStates("team-1");
        Assert.Equal("team-1", request.Variables["id"]);
        Assert.Contains("states", request.Query);
        Assert.Contains("type", request.Query);
        Assert.Contains("position", request.Query);
    }

    [Fact]
    public void Update_state_query_is_a_mutation_with_the_state_id_in_the_input()
    {
        var request = LinearQueries.UpdateIssueState("issue-1", "state-9");
        Assert.StartsWith("mutation", request.Query);
        Assert.Contains("issueUpdate", request.Query);
        Assert.Equal("issue-1", request.Variables["id"]);
        var input = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(request.Variables["input"]);
        Assert.Equal("state-9", input["stateId"]);
    }

    [Fact]
    public void Mapper_reads_states_and_skips_nodes_without_an_id()
    {
        var states = LinearMapper.ParseStates(Parse(StatesJson));
        Assert.Equal(5, states.Count);
        Assert.Equal(LinearStateType.Started, states[1].Type);
        Assert.Equal(2, states[1].Position);
        Assert.DoesNotContain(states, s => s.Name == "Broken");
    }

    [Fact]
    public void Mapper_returns_nothing_for_a_missing_connection()
    {
        Assert.Empty(LinearMapper.ParseStates(null));
        Assert.Empty(LinearMapper.ParseStates(Parse("{}")));
    }

    [Fact]
    public void Done_state_is_the_completed_state_with_the_lowest_position()
    {
        var states = LinearMapper.ParseStates(Parse(StatesJson));
        Assert.Equal("Done", LinearMapper.PickDoneState(states)!.Name);
    }

    [Fact]
    public void Done_state_is_null_when_the_team_has_no_completed_state()
    {
        var states = new[] { new LinearState("a", "Todo", LinearStateType.Unstarted, 1) };
        Assert.Null(LinearMapper.PickDoneState(states));
    }

    [Fact]
    public async Task Client_loads_team_states()
    {
        var handler = new FakeHandler().Enqueue(HttpStatusCode.OK, "{\"data\":{\"team\":{\"states\":" + StatesJson + "}}}");
        var states = await Client(handler).GetTeamStatesAsync("team-1");
        Assert.Equal(5, states.Count);
        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal("team-1", body.RootElement.GetProperty("variables").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Client_updates_the_state_and_returns_the_issue()
    {
        var response = "{\"data\":{\"issueUpdate\":{\"success\":true,\"issue\":" + Fx.IssueJson("1", "ENG-1", "Done", "completed", "\"2026-11-20T09:00:00.000Z\"") + "}}}";
        var handler = new FakeHandler().Enqueue(HttpStatusCode.OK, response);
        var issue = await Client(handler).UpdateIssueStateAsync("issue-1", "s3");

        Assert.True(issue.IsDone);
        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var vars = body.RootElement.GetProperty("variables");
        Assert.Equal("issue-1", vars.GetProperty("id").GetString());
        Assert.Equal("s3", vars.GetProperty("input").GetProperty("stateId").GetString());
    }

    [Fact]
    public async Task Client_reports_a_failed_update_and_validates_input()
    {
        var failed = new FakeHandler().Enqueue(HttpStatusCode.OK, "{\"data\":{\"issueUpdate\":{\"success\":false,\"issue\":null}}}");
        await Assert.ThrowsAsync<LinearApiException>(() => Client(failed).UpdateIssueStateAsync("i", "s"));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(new FakeHandler()).UpdateIssueStateAsync(" ", "s"));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(new FakeHandler()).UpdateIssueStateAsync("i", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => Client(new FakeHandler()).GetTeamStatesAsync(" "));
    }
}
