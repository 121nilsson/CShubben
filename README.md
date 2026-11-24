# Cshubben

A Windows desktop hub over **GitHub pull requests**, **Linear issues** and **Claude**, with a place for notes, ideas and files.
It is the successor to My-hub, built with **.NET 11 / C# 15** and **Microsoft.UI.Reactor** (declarative, React-style UI in plain
C# for WinUI 3). Reactor is a preview and Windows-only.

## What it does

- **Overview**: a short briefing of what needs you now, what is ready to merge, and what waits on others.
- **Feed, My PRs, Review queue**: activity since you last looked, grouped and filterable; your pull requests with a next step.
- **Inspector**: for any pull request, CI, review, threads and merge state at a glance, a next-step button, and quick actions
  (ask Claude, link a Linear issue, save a note, copy the link).
- **Code review**: diff with syntax highlighting, split view, threads and drafts, suggestions (dry run until writes are enabled),
  keyboard flow (press `?` on the page) and search (Ctrl+F).
- **Drift**: CI runs and deployments that need attention. **Linear**: issues next to their pull requests, with one-click fixes
  for things that are out of sync. **Notes**: ideas, decisions and files. **Stats**.
- **Errors** (optional, off by default): error spikes per application from your logs (Azure Monitor through an MCP server, or Grafana/Loki),
  tied to the deployment just before them. Error groups show whether they are new, rising or fading; each application is compared with the
  same time last week. Ask Claude to analyse an error, create a Linear issue from it, copy a summary, and get a notification when an application
  starts to spike. Sensitive values (tokens, passwords, e-mail addresses, personal numbers, IP addresses) are removed before anything goes to Claude or is copied.
- **Command palette** (Ctrl+K), keyboard shortcuts (`?`), tray icon and notifications, light and dark theme.

## First run

1. Start the app. Without credentials it shows demo data. Open **Settings**.
2. **GitHub**: the token is read from `GITHUB_TOKEN`, `GH_TOKEN` or the GitHub CLI and is never stored by Cshubben.
3. **Linear**: paste a personal API key; it is kept in the Windows credential store.
4. **Claude**: needs the `claude` command line tool, signed in. It runs with no tools enabled.
5. Writes to GitHub (merge, review, comment, re-run) are **simulated (dry run)** until you switch on *Allow writes to GitHub*.
6. Not sure something works? Run `Cshubben.exe --smoke`: it only reads, and writes `smoke-report.txt` in the data folder
   (also available as **Diagnostics** in Settings). The report never contains tokens.

## Command line

| Flag | Meaning |
| --- | --- |
| `--demo` | demo data, no network, no real settings |
| `--smoke` | read-only self check, writes `smoke-report.txt`, then exits |
| `--start <page>`, `--select <pr>`, `--tab <n>`, `--file <part of path>` | open a page, pull request, inspector tab or file (used for screenshots) |
| `--palette [query]`, `--theme light\|dark` | open the palette, force a theme (used for screenshots) |

## Build

```
dotnet build src/Cshubben/Cshubben.csproj -p:Platform=x64
dotnet run --project src/Cshubben -p:Platform=x64
dotnet test tests/Cshubben.Core.Tests
```

Always build with an explicit `-p:Platform=x64` (or `ARM64`). `Cshubben.Core` has no UI dependency and is tested on Linux.

## CI and releases

- `.github/workflows/ci.yml`: core tests on Linux, the app on Windows (x64 on every push, ARM64 on manual runs and tags) and
  screenshots of every page in several window sizes and both themes (artifact, and the `ci-screenshots` branch, which is updated on pushes and on pull
  requests from this repository).
- `.github/workflows/release.yml`: tag `v1.2.3` builds Velopack installers and update packages for x64 and ARM64 and attaches
  them to a GitHub Release. Run it manually for a dry run (nothing is published). The update feed needs a public repository.

## Code layout

Vertical feature slices: `src/Cshubben.Core/Features/<Name>` hold a feature's model, logic and data access, `Shared` is a small kernel,
`src/Cshubben` is the Reactor UI and the composition root (`Services/`). Architecture tests enforce that Core never references the
UI and that features do not reach into each other. See `CLAUDE.md` for the rules and `PLAN.md` for decisions and status.
