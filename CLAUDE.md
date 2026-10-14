# CLAUDE.md

Claude Code-specific notes for this repo. The baseline rules are in [AGENTS.md](AGENTS.md) — read that first; everything there applies to you too, including the atomic-commit rule.

## Claude Code specifics

- Use `git commit` directly in your worktree; don't ask the orchestrator to commit for you.
- When `bypass permissions` / auto-approve mode is on, you still stop and report after each assigned task — that setting controls tool approval prompts, not the A2A protocol (still finish → commit → log → report → stop, never self-chain).
- If a prompt from the orchestrator is ambiguous about which files you own this round, default to the narrowest reasonable scope and say what you assumed in your report, rather than guessing wide.
- Session recaps/health-check prompts (the "How is Claude doing this session?" dialog, `/config` recap toggles) are fine to leave as-is; they don't block the A2A protocol.

See AGENTS.md for the commit format, reporting protocol, and Obsidian logging requirement — they are identical for every agent kind in this repo, Claude included.
