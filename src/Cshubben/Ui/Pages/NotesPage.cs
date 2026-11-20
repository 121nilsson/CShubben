using Cshubben.Core.Logic;
using Cshubben.Core.Features.Notes;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using MarkdownFactory = Microsoft.UI.Reactor.Advanced.Factories;

namespace Cshubben.Ui.Pages;

/// <summary>
/// Notes: a searchable list on the left and a markdown editor with links and attachments on the right.
/// The page owns its own state; the store comes from <see cref="Store"/>.
/// </summary>
sealed class NotesPage : Component
{
    /// <summary>The store the page reads and writes. The app assigns the real one at startup.</summary>
    public static INoteStore Store { get; set; } = InMemoryNoteStore.Demo();

    /// <summary>Id of the note to select when the page first shows (for screenshots).</summary>
    public static string? InitialSelection { get; set; }

    /// <summary>Called with a local file path when an attachment is clicked. Null means do nothing.</summary>
    public static Action<string>? OpenPath { get; set; }

    public override Element Render()
    {
        var store = Store;
        var (windowWidth, _) = UseWindowSize();
        var narrow = windowWidth < 900;
        var now = DateTimeOffset.UtcNow;

        var (tick, setTick) = UseState(0);
        var loadError = "";
        IReadOnlyList<Note> notes = Array.Empty<Note>();
        try
        {
            notes = store.ListAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            loadError = ex.Message;
        }

        var initial = notes.FirstOrDefault(n => n.Id == InitialSelection);
        var (selectedId, setSelectedId) = UseState<string?>(initial?.Id);
        var (query, setQuery) = UseState("");
        var (tagFilter, setTagFilter) = UseState<string?>(null);
        var (preview, setPreview) = UseState(initial is not null && initial.Body.Length > 0);
        var (title, setTitle) = UseState(initial?.Title ?? "");
        var (body, setBody) = UseState(initial?.Body ?? "");
        var (tagsText, setTagsText) = UseState(initial is null ? "" : string.Join(", ", initial.Tags));
        var (confirmDelete, setConfirmDelete) = UseState(false);
        var (error, setError) = UseState("");

        var selected = notes.FirstOrDefault(n => n.Id == selectedId);
        var allTags = NoteSearch.AllTags(notes);
        var activeTag = tagFilter is not null && allTags.Contains(tagFilter, StringComparer.OrdinalIgnoreCase) ? tagFilter : null;
        var visible = NoteSearch.Filter(notes, query, activeTag);

        void Guard(Action action)
        {
            try
            {
                action();
                setError("");
            }
            catch (Exception ex)
            {
                setError(ex.Message);
            }
            setTick(tick + 1);
        }

        void LoadDraft(Note n)
        {
            setSelectedId(n.Id);
            setTitle(n.Title);
            setBody(n.Body);
            setTagsText(string.Join(", ", n.Tags));
            setPreview(n.Body.Length > 0);
            setConfirmDelete(false);
        }

        void NewNote()
        {
            var note = Note.Create("New note", "", null, null, false, DateTimeOffset.UtcNow);
            Guard(() => store.SaveAsync(note).GetAwaiter().GetResult());
            setQuery("");
            setTagFilter(null);
            LoadDraft(note);
            setPreview(false);
        }

        void Save()
        {
            if (selected is null) return;
            var trimmed = title.Trim();
            var updated = selected with
            {
                Title = trimmed.Length == 0 ? "Untitled" : trimmed,
                Body = body,
                Tags = NoteSearch.ParseTags(tagsText),
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            Guard(() => store.SaveAsync(updated).GetAwaiter().GetResult());
            setTitle(updated.Title);
        }

        void TogglePin()
        {
            if (selected is null) return;
            var updated = selected with { Pinned = !selected.Pinned };
            Guard(() => store.SaveAsync(updated).GetAwaiter().GetResult());
        }

        void Delete()
        {
            if (selected is null) return;
            var id = selected.Id;
            Guard(() => store.DeleteAsync(id).GetAwaiter().GetResult());
            setSelectedId(null);
            setConfirmDelete(false);
        }

        void OpenAttachment(string noteId, string fileName)
        {
            Guard(() =>
            {
                var path = store.OpenAttachmentPathAsync(noteId, fileName).GetAwaiter().GetResult();
                OpenPath?.Invoke(path);
            });
        }

        var dirty = selected is not null
            && (title != selected.Title
                || body != selected.Body
                || !NoteSearch.ParseTags(tagsText).SequenceEqual(selected.Tags));

        var shownError = error.Length > 0 ? error : loadError;

        var left = BuildList(notes, visible, allTags, activeTag, selected, now, query, setQuery, setTagFilter, LoadDraft, NewNote, shownError);
        Action? back = narrow ? () => { setSelectedId(null); setConfirmDelete(false); } : null;
        var right = selected is null
            ? BuildEmpty(NewNote)
            : BuildEditor(selected, title, setTitle, body, setBody, tagsText, setTagsText, preview, setPreview, dirty,
                confirmDelete, setConfirmDelete, now, Save, TogglePin, Delete, OpenAttachment, back);

        // Narrow windows: one pane at a time (list, or the editor with a Back button).
        if (narrow)
            return selected is null ? left : right;

        return Grid(
            columns: [GridSize.Px(300), GridSize.Star()],
            rows: [GridSize.Star()],
            left.Grid(column: 0),
            right.Grid(column: 1));
    }

    // ------------------------------------------------------------------ left pane

    private static Element BuildList(
        IReadOnlyList<Note> all,
        IReadOnlyList<Note> visible,
        IReadOnlyList<string> allTags,
        string? activeTag,
        Note? selected,
        DateTimeOffset now,
        string query,
        Action<string> setQuery,
        Action<string?> setTagFilter,
        Action<Note> select,
        Action newNote,
        string error)
    {
        var header = Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            VStack(2, Title("Notes"), Kit.Muted(all.Count == 1 ? "1 note" : $"{all.Count} notes")).Grid(column: 0),
            Button(HStack(6, Kit.Glyph("", 14), TextBlock("New note")), newNote)
                .AccentButton().VAlign(VerticalAlignment.Top).AutomationName("New note").Grid(column: 1));

        var search = TextBox(query, setQuery, placeholderText: "Search notes");

        Element? chips = null;
        if (allTags.Count > 0)
        {
            var items = allTags.Select(t => TagChip(t, string.Equals(t, activeTag, StringComparison.OrdinalIgnoreCase), setTagFilter)).ToArray();
            chips = FlexRow(items) with { Wrap = FlexWrap.Wrap, ColumnGap = 4, RowGap = 4 };
        }

        Element rows;
        if (visible.Count == 0)
        {
            rows = Kit.EmptyNote(all.Count == 0 ? "No notes yet. Create the first one." : "No notes match your search.");
        }
        else
        {
            var items = visible.Select(n => NoteRow(n, selected is not null && n.Id == selected.Id, now, () => select(n))).ToArray();
            rows = FlexColumn(items) with { RowGap = 2 };
        }

        Element? errorLine = error.Length > 0 ? Kit.Muted(error).Foreground(Kit.Fg(Tone.Danger)) : null;

        var column = FlexColumn(
            header,
            search,
            chips,
            errorLine,
            ScrollView(rows).Flex(grow: 1, basis: 0)) with { RowGap = 12 };
        return column.FlexPadding(24, 16, 16, 16);
    }

    private static Element TagChip(string tag, bool active, Action<string?> setTagFilter) =>
        ToggleButton(TextBlock("#" + tag).FontSize(12), active, on => setTagFilter(on ? tag : null))
            .Padding(8, 2)
            .MinHeight(24)
            .WithKey(tag);

    private static Element NoteRow(Note n, bool selected, DateTimeOffset now, Action onClick)
    {
        var name = string.IsNullOrWhiteSpace(n.Title) ? "Untitled" : n.Title;

        Element pin;
        if (n.Pinned) pin = Kit.Glyph("", 12).Foreground(Theme.AccentText).Margin(0, 0, 6, 0).VAlign(VerticalAlignment.Center);
        else pin = Empty();

        var head = Grid(
            columns: [GridSize.Auto, GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            pin.Grid(column: 0),
            TextBlock(name).SemiBold().TextTrimming(TextTrimming.CharacterEllipsis).Grid(column: 1),
            Kit.Muted(Age.Format(n.UpdatedAt, now)).Margin(8, 0, 0, 0).VAlign(VerticalAlignment.Center).Grid(column: 2));

        var firstLine = NoteSearch.Preview(n.Body, 90);
        Element? previewLine = null;
        if (firstLine.Length > 0)
            previewLine = Kit.Muted(firstLine).TextTrimming(TextTrimming.CharacterEllipsis);

        Element? tagRow = null;
        if (n.Tags.Count > 0)
            tagRow = HStack(4, n.Tags.Take(3).Select(t => Kit.Pill(t, Tone.Neutral)).ToArray());

        var button = Button(VStack(4, head, previewLine, tagRow), onClick)
            .SubtleButton()
            .HAlign(HorizontalAlignment.Stretch)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .AutomationName(name);

        return Border(button)
            .Selected(selected)
            .CornerRadius(6)
            .WithKey(n.Id);
    }

    // ------------------------------------------------------------------ right pane

    private static Element BuildEmpty(Action newNote)
    {
        var content = VStack(8,
            Kit.Glyph("", 32).Foreground(Theme.TertiaryText).HAlign(HorizontalAlignment.Center),
            BodyStrong("No note selected").HAlign(HorizontalAlignment.Center),
            Kit.Muted("Pick a note on the left, or start a new one.").HAlign(HorizontalAlignment.Center),
            Button("New note", newNote).AccentButton().HAlign(HorizontalAlignment.Center).Margin(0, 8, 0, 0))
            .HAlign(HorizontalAlignment.Center)
            .VAlign(VerticalAlignment.Center);
        return Border(content)
            .Background(Theme.CardBackground)
            .WithBorder(Theme.CardStroke)
            .CornerRadius(8)
            .Margin(8, 16, 16, 16);
    }

    private static Element BuildEditor(
        Note note,
        string title,
        Action<string> setTitle,
        string body,
        Action<string> setBody,
        string tagsText,
        Action<string> setTagsText,
        bool preview,
        Action<bool> setPreview,
        bool dirty,
        bool confirmDelete,
        Action<bool> setConfirmDelete,
        DateTimeOffset now,
        Action save,
        Action togglePin,
        Action delete,
        Action<string, string> openAttachment,
        Action? back = null)
    {
        var titleBox = TextBox(title, setTitle, placeholderText: "Title").Set(tb => { tb.FontSize = 20; });

        var meta = Kit.Muted(RelativeTime("Updated", note.UpdatedAt, now));

        var modeBar = SelectorBar(
            new[] { SelectorBarItem("Edit"), SelectorBarItem("Preview") },
            preview ? 1 : 0,
            i => setPreview(i == 1));

        var pinButton = ToggleButton(Kit.Glyph("", 14), note.Pinned, _ => togglePin())
            .AutomationName("Pin note")
            .ToolTip(note.Pinned ? "Unpin" : "Pin to the top of the list");

        Element deleteControls;
        if (confirmDelete)
        {
            deleteControls = HStack(4,
                Button("Confirm delete", delete).Foreground(Kit.Fg(Tone.Danger)),
                Button("Cancel", () => setConfirmDelete(false)));
        }
        else
        {
            deleteControls = Button("Delete", () => setConfirmDelete(true));
        }

        Element? unsaved = dirty ? Kit.Pill("Unsaved changes", Tone.Warning) : null;

        var toolbar = FlexRow(
            modeBar,
            pinButton,
            unsaved,
            Button("Save", save).AccentButton().IsEnabled(dirty),
            deleteControls) with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 8 };

        Element bodyView;
        if (preview)
        {
            Element rendered = string.IsNullOrWhiteSpace(body)
                ? Kit.Muted("Nothing to preview yet. Switch to Edit and write something.")
                : MarkdownFactory.Markdown(body);
            bodyView = Border(rendered)
                .Padding(12)
                .MinHeight(240)
                .CornerRadius(6)
                .Background(Theme.SubtleFill);
        }
        else
        {
            bodyView = TextBox(body, setBody, placeholderText: "Write in markdown")
                .AcceptsReturn()
                .TextWrapping(TextWrapping.Wrap)
                .MinHeight(280);
        }

        Element tagsView;
        if (preview)
        {
            var pills = NoteSearch.ParseTags(tagsText).Select(t => Kit.Pill(t, Tone.Neutral)).ToArray();
            if (pills.Length == 0) tagsView = Kit.Muted("No tags");
            else tagsView = FlexRow(pills) with { Wrap = FlexWrap.Wrap, ColumnGap = 4, RowGap = 4 };
        }
        else
        {
            tagsView = TextBox(tagsText, setTagsText, placeholderText: "Tags, separated by commas");
        }

        Element? backButton = back is null
            ? null
            : Button("← Notes", back).SubtleButton().AutomationName("Back to notes").HAlign(HorizontalAlignment.Left);

        var column = FlexColumn(
            backButton,
            titleBox,
            meta,
            tagsView,
            toolbar,
            bodyView,
            LinksSection(note),
            AttachmentsSection(note, openAttachment)) with { RowGap = 12 };

        var panel = ScrollView(Border(column.FlexPadding(20, 16)).MaxWidth(900).HAlign(HorizontalAlignment.Left));
        return Border(panel)
            .Background(Theme.CardBackground)
            .WithBorder(Theme.CardStroke)
            .CornerRadius(8)
            .Margin(8, 16, 16, 16);
    }

    private static Element LinksSection(Note note)
    {
        Element content;
        if (note.Links.Count == 0)
        {
            content = Kit.Muted("No links yet.");
        }
        else
        {
            var rows = new List<Element>();
            for (var i = 0; i < note.Links.Count; i++)
                rows.Add(LinkRow(note.Links[i], i));
            content = FlexColumn(rows.ToArray()) with { RowGap = 2 };
        }
        return Section("Links", content);
    }

    private static Element LinkRow(NoteLink link, int index)
    {
        var hasLabel = !string.IsNullOrWhiteSpace(link.Label);
        var main = hasLabel ? link.Label : link.Target;
        Element? sub = hasLabel ? Kit.Muted(link.Target).TextTrimming(TextTrimming.CharacterEllipsis) : null;

        var row = Grid(
            columns: [GridSize.Px(28), GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            Kit.Glyph("", 14).VAlign(VerticalAlignment.Center).Grid(column: 0),
            VStack(2,
                TextBlock(main).TextTrimming(TextTrimming.CharacterEllipsis),
                sub).Grid(column: 1),
            Kit.Pill(KindLabel(link.Kind), Tone.Neutral).VAlign(VerticalAlignment.Center).Margin(8, 0, 0, 0).Grid(column: 2));

        return Button(row, () => { })
            .SubtleButton()
            .HAlign(HorizontalAlignment.Stretch)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .AutomationName(KindLabel(link.Kind) + " " + link.Target)
            .WithKey(index + ":" + link.Target);
    }

    private static Element AttachmentsSection(Note note, Action<string, string> open)
    {
        Element content;
        if (note.Attachments.Count == 0)
        {
            content = Kit.Muted("No files attached.");
        }
        else
        {
            var rows = new List<Element>();
            foreach (var name in note.Attachments)
            {
                var fileName = name;
                var row = Grid(
                    columns: [GridSize.Px(28), GridSize.Star()],
                    rows: [GridSize.Auto],
                    Kit.Glyph("", 14).VAlign(VerticalAlignment.Center).Grid(column: 0),
                    TextBlock(fileName).TextTrimming(TextTrimming.CharacterEllipsis).VAlign(VerticalAlignment.Center).Grid(column: 1));
                rows.Add(Button(row, () => open(note.Id, fileName))
                    .SubtleButton()
                    .HAlign(HorizontalAlignment.Stretch)
                    .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                    .AutomationName(fileName)
                    .WithKey(fileName));
            }
            content = FlexColumn(rows.ToArray()) with { RowGap = 2 };
        }
        return Section("Attachments", content);
    }

    private static Element Section(string label, Element content) =>
        VStack(6,
            TextBlock(label.ToUpperInvariant()).FontSize(11).SemiBold().Foreground(Theme.SecondaryText),
            content);

    private static string KindLabel(NoteLinkKind kind) => kind switch
    {
        NoteLinkKind.Pr => "PR",
        NoteLinkKind.Issue => "Issue",
        NoteLinkKind.Repo => "Repo",
        _ => "URL",
    };

    private static string RelativeTime(string prefix, DateTimeOffset then, DateTimeOffset now)
    {
        var age = Age.Format(then, now);
        return age == "now" ? prefix + " just now" : prefix + " " + age + " ago";
    }
}
