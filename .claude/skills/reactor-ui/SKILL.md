---
name: reactor-ui
description: Write or change UI in Cshubben with Microsoft.UI.Reactor (React-style WinUI 3 in C#). Use for any file under src/Cshubben/Ui, before adding a page, component or style.
---

# Writing UI in Cshubben

Reactor is a preview framework and the sandbox cannot compile it, so write conservatively and let CI confirm.

## Get the docs (do this first, it is cheap)
The Reactor repo is public and ships agent docs. If `/home/claude/microsoft/microsoft-ui-reactor` is missing:
`mkdir -p /home/claude/microsoft && GIT_LFS_SKIP_SMUDGE=1 git clone --depth 1 https://github.com/microsoft/microsoft-ui-reactor /home/claude/microsoft/microsoft-ui-reactor`
(the add_repo tool says read access is already available; the clone can take a few minutes).
- `skills/reactor.api.txt` (~370 KB): every factory, modifier, hook and `Theme.*` token. **grep it, never read it whole**,
  e.g. `grep -nE "^SelectorBar\(" skills/reactor.api.txt`. Trust it over memory.
- `skills/dsl-reference.md`, `skills/design.md`, `skills/navigation.md`, `skills/async.md`, `skills/recipes/*.cs`.

## House rules
- **Colours only from `Theme.*` tokens** (`Theme.CardBackground`, `Theme.SecondaryText`, `Theme.SystemCritical`, ...). No hex
  on themed surfaces. This gives light/dark/high-contrast for free. `Kit.Fg(Tone)` / `Kit.Bg(Tone)` map our semantic tones.
- **Silence means fine.** Spend colour only on what needs attention (red CI, conflicts, "waiting on you"). Healthy states are quiet.
- Real controls for interaction: clickable things are `Button(...)`. Rows are `Button(Grid(...), onClick).SubtleButton()`.
- Layout: `FlexColumn`/`FlexRow` (use `with { RowGap = 12 }`), `Grid(columns: [...], rows: [...], children)` with `GridSize.Star()/Px()/Auto`.
  `.Flex(grow: 1, basis: 0)` for the growing region. `FlexColumn` ignores `.Padding()`, use `.FlexPadding(h, v)`.
  Text that must trim needs a Grid `Star` column (or a flex child) plus `.TextTrimming(TextTrimming.CharacterEllipsis)`.
- Shell: `TitleBar` + `.Backdrop(BackdropKind.Mica)`; the page is a rounded `Theme.LayerFill` panel floating on Mica.
- 4 px grid for spacing. Type ramp: `Title`, `Subtitle`, `BodyStrong`, `Body`, `Caption`; small text via `Kit.Muted`.
- Hooks (`UseState`, ...) only inside `Component.Render()`, always in the same order. Pages are plain static functions that
  return `Element` and take data and callbacks; only `AppRoot` (and later a few components) own state.
- Dynamic lists: `.WithKey(id)` on every item. Icon-only buttons need `.AutomationName(...)`.
- Modifier order: type-specific ones first (`TextBlock(..).Bold().Margin(..)`).
- Do not put logic in the UI. Anything testable (sorting, grouping, "next action") belongs in `Cshubben.Core` with tests.

## Loop
1. Edit. 2. Commit and push to `main` (or a branch). 3. `gh run watch <id> -R 121nilsson/CShubben --exit-status`.
4. On red: `gh run view <id> -R 121nilsson/CShubben --log-failed | grep -E "error|warn"`; Reactor errors come with `→ try:` hints, use them.
5. Look at the result with the `ci-screenshots` skill.
