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
