namespace Cshubben.Core.Features.Stats;

/// <summary>Deterministic fictional history for screenshots and for working without a GitHub token.</summary>
public static class DemoStats
{
    private static readonly string[] Repos = { "acme/api", "acme/web", "acme/api", "acme/mobile", "acme/infra", "acme/web", "acme/docs" };
    private static readonly int[] CycleHours = { 2, 4, 6, 9, 14, 20, 26, 31, 44, 58, 75, 120 };
    private static readonly int[] LineChoices = { 12, 25, 40, 80, 130, 190, 260, 420, 640, 880, 1500 };
    private static readonly string[] Titles =
    {
        "Add retry budget to the HTTP client", "Fix focus trap in dialogs", "Stream invoice export", "Split state per environment",
        "Cache listing queries", "Dark mode for settings", "Offline queue for reports", "Document webhook retries",
        "Bump serializer", "Rate limit per API key", "Audit log for role changes", "Replace date library",
    };

    /// <summary>About three months of history ending at <paramref name="now"/>. The same <paramref name="now"/> always gives the same data.</summary>
    public static StatsInput Build(DateTimeOffset now, string viewer = "alex")
    {
        var rng = new Lcg(20261120);
        var merged = new List<MergedPrRecord>();
        for (var i = 0; i < 64; i++)
        {
            var mergedAt = now - TimeSpan.FromHours(6 + i * 31.5 + rng.Next(0, 20));
            var cycle = CycleHours[rng.Next(0, CycleHours.Length)] + rng.Next(0, 3);
            var lines = LineChoices[rng.Next(0, LineChoices.Length)];
            // Bigger changes take longer.
            if (lines > 400) cycle += 18;
            var opened = mergedAt - TimeSpan.FromHours(cycle);
            DateTimeOffset? firstReview = null;
            if (rng.Next(0, 10) != 0)
            {
                var share = 0.15 + rng.Next(0, 30) / 100.0;
                firstReview = opened + TimeSpan.FromHours(Math.Max(0.4, cycle * share));
            }

            var additions = lines * 3 / 4;
            merged.Add(new MergedPrRecord(
                Repos[rng.Next(0, Repos.Length)],
                300 + 400 - i * 5 + rng.Next(0, 4),
                Titles[i % Titles.Length],
                viewer,
                opened,
                mergedAt,
                firstReview,
                additions,
                lines - additions,
                1 + lines / 60));
        }

        var reviews = new List<ReviewRecord>();
        for (var i = 0; i < 90; i++)
        {
            var at = now - TimeSpan.FromHours(3 + i * 22.5 + rng.Next(0, 12));
            reviews.Add(new ReviewRecord(Repos[rng.Next(0, Repos.Length)], 200 + rng.Next(0, 300), at));
        }

        return new StatsInput(merged, reviews);
    }

    private sealed class Lcg(ulong seed)
    {
        private ulong _state = seed;

        public int Next(int minInclusive, int maxExclusive)
        {
            _state = unchecked(_state * 6364136223846793005UL + 1442695040888963407UL);
            var value = (int)((_state >> 33) % (ulong)(maxExclusive - minInclusive));
            return minInclusive + value;
        }
    }
}
