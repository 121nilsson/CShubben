---
name: ci-screenshots
description: See what the app looks like. Download screenshots produced by the CI "screenshots" artifact and view them. Use after UI changes and when iterating on design.
---

# Seeing the app

The sandbox cannot run WinUI, so CI runs the app on a Windows runner with deterministic demo data and photographs each page.

## How it works
- `scripts/screenshot.ps1` starts `Cshubben.exe --demo --start <page> [--select <prId>] [--tab <n>]` per shot, waits, and saves
  `<shot>.png` (PrintWindow of the window) and `<shot>.screen.png` (screen copy, fallback). `log.txt` lists what happened.
- Add a shot by adding `"<page> --select <id>"` to the default `-Shots` list in the script (or the workflow). Pages: overview, feed,
  mine, reviews, review, drift, errors, linear, notes, stats, settings. Demo PR ids: a1..a8 (mine), r1..r7 (review queue).
- App options are parsed in `src/Cshubben/AppOptions.cs`; add new ones there when a view needs a special starting state.

## Get the images
`gh run download` does NOT work from the sandbox (the artifact host is blocked by the egress proxy). CI therefore also force-pushes the
latest screenshots to the orphan branch `ci-screenshots` (on pushes and on pull requests from this repository; `SOURCE.txt` names the commit that was pushed), which git can fetch:
```
RUN=$(gh run list -R 121nilsson/CShubben --branch <your-branch> --limit 1 --json databaseId -q '.[0].databaseId')
gh run watch $RUN -R 121nilsson/CShubben --exit-status          # wait for the run to finish
cd /home/claude/cshubben && rm -rf /tmp/shots && mkdir -p /tmp/shots
git fetch -q --depth 1 origin ci-screenshots && git --work-tree=/tmp/shots checkout FETCH_HEAD -- . && git reset -q
cat /tmp/shots/SOURCE.txt /tmp/shots/log.txt        # SOURCE.txt must name the commit you just pushed
```
Then `Read` the PNGs in /tmp/shots (the Read tool shows images). If `<shot>.png` is black or blank, look at `<shot>.screen.png`.
If the branch is missing or stale, the app probably crashed on start: read `log.txt` ("exited early") and `gh run view $RUN --log`.

## Reviewing a design
Look at: hierarchy (is the next step obvious in 2 seconds?), spacing on the 4 px grid, text clipped or overlapping, contrast, whether
colour is used only for attention, empty and long-text cases. Change, push, re-shoot. Keep a before/after in the PR or in PLAN.md notes.
