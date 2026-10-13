# Toby repository instructions (Claude Code)

Read AGENTS.md first — it is the primary instruction file. This file adds Claude-specific notes and repeats the most critical rules.

## Atomic commits — required

Every commit must contain exactly one logical change. This is a hard rule so the owner can bisect and revert individual behaviors without untangling unrelated work.

- One behavior change (or one new file, one refactor, one doc update) per commit.
- Include only the tests and documentation directly relevant to that change — nothing else.
- Do not bundle a feature, its refactor, and a docs update into a single commit.
- Commit message format: `<type>(<scope>): <what changed>` — e.g. `spike(m0): add whisper-rs transcription binary`.
- If you find yourself writing "and" in a commit message, split the commit.

## Work scope

Do not implement the application merely because its documents describe future modules. The owner approves each milestone before implementation begins. You are operating under an approved milestone and a specific work packet from Chief (Hermes).

## Original work

Author new code and assets independently. Do not reuse the inspirational fork's source, images, GIFs, fonts, or build scripts.

## Verification

Verify outcomes — never infer success from output, silence, or model confidence. Report actual checks and unresolved limitations.

## Secrets and data

Keep provider credentials, raw personal audio, screenshots with personal data, model weights, and generated binaries out of Git. Use synthetic fixtures for tests.

## Planning reference

docs/HIERARCHY.md — roles, work packets, handoff format
docs/ROADMAP.md — milestone gates and acceptance criteria
docs/REPOSITORY_PLAN.md — branch strategy and worktree policy
