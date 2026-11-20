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
  mine, reviews, drift, linear, notes, stats, settings. Demo PR ids: a1..a8 (mine), r1..r7 (review queue).
- App options are parsed in `src/Cshubben/AppOptions.cs`; add new ones there when a view needs a special starting state.

## Get the images
```
RUN=$(gh run list -R 121nilsson/CShubben --branch main --limit 1 --json databaseId -q '.[0].databaseId')
gh run watch $RUN -R 121nilsson/CShubben --exit-status
rm -rf /tmp/shots && gh run download $RUN -R 121nilsson/CShubben -n screenshots -D /tmp/shots
cat /tmp/shots/log.txt
```
Then `Read` the PNGs (the Read tool shows images). If `.png` is black or blank, look at `.screen.png`.
If the artifact is missing, the app probably crashed on start: check `log.txt` ("exited early") and the run log.

## Reviewing a design
Look at: hierarchy (is the next step obvious in 2 seconds?), spacing on the 4 px grid, text clipped or overlapping, contrast, whether
colour is used only for attention, empty and long-text cases. Change, push, re-shoot. Keep a before/after in the PR or in PLAN.md notes.
