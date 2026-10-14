# AGENTS.md

Universal instructions for any coding agent working in this repo (Codex, Grok, Gemini, Cursor, etc.) — model-specific files (`CLAUDE.md`, etc.) add to this, they don't replace it.

## Atomic commits — mandatory

Every agent commits its own work before reporting back to the orchestrator. No exceptions:

- One logical change per commit. If you fixed two unrelated things, that's two commits.
- Commit message format: `type(scope): short imperative summary` (e.g. `fix(windows): latch Ctrl+Alt+S stop`). Types: `feat`, `fix`, `docs`, `chore`, `refactor`, `test`.
- Commit on YOUR OWN branch/worktree only. Never commit on `main` or another agent's branch.
- Commit immediately after finishing an assigned task — don't batch several tasks' changes into one commit, and don't leave work uncommitted "for later."
- Stay inside your assigned files/area for that task. If you touch something outside it, call that out explicitly in your commit message and your report — don't silently scope-creep.
- A build/test verification claim ("builds clean", "80/80 tests pass") must be something you actually ran this session, not something you're assuming. Say explicitly what you verified vs what you couldn't verify (e.g. no live API key, no real device).

## Reporting (A2A protocol)

1. You only ever work a task explicitly assigned by the orchestrator.
2. The moment you finish or hit a blocker, you commit, then stop and report back. No self-chaining into a new task, no open-ended looping.
3. Before starting, read your project's Obsidian log (path given in your task) for prior context. After finishing, append a dated bullet there with what you did/decided — this is the team's persistent memory across sessions.
4. Pushing to the remote and merging branches into `main` is the orchestrator's job, not yours. You commit locally; the orchestrator integrates.

## Why this matters here specifically

Four agents work in parallel in separate `git worktree` checkouts of this repo, each on its own branch. Non-atomic or uncommitted work is invisible to the orchestrator's merge step and silently lost when a worktree gets reset for the next round. Commit early, commit often, commit scoped.
