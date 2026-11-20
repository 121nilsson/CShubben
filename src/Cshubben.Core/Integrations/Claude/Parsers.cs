using System.Text.Json;

namespace Cshubben.Core.Integrations.Claude;

public enum BriefingItemKind { Action, Watch, Info }

public sealed record BriefingItem(BriefingItemKind Kind, string Text, string? PrUrl);

public sealed record BriefingResult(string Headline, string Summary, IReadOnlyList<BriefingItem> Items)
{
    public int ActionCount => Items.Count(i => i.Kind == BriefingItemKind.Action);
}

/// <summary>Validates and cleans the model's briefing JSON. Never trusts links: only https://github.com/ survives.</summary>
public static class BriefingParser
{
    public const int MaxItems = 7;

    public static BriefingResult? Parse(string? modelText)
    {
        if (JsonText.ParseObject(modelText) is not JsonElement root) return null;

        var headline = PromptText.OneLine(LinkFilter.StripNonGitHubLinks(JsonRead.Str(root, "headline")), 120);
        var summary = PromptText.OneLine(LinkFilter.StripNonGitHubLinks(JsonRead.Str(root, "summary")), 800);
        if (headline.Length == 0 && summary.Length == 0) return null;

        var items = new List<BriefingItem>();
        if (JsonRead.Arr(root, "items") is JsonElement array)
        {
            foreach (var element in array.EnumerateArray())
            {
                if (items.Count >= MaxItems) break;
                if (element.ValueKind != JsonValueKind.Object) continue;
                var text = PromptText.OneLine(LinkFilter.StripNonGitHubLinks(JsonRead.Str(element, "text")), 240);
                if (text.Length == 0) continue;
                var url = JsonRead.Str(element, "prUrl")?.Trim();
                items.Add(new BriefingItem(ParseKind(JsonRead.Str(element, "kind")), text, LinkFilter.IsGitHubUrl(url) ? url : null));
            }
        }

        return new BriefingResult(headline, summary, items);
    }

    private static BriefingItemKind ParseKind(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "action" => BriefingItemKind.Action,
        "watch" => BriefingItemKind.Watch,
        _ => BriefingItemKind.Info,
    };
}

/// <summary>A structured Linear issue suggestion. Priority follows Linear: 0 none, 1 urgent, 2 high, 3 medium, 4 low.</summary>
public sealed record IssueDraft(string Title, string Description, int? Priority, IReadOnlyList<string> Labels);

public static class IssueDraftParser
{
    public const int MaxLabels = 5;

    public static IssueDraft? Parse(string? modelText)
    {
        if (JsonText.ParseObject(modelText) is not JsonElement root) return null;

        var title = PromptText.OneLine(LinkFilter.StripNonGitHubLinks(JsonRead.Str(root, "title")), 120);
        if (title.Length == 0) return null;

        var description = LinkFilter.StripNonGitHubLinks(PromptText.Clean(JsonRead.Str(root, "description"))).Trim();
        if (description.Length > 8_000) description = description.Substring(0, 8_000);

        int? priority = null;
        if (root.TryGetProperty("priority", out var p))
        {
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var number)) priority = number;
            else if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var parsed)) priority = parsed;
            if (priority is < 0 or > 4) priority = null;
        }

        var labels = new List<string>();
        if (JsonRead.Arr(root, "labels") is JsonElement array)
        {
            foreach (var element in array.EnumerateArray())
            {
                if (labels.Count >= MaxLabels) break;
                if (element.ValueKind != JsonValueKind.String) continue;
                var label = PromptText.OneLine(element.GetString(), 40);
                if (label.Length > 0 && !labels.Contains(label, StringComparer.OrdinalIgnoreCase)) labels.Add(label);
            }
        }

        return new IssueDraft(title, description, priority, labels);
    }
}
