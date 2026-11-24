# Cshubben – plan

A hub over **GitHub pull requests, Linear issues and Claude**, plus a place for **notes, ideas and files**.
Windows desktop, .NET 11 / C# 15, Microsoft.UI.Reactor. Sibling to My-hub (Rust/GPUI), which we port the best parts of and improve.

## Principles
1. **Glanceable, then drillable.** Open the app and know in seconds what needs you; one click to act.
2. **One obvious next step.** Every PR shows the single best action (`NextAction`) as the primary button.
3. **Silence means fine.** Colour is spent only on attention. Healthy states are quiet.
4. **Integrations are one click away**, not buried: Ask Claude, create/link a Linear issue, save to notes.
5. **Modern and native.** Win11 look via theme tokens (light/dark/contrast for free), Mica, real controls, keyboard friendly.
6. **UI-agnostic core, tested.** Logic lives in `Cshubben.Core`; UI is thin. CI is the compiler, test runner and camera.

## Pages
Overview · Feed · My PRs · Review queue · Drift (Actions + deployments) · Linear · **Notes** (ideas, markdown notes, attached files,
links to PRs/issues) · Stats · Settings. The right-hand **inspector** opens for a PR from any list.

## Inspector (the "right menu" redesign)
My-hub's panel is one long scroll with Ask Claude last. Cshubben's inspector, top to bottom: header (repo, number, title) →
**status strip** (CI, review, threads, merge) → **next step** (one primary button + reason) → **quick actions**
(Approve / Request changes, Ask Claude, Linear issue, Save to notes, GitHub) → **tabs** (Overview, Conversation, Files, Pipeline)
with the scrolling content below. Later: keyboard shortcuts (J/K, A, R), sticky comment box, Claude draft replies.

## Carry over from My-hub (what the code has today, not what PLAN.md says)
Feed with new/seen divider and per-PR grouping · followed/muted repos · review queue states (waiting on you / updated since your review /
waiting on author / approved) · Drift tab with alert rules · PR pipeline timeline · diff view with threads · merge with 2-step confirm ·
Linear tab and PR↔issue reconciliation · Claude briefing, summaries and review drafts via `claude -p` · notifications, tray, autostart ·
light/dark theme. Not carried: GPUI specifics, Rust-only workarounds.

## Architecture
- `Cshubben.Core`: `Model` (records), `Logic` (NextAction, Overview, ReviewQueue, Age), `Data` (`IHubData`, `DemoData`; next: GitHub GraphQL
  client, Linear client, Claude runner, note store, settings).
- `Cshubben`: `AppRoot` shell, `Ui/Pages/*`, `Ui/Inspector`, `Ui/Kit`. Startup options in `AppOptions` for screenshots.
- Secrets: environment or Windows Credential Manager. Never in the repo.

## Slices (parallelisable)
A. GitHub client (GraphQL, PAT from env or `gh auth token`) → `HubSnapshot`, with fixture tests.
B. Notes: model, file-backed store (markdown + attachments folder), search, links; Notes page.
C. Linear + Claude integration: Linear client, `claude -p` runner (no tools, untrusted-data prompt), briefing, PR summary/review draft,
   "create Linear issue from PR/note".
D. UI: Feed (new/seen, grouping), Drift, Stats, Settings pages; inspector tabs (Conversation, Files/diff, Pipeline); command palette.
E. Persistence, background sync and notifications, tray.

## Status log
- 2026-11-20: repo, CI (core tests on Linux, app + screenshots on Windows), Core with tested NextAction/Overview/ReviewQueue,
  shell with Overview, Feed, My PRs, Review queue and the new inspector on demo data.

## Code review flow (must beat the GitHub web UI, or people go back to it)
Goal: reviewing a PR here is faster than on github.com for the common case, and anything not supported is one click away
(`Open on GitHub` on every view, deep-linked to the same file/line/thread) so nobody has to leave for a missing feature.

Parity checklist (a review is not "done" in the app until all of these work):
- **Files**: file tree with +/- counts, status icons, filter, collapse folders, per-file *Viewed* checkbox (synced to GitHub), progress "7/12 viewed".
- **Diff**: unified and split view, syntax highlighting, word-level changes, expand context, collapse generated/lockfiles by default, renames, binary/large-diff fallback.
- **Comments**: inline on single lines and ranges, threads with reply/resolve/unresolve, *suggested changes* (with apply), pending (batched) review, edit/delete own comments, markdown with preview, @mentions.
- **Submit**: one dialog (Approve / Request changes / Comment) with the summary text, shows number of pending comments.
- **Navigation**: keyboard first: `j/k` next/prev file, `n/p` next/prev unresolved thread or comment, `v` toggle viewed, `c` comment, `Ctrl+Enter` submit comment, `a`/`r` approve / request changes, `?` shortcut help.
- **Context**: CI checks with failing log excerpt linked to the line, commits list, review state of other reviewers, linked Linear issue, description.

Where it should be better than the web:
- **"Since my last review"** is the default for a re-review (diff between my last reviewed commit and head), with a toggle for the full diff.
- **Next unresolved thread / next unviewed file** as a single always-visible primary button (the review's NEXT STEP).
- **Claude assist, human in control**: summary and risk-ordered file list, "explain this change", drafted comments that appear as drafts in the pending review; nothing is posted without the user pressing submit. Diff text is untrusted DATA in the prompt.
- **Overview first**: size, risk, CI and who is waiting, before any code; review time estimate from lines changed.
- **Never lose work**: drafts of comments persist locally across restarts and failed network calls.
- **Fast**: diff and file list stream in; the first file is visible while the rest loads; large PRs virtualized.

Layout rule: the review view needs the full window width. On narrow windows the inspector's Files tab becomes a full-page route (list -> file tree -> diff) instead of a squeezed side panel; on wide windows it is tree | diff | thread pane.
Slice F (after D): Review view, in order: file tree + unified diff read-only -> viewed state -> inline threads read -> comment/reply/resolve -> pending review + submit -> split view, suggestions, since-last-review -> Claude assist.

## Status log (2026-11-20, evening)
- Done and green on CI (core tests, x64, ARM64): shell with responsive icon rail, Overview/Feed/My PRs/Review queue, inspector with NEXT STEP card (full-page with Back on windows < 900 px), Notes page + file store, GitHub GraphQL/REST client with write actions (not yet wired to UI), Claude runner + prompts, Linear client + reconciliation (not yet wired to UI).
- Screenshots: sizes 1920x1080 (runner resolution now works), 1440x900, 1000x760, 640x900 are produced on every push; ui-guru agent reviews them.
- Next: wire the real data (`GitHubHubData`) behind `--demo` off, settings page (tokens via credential store), Linear page, Drift/Stats, inspector tabs (Conversation, Files/diff, Pipeline), review flow (slice F), restructure into Features/* with architecture tests, quick actions (Ask Claude, Linear issue, Save to notes).
- Polish seen in screenshots: quick-action row wraps "GitHub" onto a second line at 480 px inspector; status chips wrap 3+1.

## Status log (2026-11-21, night)
- Wave 2 done and green on CI: Feed (new/seen divider, grouping, filters, muted repos), Drift page, inspector Conversation and Pipeline tabs, merge flow with two-step confirm (dry-run unless "Allow writes to GitHub" is on in Settings), notifications (toasts, quiet hours, digest), tray and autostart, command palette (Ctrl+K), keyboard shortcuts help, real Linear/Claude/GitHub wiring.
- CI: x64 build + core tests + 3 parallel screenshot shards, about 4 minutes. ARM64 only on manual runs and tags.
- Not verified with a real account yet (no token in the sandbox): live GitHub loading, Linear key flow, Claude CLI discovery, toasts/tray on a real desktop. These are the first things to try on the owner's machine; report anything that misbehaves.
- Known gaps vs My-hub: Windows installer/auto-update, syntax highlighting in diffs, suggestion apply, split-diff polish, background sync while the window is closed (tray exists, but it needs a real-world check).

## Distribution
- **Decision: Velopack** (`vpk`) with GitHub Releases as the update feed. Chosen over Inno Setup (installer only, no update story, we would
  write the self-updater) and zip + hand-rolled updater (most code to own, no delta updates, no uninstall entry).
- Velopack supports unpackaged self-contained .NET apps on win-x64 and win-arm64, installs per user without admin or Visual Studio
  (`<Id>-<channel>-Setup.exe`, Start menu shortcut, Apps & Features entry), and updates from the `Velopack` NuGet package
  (`UpdateManager` + `GithubSource`) with delta packages. NuGet `Velopack` 1.2.161 targets net10.0 and older; we use it from net11.0-windows.
- `.github/workflows/release.yml` runs on tags `v*` (tag `v1.2.3` becomes package version `1.2.3`): publish x64 and ARM64 exactly as `ci.yml`,
  `vpk pack` per platform with its own channel (`win-x64`, `win-arm64`) so each machine only sees updates for its architecture, then one
  GitHub Release gets all Setup.exe files and update packages.
- In the app: `IUpdateService` (Core, `Features/Updates`), Velopack implementation in `Services/UpdatesVelopack.cs`, About card in Settings.
  Off in `--demo`/`--smoke`/screenshots and in unpackaged dev builds; checks only on a button click, errors become a status line.
- Needs a public repo (or a token) for the GitHub feed. Not verified without a toolchain: first tag should be tried as `v0.1.0`.

## Status log (2026-11-21, day)
- Done and green on CI: syntax highlighting in diffs (TextMateSharp grammars with a built-in fallback, grammar state carried across hunk lines), command palette polish (Tab trap, row count follows window height, opaque card that follows the window theme), copy link / copy Markdown for GitHub PRs and Linear issues (buttons plus palette actions), failure classifier with calm error and empty states, Diagnostics card and `--smoke` (read-only report to `smoke-report.txt`), review flow additions (apply suggestion in dry run, key map j/k/n/p/]/[ /s/v//, Ctrl+F search in the diff), Velopack release workflow and About card, wrapping of long diff lines by default.
- Screenshots: new flags `--palette [query]`, `--file <part of path>`, `--theme light|dark`; shard 4 shoots the dark theme. ui-guru passes 4 and 5 fixed the review header at 640 px and the Linear action rows.
- Still not verified with a real account: GitHub/Linear/Claude live behaviour, writes, tray and toasts, and the Velopack release flow (first tag should be `v0.1.0`). First thing to try on the owner's machine: `Cshubben.exe --smoke`.
- Open polish: copy buttons are plain subtle buttons (no menu), dark Settings/Notes not yet reviewed, content is capped and left-aligned at 1920, in-app screenshot mode not built (CI run is about 4 minutes), diff lines changed inside a line and revealed gap lines are highlighted without grammar state.

## Accessibility
Covered (static review of `src/Cshubben/Ui`, not verified with Narrator; no toolchain here):
- Every icon-only or glyph button already had an `AutomationName` (rail, refresh, find, file navigation, copy, close, thread actions).
- `Kit.Glyph` is now `AccessibilityView.Raw`, so decorative Segoe Fluent glyphs are not read; the owning control carries the name.
- Rail items announce their count ("Review queue, 3 waiting"). The copy-link button announces "Copied" through `UseAnnounce` (live region).
- Keyboard: palette handles Esc, Tab trap and ignores typing in text boxes (`KeyInput.IsTextInput`); review keys are disabled while `Typing`, Ctrl shortcuts still work; find box and composers close on Esc; no focus visuals removed.
- Status never by colour alone: CI states have glyph plus label, diff lines have a +/- sign, resolved threads are dimmed to 0.85 (was 0.7) and also carry a "resolved" affordance.
Remaining:
- Announce other confirmations/errors ("Saved", "Copy answer", failures) with `UseAnnounce`; only the copy-link button does it so far.
- Linear priority icon (bars) has no text of its own; it relies on the row name. `Theme.TertiaryText` is used only for decorative marks there.
- Contrast of `Kit.Muted` and pills on tinted backgrounds is unmeasured; check in high-contrast and dark themes.
- Small hit targets (18 px avatars/priority icons are not interactive; some SubtleButtons with 12 px text are under 32 px tall).
- Notes page was out of scope for this pass. Tab order follows tree order and has not been audited.

## Status log (2026-11-21, afternoon)
- Stats now reads real history: merged PRs authored by the viewer (first review by someone else, size) and reviews given, over 12 weeks, via GraphQL search (`StatsQueries`/`StatsMapper` in Core, `GitHubStatsData` + `StatsPageHost` in the app). Demo data only in `--demo` or without a GitHub connection (with a banner). Not verified against a real account.
- Idea: a read-only `live-smoke` workflow (workflow_dispatch, Environment secrets `SMOKE_GITHUB_TOKEN`, `SMOKE_LINEAR_KEY`) that runs `--smoke` on the Windows runner.

## Status log (2026-11-21, evening)
- Speed: disk snapshot cache plus in-memory page caches (`SnapshotCache`, `StaleCache`, `PageCaches`); not timed against a real account yet.
- Avatars everywhere, clickable own feed items, Linear card in the My PRs panel, Linear issue text in a side panel.
- Publish size: only en and sv resources, current architecture only, no ONNX/DirectML (`scripts/prune-languages.ps1`, 267 MB to 131 MB for x64).
- Overview and Review queue sort newest first (My PRs still oldest first).
- Settings: repository list per owner with watch switch, Important toggle and activity count (`RepoDirectory`). Important repos are always checked on Drift and are the only ones that raise main-red, deploy-failed and new-PR notifications. Not seen in a screenshot yet.
- GitHub look: Octicons (merge, PR, closed, commit, branch, comment, check, cross) built from parsed SVG (`SvgPath` in Core, `GitHubStyle` in the app) in GitHub colours (purple merged, green open, red failed). Reactor's own path-text parser failed on icon data, so do not pass path strings to `PathDataString`.
- Notifications: new kinds Approved, NewInImportantRepo, MainRed, DeployFailed, LinearAssigned, LinearOutOfSync. `BackgroundWatch` checks Drift (important repos, every 5 min) and Linear (every 3 min) after each refresh; `WatchDiff` (Core) finds the changes and `WatchSources` (Core/Composition) maps Drift and Linear results to plain values. First check is a baseline. Not tried with real toasts.
- Ideas not built: notification history/bell, Linear team board and cycle progress, Drift mismatch with My-hub (red main not flagged, waiting deployments counted as running), repo chips in the review queue.

### 2026-11-21, late evening: Claude integration
- Model and effort per Claude function (`ClaudeFeature`, `AppSettings.ClaudeProfiles`, `ClaudeSettings.Resolve`). Order: user choice for the function, the function's default (Activity = haiku/low), the general Settings value. The general value was never applied before; `RealClaudeAssistant` now applies it.
- Claude page (`Features/Digest`, `GitHubDigestData`, `DigestPage`): summaries of merged PRs for yesterday, 7 days, 30 days and this year across watched repos, with a free question and a per-run model/effort override. Runs only on a button press.
- Review workspace: `m` marks a line, `a` asks Claude about the marked range or the selected line (`CodeSelectionBuilder`, `Prompts.CodeQuestion`). Claude sees only those lines.
- Not done: asking Claude for a full review from the workspace (the PR inspector still has "Draft review"), live check of the Claude page with real data.

## Errors module (error spikes from logs, tied to deploys)
Goal: see when errors spike per application, tie the spike to the deploy that probably caused it, and let Claude analyse it.
Logs live in Grafana (Loki: labels `appname`, `namespace`, `loglevel`, `pod`). Cshubben is a local desktop app.

Decisions:
- **Optional module, off by default** (`AppSettings.Errors.Enabled`). Off means no sidebar entry, no palette entry, no background work and no token needed. `PageDef.Visible` is the generic switch for any page.
- **Layers.** (1) Deterministic collection and spike detection in Core, no AI. (2) Deploys from GitHub (Deployments/Actions; fallback: a new pod template hash in the pod name means a rollout). (3) Claude on demand: gets a fingerprint, top stack traces and the good/bad deploy SHAs, reads the diff through GitHub, and only uses a worktree on the deploy SHA (never the user's working tree) for deeper analysis.
- **Mappings** (`RepoBindings`): `appname -> owner/repo` and `namespace prefix -> owner/repo` (stack frames name shared libraries such as BuildingBlocks). Shared inside the team as one line of JSON (no address, token or local folders in it).
- **Grafana token** only in the credential store (`SecretKeys.GrafanaToken`), never in settings.json. Plain http only for localhost.
- **Fingerprint** of an error = exception type + logger + top app frame (file:line), never the message (ids and tenant paths vary). Known rule-based classes get their own action, for example the missing `backend_role` claim: the code that requires the role shipped before the claim was added in PolicyServer, so the fix is a PolicyServer change, not code.
- **Trends**: per app a sparkline, factor against the app's own trailing 24 h, arrow up/flat/down; per error group its own sparkline, first/last seen, pods and a status (new, rising, fading, stopped). "First seen after deploy X" is the strongest signal. Deploy markers on the chart. The same hour one week earlier is shown next to the rate as context (one week only, informational; the spike rule itself does not use it).
- Logs can hold customer data: redact before anything goes to Claude, and send aggregates and excerpts, not raw logs.

Status:
- 2026-11-23: foundation. Module switch, Settings card (Grafana address and token, app and namespace mappings with repo suggestions, local clones folder, team sharing), Errors page showing connection and mappings, tests in `ErrorsSettingsTests`.
- 2026-11-23: spike view (`Features/Errors`). `GrafanaClient` reads Loki through Grafana's datasource proxy (one metric query for all apps, log lines on drill-down), `SpikeDetector` compares the latest hour with the app's own 24 h history (median + 4 robust deviations, at least one error per bucket), trend arrow against the previous 6 h, `ErrorFingerprints` + `ErrorGrouping` group the lines. The page lists apps (rate, trend, mini chart) and shows the picked app's chart and error groups with repo and hints. `--demo` uses `DemoErrorSource`. Settings: namespace, data source UID, include warnings, Test connection. Not verified against a real Grafana, and the UI has not been looked at yet.
- (The "Next" list that stood here, deploy markers, same-time-of-week comparison, Claude analysis and a retention probe, is done: see the entries below.)
- 2026-11-24: Errors improvements (one PR). (Correction to the notes above: the Azure Monitor source through the MCP server, `AzureLogs`/`McpClient`/`McpOAuth*`, and Claude analysis of one error already exist; Grafana is the alternative source.)
  - `LogRedactor`: credentials, tokens, e-mail addresses, personal identity numbers and IP addresses are removed from log text before it goes to Claude (`ErrorPrompts`, count shown under the answer) and into copied summaries.
  - Deployments of the bound repository (`ErrorContextLoader.LoadDeploysAsync`, cached in `PageCaches.ErrorDeploys`, demo data in `DemoErrorSource.DeploysFor`) are marked on the app and error charts. `SpikeOnset` finds when a spike began and `DeployCorrelation` ties it, or the first sighting of an error, to the deployment just before it.
  - Error groups get a status (`GroupStatuses`: new, rising, fading, stopped, ongoing) and four sort orders (`GroupOrdering`). The groups come from the newest log lines only (1000 for Loki, 400 for Azure, `IErrorSource.LineLimit`), so when that cut off older ones (`GroupSampling`) "new" and "first seen after deploy" are not shown and the page says so.
  - "Copy summary" for an application and for one error (`ErrorReport`, redacted). "Open as deployed" links the file at the deployed commit (`ErrorLinks`). Charts have a text description for screen readers (`ErrorsText.ChartSummary`). The page says when it was updated.
  - At this point not done (all three were done later, see below): a same-time-of-week comparison, a retention probe, a spike notification. Not verified against real logs. The UI was reviewed from CI screenshots (light, dark, 1000x760); the narrow (640 px) detail view was not photographed.
  - 2026-11-24, later: "Create Linear issue" on an error group (`ErrorIssue.Draft` in Core: redacted summary as description, priority from status and volume; `ErrorIssueButton` in the UI, shown only when Linear is connected).
  - 2026-11-24, evening: same-time-of-week comparison. `IErrorSource.LoadLastWeekAsync` reads the hour that matches "now" one week ago for all applications (one extra query, cached, never an error); `WeeklyBaseline` says higher / usual / lower and the page and the copied summary show it. It does not change what counts as a spike. Caveat: an application with no errors a week ago and logs that do not reach back that far look the same ("none at this time last week"); the retention probe followed in the next entries.
  - 2026-11-24, night: spike notification. New `NotificationKind.ErrorSpike` (toggle in Settings, on by default): `BackgroundWatch` reads the error counts every 5 minutes after a refresh (same data and cache as the Errors page, saved sign-in only, never opens a browser), `WatchSources.FromErrors` + `WatchDiff.Errors` report an application that started to spike once, through the existing rules (mute, quiet hours, digest, seen-set). A click opens the Errors page on that application (`cshubben://errors/<app>` in the toast, `ErrorsFocus` hands the app to the page). Only while the app runs (tray keeps it alive). Known edge: clicking the toast while already on the Errors page only brings the window forward.
  - 2026-11-24, night: retention probe. `LoadLastWeekAsync` now returns `LastWeekLogs(Series, LogsReachBack)`: it first checks that the namespace (Loki, all levels) or `AppTraces` (Azure) has any rows in that hour a week ago, and when not the page says "No comparison with last week: the logs do not reach back that far." instead of claiming "none at this time last week". Also fixed: the Azure tool's lookback defaults to 25 hours, so the last-week query now passes about 170 hours (before this, Azure would always have looked empty).
  - 2026-11-24, night: code review of PRs #11-#15 and fixes. (1) `IErrorSource.LineLimit` (Loki 1000, Azure 400): the page decides "complete" and words the truncation note from what the source really reads. (2) "First seen after deploy" is not shown when the groups are incomplete. (3) Deployments count when any status was "success" (older ones are "inactive" once a newer one succeeds); untested against real GitHub data. (4) `ErrorIssueButton` calls all hooks before any early return, remembers issues it made in this session, shows a failed team load. (5) Redaction: well-known token shapes, IPv6, twelve-digit personal numbers (Luhn-checked), passwords with spaces. (6) The last-week window is built from the current window and the wall clock of the local time zone (a clock change moves it), and its cache key holds the window end. (7) File links escape each path part and drop `.`/`..`; still assumes the log path lost a leading `src/`, which is wrong for repos without a `src` folder. Also: `SOURCE.txt` on the screenshots branch names the PR head commit; titles are not cut inside an emoji.
  - Open items for the Errors module: nothing has been checked against real Azure, Grafana or GitHub data (deployments that went `inactive`, the 170 hour Azure lookback, the retention probe, the spike toast on a real desktop); file links assume a leading `src/` was lost from the log path; the narrow (640 px) detail view and the issue button have not been photographed; a spike that is already running when the app starts does not raise a toast.
