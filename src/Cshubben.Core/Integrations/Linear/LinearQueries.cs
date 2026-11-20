using System.Globalization;

namespace Cshubben.Core.Integrations.Linear;

/// <summary>Pure builders for GraphQL requests. No network here.</summary>
public static class LinearQueries
{
    public const string IssueFields =
        "id identifier title url priority updatedAt completedAt " +
        "state { name type } assignee { name displayName } team { key } project { name } labels { nodes { name } }";

    public static LinearRequest Viewer() => new("query { viewer { id name email } }", Vars());

    /// <summary>Open issues assigned to me (not completed or canceled), newest update first.</summary>
    public static LinearRequest MyIssues(int first, string? after) => new(
        "query($first: Int!, $after: String) { viewer { assignedIssues(first: $first, after: $after, " +
        "filter: { state: { type: { nin: [\"completed\", \"canceled\"] } } }, orderBy: updatedAt) " +
        "{ nodes { " + IssueFields + " } pageInfo { hasNextPage endCursor } } } }",
        Vars(("first", first), ("after", after)));

    /// <summary>Issues assigned to me that were completed since <paramref name="since"/>.</summary>
    public static LinearRequest RecentlyCompleted(DateTimeOffset since, int first)
    {
        // The timestamp is generated here in a fixed format, so it is safe to put in the query text.
        var literal = since.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return new LinearRequest(
            "query($first: Int!) { viewer { assignedIssues(first: $first, " +
            "filter: { completedAt: { gte: \"" + literal + "\" } }, orderBy: updatedAt) " +
            "{ nodes { " + IssueFields + " } pageInfo { hasNextPage endCursor } } } }",
            Vars(("first", first)));
    }

    /// <summary>Linear accepts the human identifier (ENG-123) as the issue id.</summary>
    public static LinearRequest IssueByIdentifier(string identifier) => new(
        "query($id: String!) { issue(id: $id) { " + IssueFields + " } }",
        Vars(("id", identifier)));

    public static LinearRequest Teams(int first, string? after) => new(
        "query($first: Int!, $after: String) { teams(first: $first, after: $after) " +
        "{ nodes { id key name } pageInfo { hasNextPage endCursor } } }",
        Vars(("first", first), ("after", after)));

    public static LinearRequest TeamLabels(string teamId) => new(
        "query($id: String!) { team(id: $id) { labels(first: 100) { nodes { id name } } } }",
        Vars(("id", teamId)));

    public static LinearRequest CreateIssue(string teamId, string title, string? description, int? priority, IReadOnlyList<string>? labelIds)
    {
        var input = new Dictionary<string, object?>
        {
            ["teamId"] = teamId,
            ["title"] = title,
        };
        if (!string.IsNullOrWhiteSpace(description)) input["description"] = description;
        if (priority is int p) input["priority"] = p;
        if (labelIds is not null && labelIds.Count > 0) input["labelIds"] = labelIds.ToArray();
        return new LinearRequest(
            "mutation($input: IssueCreateInput!) { issueCreate(input: $input) { success issue { " + IssueFields + " } } }",
            Vars(("input", input)));
    }

    /// <summary>Attaches a pull request URL to an issue. Linear de-duplicates by issue and URL.</summary>
    public static LinearRequest LinkPullRequest(string issueId, string url, string title)
    {
        var input = new Dictionary<string, object?>
        {
            ["issueId"] = issueId,
            ["url"] = url,
            ["title"] = title,
        };
        return new LinearRequest(
            "mutation($input: AttachmentCreateInput!) { attachmentCreate(input: $input) { success attachment { id url } } }",
            Vars(("input", input)));
    }

    private static IReadOnlyDictionary<string, object?> Vars(params (string Name, object? Value)[] pairs)
    {
        var map = new Dictionary<string, object?>();
        foreach (var pair in pairs)
        {
            if (pair.Value is not null) map[pair.Name] = pair.Value;
        }

        return map;
    }
}
