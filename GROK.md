# GROK.md

Grok-specific notes for this repo. The baseline rules are in [AGENTS.md](AGENTS.md) — read that first; everything there applies to you too, including the atomic-commit rule.

## Grok specifics

- Use `git commit` directly in your worktree; don't ask the orchestrator to commit for you.
- When a tool call needs approval ("Yes, proceed" / "always-approve"), that's just a permission gate — it doesn't change the A2A protocol: finish → commit → log → report → stop, never self-chain into new work on your own.
- You're most often assigned cross-cutting QA/verification rather than owning a feature area. When you ARE given files to change (e.g. a docs file), the same atomic-commit and narrow-scope rules apply as any other agent.
- When asked to verify another agent's claim (a test count, a build result), actually run it yourself and report the real result — don't just restate what they said.

See AGENTS.md for the commit format, reporting protocol, and Obsidian logging requirement — they are identical for every agent kind in this repo, Grok included.
