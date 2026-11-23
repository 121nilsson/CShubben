using Cshubben.Core.Features.Notes;
using Cshubben.Core.Features.Cache;
using Cshubben.Core.Features.Settings;
using Cshubben.Core.Logic;
using Cshubben.Core.Model;
using Cshubben.Core.Shared;
using Cshubben.Services;
using Cshubben.Ui.Palette;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using FailureInfo = Cshubben.Core.Shared.Failure;
using static Microsoft.UI.Reactor.Factories;

namespace Cshubben.Ui;

/// <summary>
/// Window shell: sidebar (built from <see cref="PageRegistry"/>), current page and (when a PR is selected) the
/// inspector on the right. Owns the data: the current snapshot, loading/error state and the background refresh.
/// </summary>
sealed class AppRoot : Component
{
    private const int TickSeconds = 15;

    public override Element Render()
    {
        var services = AppServices.Current;

        // Navigation state.
        var (page, setPage) = UseState(AppOptions.StartPage);
        var (selectedId, setSelected) = UseState<string?>(AppOptions.SelectedPr);
        var (tab, setTab) = UseState(AppOptions.StartTab);
        var (windowWidth, _) = UseWindowSize();

        // Data state. Setters may be called from background threads, hence threadSafe.
        var (snapshot, setSnapshot) = UseState<HubSnapshot?>(services.InitialSnapshot, true);
        var (error, setError) = UseState<FailureInfo?>(null, true);
        var (notice, setNotice) = UseState<string?>(null, true);
        var (warning, setWarning) = UseState<string?>(null, true);
        var (refreshing, setRefreshing) = UseState(false, true);
        var (now, setNow) = UseState(MinuteOf(DateTimeOffset.UtcNow), true);
        var (settings, setSettings) = UseState(services.Settings);

        UseEffect(() => PagePrefetch.RecordVisit(page), page);

        var inFlight = UseRef(false);
        // Pane state. The wanted value is read through a ref so callbacks from an earlier render (WinUI reports our own writes back
        // through them) never mistake it for a user toggle; toggles only count once the pane has loaded.
        var paneDesired = UseRef(true);
        var paneReady = UseRef(false);
        var paneHooked = UseRef(false);
        var pausedUntil = UseRef<DateTimeOffset?>(null);
        var windowActive = UseRef(true);
        var intervalSeconds = UseRef(AppSettings.DefaultRefreshSeconds);
        var isActive = UseIsActive();
        windowActive.Current = isActive;
        intervalSeconds.Current = settings.RefreshSeconds;

        async Task RefreshAsync()
        {
            if (inFlight.Current) return;
            inFlight.Current = true;
            setRefreshing(true);
            try
            {
                var data = await services.GetHubDataAsync();
                var loaded = await data.LoadAsync();
                setSnapshot(loaded);
                if (!services.IsDemo) _ = Task.Run(() => SnapshotCache.TrySave(AppPaths.DataDirectory, loaded));
                IntegrationWiring.AttachGitHub(services);
                NotificationsHub.OnSnapshot(loaded, services.Settings, services.IsDemo);
                BackgroundWatch.Tick(loaded, services.Settings, services.IsDemo);
                PagePrefetch.Tick(loaded, services);
                setError(null);
                pausedUntil.Current = null;
                setNotice(services.DemoReason is { } reason ? "Showing demo data. " + reason : null);
                var warnings = services.GitHub?.LastWarnings;
                setWarning(warnings is { Count: > 0 }
                    ? "Some data may be incomplete: " + string.Join(" ", warnings.Take(2))
                    : null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Keep whatever is on screen; the banner says what happened and what to do.
                var failure = ErrorText.Classify(ex);
                setError(failure);
                // While rate limited, do not let the background timer hammer the API; a manual Retry still works.
                pausedUntil.Current = failure.Kind == Cshubben.Core.Shared.FailureKind.RateLimited ? failure.RetryAt : null;
            }
            finally
            {
                inFlight.Current = false;
                setRefreshing(false);
                setNow(MinuteOf(DateTimeOffset.UtcNow));
            }
        }

        void Refresh() => _ = RefreshAsync();

        // First load (live mode only; --demo already has its snapshot).
        UseEffect(() =>
        {
            if (!services.ForcedDemo) Refresh();
            return () => { };
        }, new object[0]);

        // Background refresh while the window is active, and a slow tick so "Updated 2 min ago" stays true.
        UseEffect(() =>
        {
            var cts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                var elapsed = 0;
                try
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(TickSeconds));
                    while (await timer.WaitForNextTickAsync(cts.Token))
                    {
                        elapsed += TickSeconds;
                        setNow(MinuteOf(DateTimeOffset.UtcNow));
                        if (elapsed >= intervalSeconds.Current)
                        {
                            elapsed = 0;
                            if (windowActive.Current && !services.ForcedDemo
                                && !(pausedUntil.Current is { } until && until > DateTimeOffset.UtcNow))
                            {
                                await RefreshAsync();
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Component unmounted.
                }
            });
            return () => cts.Cancel();
        }, new object[0]);

        void SaveSettings(AppSettings next)
        {
            var clean = next.Sanitized();
            try
            {
                services.SettingsStore.Save(clean);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                setError(new FailureInfo(Cshubben.Core.Shared.FailureKind.Unknown, "Settings", "Could not save settings",
                    ex.Message + " Check that the data folder is writable (Settings, Run checks).", true, false, null));
            }

            services.Settings = clean;
            setSettings(clean);
        }

        // Responsive: on narrow windows the sidebar becomes an icon rail and the inspector gets slimmer,
        // so the page in the middle keeps enough room for its rows.
        // The toggle in the sidebar header overrides the automatic choice everywhere, including the review page.
        var compact = settings.SidebarCollapsed ?? (windowWidth < 1200 || page == Page.Review);
        paneDesired.Current = !compact;
        // Below this width the inspector replaces the list as a full-page view with a Back button.
        var narrow = windowWidth < 900;
        var inspectorWidth = windowWidth >= 1500 ? 480.0 : windowWidth >= 1200 ? 420.0 : 340.0;

        var loadedOnce = snapshot is not null;
        var snap = snapshot ?? AppServices.EmptySnapshot;
        // The review page uses the full window, so the inspector is hidden while it is open.
        var selected = page == Page.Review ? null : snap.Find(selectedId);
        Inspector.OpenReview = id => { setSelected(id); setPage(Page.Review); };
        void SetInspectorTab(int t)
        {
            if (t == 2 && snap.Find(selectedId) is { } pr) { setSelected(pr.Id); setPage(Page.Review); }
            else setTab(t);
        }

        void Select(string id) { setSelected(id); setTab(0); }
        NotificationsHub.RefreshRequested = Refresh;
        Pages.NotesPage.OpenPullRequest = (repo, number) =>
        {
            var target = Cshubben.Core.Features.Notes.NoteReferences.FindPullRequest(snap.All, repo, number);
            if (target is null) return false;
            NotificationsHub.SelectPr?.Invoke(target.Id);
            return true;
        };
        NotificationsHub.SelectPr = id => { setPage(snap.Find(id)?.Role == PrRole.Authored ? Page.Mine : Page.Reviews); Select(id); };
        PipelineServices.Merged = _ => Refresh();

        void OpenSettings()
        {
            setPage(Page.Settings);
            if (narrow) setSelected(null);
        }

        var ctx = new PageContext(snap, loadedOnce, selectedId, Select, services, settings, SaveSettings, Refresh, OpenSettings);

        // Sidebar: the platform NavigationView, built from the registry. Entries are grouped by section in registry order;
        // pinned ones (Settings) use the built-in settings item at the bottom.
        var menu = new List<NavigationViewItemData>();
        var badges = new Dictionary<string, PageBadge>();
        string? currentSection = null;
        foreach (var def in PageRegistry.All)
        {
            if (def.Pinned || !def.IsVisible(settings)) continue;
            if (def.Section != currentSection)
            {
                currentSection = def.Section;
                if (!string.IsNullOrEmpty(def.Section)) menu.Add(NavItemHeader(def.Section));
            }

            menu.Add(new NavigationViewItemData(def.Label, null, def.Id.ToString()) { IconElement = FontIcon(def.Glyph) });
            if (def.Badge?.Invoke(ctx) is { Count: > 0 } badge) badges[def.Id.ToString()] = badge;
        }

        // The platform InfoBadge on each menu item. Reactor's NavItem data has no badge slot, so it is attached to the live
        // items here (setters run after the menu is reconciled); the same badge instance is reused and only its value changes.
        void ApplyBadges(WinUI.NavigationView nav)
        {
            foreach (var item in nav.MenuItems.OfType<WinUI.NavigationViewItem>())
            {
                if (item.Tag is not string tag || !badges.TryGetValue(tag, out var b))
                {
                    item.InfoBadge = null;
                    continue;
                }

                var badge = item.InfoBadge ?? new WinUI.InfoBadge();
                badge.Value = b.Count;
                var styleKey = b.Tone switch
                {
                    Tone.Warning => "CautionValueInfoBadgeStyle",
                    Tone.Danger => "CriticalValueInfoBadgeStyle",
                    Tone.Success => "SuccessValueInfoBadgeStyle",
                    Tone.Accent => "AttentionValueInfoBadgeStyle",
                    _ => "InformationalValueInfoBadgeStyle",
                };
                if (Application.Current.Resources.TryGetValue(styleKey, out var resource) && resource is Style style)
                    badge.Style = style;
                item.InfoBadge = badge;
            }
        }

        // WinUI may reset IsPaneOpen while it loads; once it has, put the wanted value back and start listening for user toggles.
        void HookPane(WinUI.NavigationView nav)
        {
            if (paneHooked.Current) return;
            paneHooked.Current = true;
            nav.Loaded += (_, _) => nav.DispatcherQueue.TryEnqueue(() =>
            {
                nav.IsPaneOpen = paneDesired.Current;
                paneReady.Current = true;
            });
        }

        void Navigate(string? tag)
        {
            if (tag == NavigationViewElement.SettingsTag) { OpenSettings(); return; }
            if (tag is not null && Enum.TryParse<Page>(tag, true, out var pg)) { setPage(pg); if (narrow) setSelected(null); }
        }

        var sidebar = NavigationView(menu.ToArray())
            .PaneDisplayMode(WinUI.NavigationViewPaneDisplayMode.Left)
            .PaneTitle("Cshubben")
            .OpenPaneLength(248)
            .BackButtonVisible(false)
            .TitleBarAutoPadding(false)
            .IsPaneOpen(!compact, open =>
            {
                // Only a real toggle by the user is saved (and ends the automatic mode). Echoes of our own writes and WinUI's
                // own changes while the pane loads are ignored.
                if (paneReady.Current && open != paneDesired.Current)
                    SaveSettings(services.Settings with { SidebarCollapsed = !open });
            })
            .PaneFooter(RefreshFooter(compact, snapshot, now, refreshing, services.IsDemo, Refresh))
            .ItemInvoked(Navigate)
            .Set(nav => ApplyBadges(nav))
            .Set(nav => HookPane(nav));
        sidebar = sidebar with { SelectedTag = page == Page.Settings ? NavigationViewElement.SettingsTag : page.ToString() };

        var pageDef = PageRegistry.Find(page);
        Element content = pageDef is null
            ? Kit.ComingSoon(page.ToString(), "This page is not registered.")
            : !pageDef.IsVisible(settings)
                ? Kit.PageOff(pageDef.Label, OpenSettings)
            : pageDef.NeedsData && !loadedOnce
                // Never an eternal spinner: if the first load failed, say why and offer Retry / Settings.
                ? (error is not null
                    ? FailureView.Panel(error, Refresh, OpenSettings)
                    : Kit.LoadingState("Loading your pull requests..."))
                : pageDef.Build(ctx);

        // Banners never replace the page: stale data stays visible under an error.
        var banners = new List<Element>();
        // The full-page failure panel already says it all, so no banner on top of it.
        var panelShown = pageDef is { NeedsData: true } && !loadedOnce;
        if (error is not null && !panelShown)
        {
            banners.Add(loadedOnce
                ? FailureView.Strip(FailureView.ToneOf(error), error.Title + " (showing the last data)", FailureView.MessageOf(error),
                    FailureView.Buttons(error, Refresh, OpenSettings))
                : FailureView.Banner(error, Refresh, OpenSettings));
        }

        if (notice is not null)
            banners.Add(FailureView.Strip(Tone.Accent, "Demo data", notice,
                Button("Open Settings", OpenSettings).AutomationName("Open Settings"),
                Button("Retry", Refresh).AutomationName("Retry")));
        if (warning is not null && error is null)
            banners.Add(Kit.Banner(Tone.Warning, "Partial data", warning));

        var bannerStack = banners.Count == 0
            ? Empty()
            : VStack(8, banners.ToArray()).Margin(12, 12, 12, 0);

        var main = Border(Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Auto, GridSize.Star()],
                bannerStack.Grid(row: 0),
                content.Grid(row: 1)))
            .Margin(0);

        Element content2 = selected is not null && narrow
            ? Inspector.Build(selected, snap, tab, SetInspectorTab, () => setSelected(null), fullPage: true)
            : selected is null
            ? main
            : Grid(columns: [GridSize.Star(), GridSize.Px(inspectorWidth)], rows: [GridSize.Star()],
                main.Grid(column: 0),
                Inspector.Build(selected, snap, tab, SetInspectorTab, () => setSelected(null)).Grid(column: 1));
        Element grid = sidebar with { Content = content2 };

        var paletteCtx = new PaletteContext(snap, selectedId,
            PageRegistry.All.Where(d => d.Id != Page.Review && d.IsVisible(settings)).Select(d => new PalettePage(d.Id.ToString(), d.Label, d.Glyph)).ToList(),
            key => { if (Enum.TryParse<Page>(key, true, out var pg)) { setPage(pg); if (narrow) setSelected(null); } },
            id => Select(id), Refresh, () => setPage(Page.Notes), () => setPage(Page.Settings));

        return FlexColumn(TitleBar("Cshubben").Flex(shrink: 0),
                CommandPalette.Host(paletteCtx, grid).Flex(grow: 1, basis: 0),
                NotificationsHub.TrayElement().Flex(shrink: 0))
            .RequestedTheme(ThemeOf(AppOptions.Theme ?? settings.Theme))
            .Backdrop(BackdropKind.Mica);
    }

    /// <summary>Refresh button plus "Updated 2 min ago" (and a "Demo data" hint) at the bottom of the sidebar.</summary>
    private static Element RefreshFooter(bool compact, HubSnapshot? snapshot, DateTimeOffset now, bool refreshing, bool demo, Action refresh)
    {
        var text = snapshot is null
            ? "Loading..."
            : Freshness.Label(snapshot.At, now);
        if (demo && snapshot is not null) text = "Demo data · " + text;

        Element icon = refreshing
            ? ProgressRing().IsActive().Width(16).Height(16)
            : Kit.Glyph("");
        var button = Button(icon, refresh)
            .SubtleButton().IsEnabled(!refreshing)
            .ToolTip(text + ". Click to refresh.").AutomationName("Refresh");

        if (compact) return button.HAlign(HorizontalAlignment.Center).Margin(0, 4, 0, 4);

        return Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            Kit.Muted(text).TextTrimming(TextTrimming.CharacterEllipsis).VAlign(VerticalAlignment.Center).Grid(column: 0),
            button.Grid(column: 1)).Margin(12, 4, 4, 4);
    }

    private static DateTimeOffset MinuteOf(DateTimeOffset t) =>
        new(t.UtcTicks - t.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero);

    private static ElementTheme ThemeOf(string theme) => ThemeChoice.Normalize(theme) switch
    {
        ThemeChoice.Light => ElementTheme.Light,
        ThemeChoice.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };
}
