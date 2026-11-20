# Cshubben – working notes for Claude

A Windows desktop app, a sibling to My-hub (github.com/121nilsson/my-hub, Rust/GPUI). It is a hub over GitHub
pull requests, Linear issues and Claude, plus a place for notes, ideas and files.
Stack: **.NET 11 / C# 15**, UI with **Microsoft.UI.Reactor** (React-style declarative WinUI 3, preview, Windows only).

- **Language:** code, comments, docs, commit messages and the UI are in **English**. The owner chats in Swedish.
- **No local toolchain:** the sandbox has no dotnet and no Windows. GitHub Actions is the compiler, the test runner and
  the "eye": push, then watch the run (`gh run watch`), read errors with `gh run view --log-failed`.
- **Layout:** `src/Cshubben.Core` (UI-agnostic models, logic, data; tested on Linux), `src/Cshubben` (Reactor UI),
  `tests/Cshubben.Core.Tests`. Keep logic in Core, UI thin.
- **Skills:** `.claude/skills/reactor-ui` (how to write the UI) and `.claude/skills/ci-screenshots` (how to see the app).
- **Plan and decisions:** `PLAN.md`.
- Never commit secrets. Tokens come from the environment or the Windows credential store, never from files in the repo.
