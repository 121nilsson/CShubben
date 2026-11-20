using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cshubben.Core.Features.Review;

/// <summary>Keeps drafts in memory only. Used for demo data and tests.</summary>
public sealed class InMemoryDraftStore : IDraftStore
{
    private readonly Dictionary<string, (List<PendingComment> Comments, string Summary)> _items = new(StringComparer.Ordinal);

    public IReadOnlyList<PendingComment> Load(string pullRequestId) =>
        _items.TryGetValue(pullRequestId, out var entry) ? entry.Comments.ToList() : new List<PendingComment>();

    public string LoadSummary(string pullRequestId) =>
        _items.TryGetValue(pullRequestId, out var entry) ? entry.Summary : string.Empty;

    public void Save(string pullRequestId, IReadOnlyList<PendingComment> comments, string summary)
    {
        if (comments.Count == 0 && summary.Length == 0) _items.Remove(pullRequestId);
        else _items[pullRequestId] = (comments.ToList(), summary);
    }
}

/// <summary>
/// One small JSON file per pull request in a folder. File names are a hash of the id, so ids never reach the file system.
/// A write goes to a temporary file first, so a crash cannot leave a half-written draft.
/// </summary>
public sealed class FileDraftStore : IDraftStore
{
    private readonly string _directory;

    public FileDraftStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A folder is required.", nameof(directory));
        _directory = directory;
    }

    public IReadOnlyList<PendingComment> Load(string pullRequestId) => Read(pullRequestId).Comments;

    public string LoadSummary(string pullRequestId) => Read(pullRequestId).Summary;

    public void Save(string pullRequestId, IReadOnlyList<PendingComment> comments, string summary)
    {
        var path = PathFor(pullRequestId);
        if (comments.Count == 0 && summary.Length == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        Directory.CreateDirectory(_directory);
        var json = DraftSerializer.Serialize(comments, summary);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json, Encoding.UTF8);
        File.Move(temp, path, true);
    }

    public string PathFor(string pullRequestId) => Path.Combine(_directory, FileNameFor(pullRequestId));

    public static string FileNameFor(string pullRequestId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(pullRequestId));
        return Convert.ToHexString(hash).Substring(0, 24).ToLowerInvariant() + ".json";
    }

    private DraftSerializer.Loaded Read(string pullRequestId)
    {
        var path = PathFor(pullRequestId);
        if (!File.Exists(path)) return new DraftSerializer.Loaded(new List<PendingComment>(), string.Empty);
        try
        {
            return DraftSerializer.Deserialize(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (IOException)
        {
            return new DraftSerializer.Loaded(new List<PendingComment>(), string.Empty);
        }
    }
}

/// <summary>JSON for drafts. Corrupt input yields no drafts instead of an exception.</summary>
public static class DraftSerializer
{
    public sealed record Loaded(IReadOnlyList<PendingComment> Comments, string Summary);

    private sealed class Document
    {
        public int Version { get; set; } = 1;

        public string Summary { get; set; } = string.Empty;

        public List<PendingComment> Comments { get; set; } = new();
    }

    public static string Serialize(IReadOnlyList<PendingComment> comments, string summary)
    {
        var doc = new Document { Summary = summary, Comments = comments.ToList() };
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }

    public static Loaded Deserialize(string json)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<Document>(json);
            if (doc is null) return new Loaded(new List<PendingComment>(), string.Empty);
            var valid = doc.Comments
                .Where(c => c is not null && !string.IsNullOrEmpty(c.Id) && !string.IsNullOrEmpty(c.Path) && c.Line > 0)
                .ToList();
            return new Loaded(valid, doc.Summary ?? string.Empty);
        }
        catch (JsonException)
        {
            return new Loaded(new List<PendingComment>(), string.Empty);
        }
    }
}
