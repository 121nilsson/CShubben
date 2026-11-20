using System.Text.Json;
using Cshubben.Core.Model;

namespace Cshubben.Core.Data.GitHub;

/// <summary>Pure derivation of <see cref="ReviewInfo"/> from the review-info query.</summary>
public static class ReviewInfoMapper
{
    /// <summary>
    /// Derives the viewer's relation to one PR. Returns null when the node has no id.
    /// RequestedDirectly: the viewer was asked as a person (pending request or request event), not only via a team.
    /// The viewer's review is the one with the newest submittedAt; DISMISSED and PENDING reviews do not count.
    /// Missing commit time becomes DateTimeOffset.MinValue so it never counts as "updated since review".
    /// </summary>
    public static ReviewInfo? FromNode(JsonElement node, string viewer)
    {
        var id = J.Str(node, "id");
        if (id.Length == 0) return null;

        var direct = false;
        foreach (var r in J.Items(node, "reviewRequests", "nodes"))
        {
            if (IsViewer(J.StrOrNull(r, "requestedReviewer", "login"), viewer)) direct = true;
        }
        foreach (var ev in J.Items(node, "timelineItems", "nodes"))
        {
            if (IsViewer(J.StrOrNull(ev, "requestedReviewer", "login"), viewer)) direct = true;
        }

        var found = false;
        DateTimeOffset? bestAt = null;
        var bestState = string.Empty;
        foreach (var review in J.Items(node, "latestReviews", "nodes"))
        {
            if (!IsViewer(J.StrOrNull(review, "author", "login"), viewer)) continue;
            var state = J.Str(review, "state");
            if (state.Length == 0 || state == "DISMISSED" || state == "PENDING") continue;
            var at = J.Time(review, "submittedAt");
            var newer = !found || (at is not null && (bestAt is null || at.Value > bestAt.Value));
            if (newer)
            {
                found = true;
                bestAt = at;
                bestState = state;
            }
        }

        var decision = bestState switch
        {
            "APPROVED" => ReviewDecision.Approved,
            "CHANGES_REQUESTED" => ReviewDecision.ChangesRequested,
            _ => ReviewDecision.None,
        };

        DateTimeOffset lastCommit = DateTimeOffset.MinValue;
        var commits = J.Items(node, "commits", "nodes");
        if (commits.Count > 0)
        {
            var t = J.Time(commits[0], "commit", "committedDate");
            if (t is not null) lastCommit = t.Value;
        }

        return new ReviewInfo(id, direct, found ? bestAt : null, lastCommit, decision);
    }

    /// <summary>Maps every node of a data.nodes answer, keyed by PR id.</summary>
    public static Dictionary<string, ReviewInfo> FromNodes(JsonElement data, string viewer)
    {
        var map = new Dictionary<string, ReviewInfo>();
        foreach (var node in J.Items(data, "nodes"))
        {
            var info = FromNode(node, viewer);
            if (info is not null) map[info.PrId] = info;
        }
        return map;
    }

    private static bool IsViewer(string? login, string viewer) =>
        !string.IsNullOrEmpty(login) && string.Equals(login, viewer, StringComparison.OrdinalIgnoreCase);
}
