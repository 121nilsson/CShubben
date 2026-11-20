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
