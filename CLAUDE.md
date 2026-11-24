# Cshubben – working notes for Claude

A Windows desktop app, a sibling to My-hub (github.com/121nilsson/my-hub, Rust/GPUI). It is a hub over GitHub
pull requests, Linear issues and Claude, plus a place for notes, ideas and files.
Stack: **.NET 11 / C# 15**, UI with **Microsoft.UI.Reactor** (React-style declarative WinUI 3, preview, Windows only).

- **Language:** code, comments, docs, commit messages and the UI are in **English**. The owner chats in Swedish.
- **No local toolchain:** the sandbox has no dotnet and no Windows. GitHub Actions is the compiler, the test runner and
  the "eye": push, then watch the run (`gh run watch`), read errors with `gh run view --log-failed`.
- **Pull requests from the sandbox:** `gh pr create` and the GitHub MCP write calls are blocked here. Push the branch, then open the PR with
  `gh api repos/121nilsson/cshubben/pulls -f title=... -f head=<branch> -f base=main -f body=...` and merge with
  `gh api -X PUT repos/121nilsson/cshubben/pulls/<n>/merge -f merge_method=squash`. A branch push alone does not start CI; the pull request does.
- **Layout:** `src/Cshubben.Core` (UI-agnostic models, logic, data; tested on Linux), `src/Cshubben` (Reactor UI),
  `tests/Cshubben.Core.Tests`. Keep logic in Core, UI thin.
- **Skills:** `.claude/skills/reactor-ui` (how to write the UI) and `.claude/skills/ci-screenshots` (how to see the app).
- **Plan and decisions:** `PLAN.md`.
- Never commit secrets. Tokens come from the environment or the Windows credential store, never from files in the repo.

## Architecture rules (keep the code easy to change)
- **Vertical feature slices.** Group by feature, not by technical layer: `Features/<Name>/` holds that feature's model, logic, data access
  and UI together (e.g. Notes, PullRequests, Linear, Claude, GitHub). `Shared` is a small kernel (Tone, Age, common records).
  A feature may depend on `Shared` and on another feature's *public contract* (its interfaces and records), never on its internals.
- **Core never references UI.** `Cshubben.Core` has no WinUI/Reactor dependency; UI is `Cshubben`. Architecture tests enforce both rules.
- **Dependency inversion at I/O boundaries only**: network, process, file system, clock, secrets sit behind small interfaces
  (`IHubData`, `INoteStore`, `IProcessRunner`, `ISecretStore`). Do not add an interface for a pure function or a single implementation.
- **Single responsibility, small files.** Pure logic (sorting, grouping, "next action", mapping JSON to models) lives in static classes that are
  unit tested on Linux. UI pages are functions from data to `Element`; only a few components own state.
- **Open for extension without editing the shell:** pages and PR-inspector actions are registered by each feature in a registry, so adding a
  feature does not touch `AppRoot`.
- **Tests:** every non-trivial rule gets a test; fixtures are inline JSON/strings. Secrets never in tests or logs.
