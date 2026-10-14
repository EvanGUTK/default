# HANDOFF — read this first when resuming

Last session paused: 2026-10-13, end of day. `main` is at `35384a3`, pushed to `origin/main`. All 4 agent worktrees are clean and idle.

Full narrative state (what's done, what's open, why decisions were made) lives in Obsidian: `AI Projects/Bluey.md` (vault at `/mnt/e/VAULT/USAAS`), specifically the "## PAUSED — resume here" section and the append-only "## Log" below it. Read that before doing anything else — it has the actual history, this file is just the mechanical resume checklist.

## What this project is, right now

A Windows desktop companion app (`Windows/BlueyDesktop.App`, WPF, .NET 10) — an animated blueberry character that floats on screen, follows your mouse, controlled via voice (OpenAI Realtime — skeleton only, unverified live) and able to drive the computer (click/type/scroll) with safety guardrails. Mac/iOS versions exist on `main` too but are backburnered, not deleted.

## Resume checklist

1. Confirm the repo and worktrees are in the expected state:
   ```
   cd /mnt/e/codex/default && git log --oneline -1   # expect 35384a3 or newer
   git worktree list                                  # expect 4 worktrees, each on windows/<agent>, clean
   ```
2. Confirm it still builds (through the Windows host, since this runs in WSL):
   ```
   powershell.exe -NoProfile -Command "cd 'E:\codex\default\Windows'; dotnet build"
   ```
   Expect 0 warnings, 0 errors. If not, something changed outside this session — investigate before assigning new work.
3. Re-launch the 4 herdr agent panes if they're gone (they don't survive a Herdr restart): see the `herdr` skill and `AI Projects/Bluey.md`'s Agent roster table for names/kinds/specialties. Brief each one fresh — they do not remember anything from before; the Obsidian Log is how they (and you) recover context.
4. Pick the next task from "Known open items" in Bluey.md's PAUSED section, or from new instructions.

## Why agents "stop" and don't report automatically

This is by design (the A2A protocol in Bluey.md), not a malfunction: each agent does exactly the task it was given, commits, logs to Obsidian, reports back once, then goes idle and waits — it will never pick up new work on its own. The orchestrator (this Hermes session) only acts when the human sends a new message; there is no background loop that keeps things moving between sessions. That's why a session boundary always looks like a "pause" — the fix for that is this handoff doc, not having agents loop on their own, which was explicitly ruled out early in this project.

## Commit/push discipline

- Every agent commits atomically on its own branch (see `AGENTS.md`, `CLAUDE.md`, `GROK.md` at repo root).
- Only the orchestrator merges agent branches into `main` and pushes to `origin`. Agents never push.
- If you ever see `git status` on `main` say "ahead of origin by N commits," that means a push was missed — run `git push origin main`.
