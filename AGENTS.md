# Toby repository instructions

## Current stage

This branch is a product blueprint. Do not implement the application merely because its documents describe future modules. The owner reviews and accepts a milestone plan before that milestone's implementation begins.

## Original work

Author new code and assets independently. Do not reuse the inspirational application's source, images, GIFs, fonts, or build scripts. Main and fork history retain upstream provenance; do not misrepresent that ancestry.

## Work scope

Read README.md, docs/DECISIONS.md, and the document relevant to the requested task. Respect the owner's current request and approved scope. Routine decisions within an approved milestone do not require repeated permission.

A hierarchy document describes future responsibilities; it does not by itself require spawning agents for every task. Assign independent bounded tasks only when appropriate and authorized. Keep Chief and Validator responsibilities distinct in implementation reviews.

## Agent-to-agent communication (A2A)

All agents in this project run inside Herdr managed panes with Herdr Link available as an MCP server (registered as `herdr_link` in `.mcp.json` at the repo root). Use it to notify Chief and ask questions without leaving your task.

**To notify Chief (Hermes) or ask a question:**

Activate the gateway once per session (idempotent):
  mcp__herdr_link__herdr_link {}

Then send:
  mcp__herdr_link__herdr_link_send { "to": "hermes", "message": "DONE: <task-name> — <one-line summary>" }
  mcp__herdr_link__herdr_link_send { "to": "hermes", "message": "BLOCKED: <task-name> — <what you need>" }
  mcp__herdr_link__herdr_link_send { "to": "hermes", "message": "QUESTION: <task-name> — <specific question>" }

Discover live peers if needed:
  mcp__herdr_link__herdr_link_peers {}

Rules:
- Send exactly one message and end your turn. Do not poll or wait for a reply; Herdr delivers it.
- Use A2A whenever done, blocked, or when you have a question requiring owner input.
- Do NOT use shell commands like `herdr agent prompt hermes "..."` — use the MCP tool instead.
- A2A is same-workspace only.

## Engineering rules

Use a separate feature branch/worktree for each concurrent implementation task. Declare ownership of shared contracts.

### Atomic commits — required

Every commit must contain exactly one logical change. This is not a style preference; it is a hard rule so the owner can bisect and revert individual behaviors without untangling unrelated work.

- One behavior change (or one new file, one refactor, one doc update) per commit.
- Include only the tests and documentation directly relevant to that change — nothing else.
- Do not bundle a feature, its refactor, and a docs update into a single commit.
- Commit message format: `<type>(<scope>): <what changed>` — e.g. `spike(m0): add whisper-rs transcription binary`.
- If you find yourself writing "and" in a commit message, split the commit.

Verify outcomes instead of inferring success from output, silence, or model confidence. Report actual checks and unresolved limitations. Never claim an integration or benchmark exists before it is demonstrated.

Keep provider credentials, raw personal audio, screenshots with personal data, model weights, and generated binaries out of Git. Use synthetic fixtures for tests.

Toby's memory must preserve scope and provenance. Learned skills cannot broaden authority. Desktop control must have a local stop path and user takeover behavior.

## Release boundaries

Provider sign-in and session control are distinct. Use supported authentication; no token-cache scraping. Revalidate eligibility at release. Do not advertise universal terminal attachment or universal computer control.

Do not merge, change default branches, force-push, or publish a release based solely on this planning document. Follow the owner's specific authorized workflow.

## Planning reference

docs/HIERARCHY.md defines work packets and validation.
docs/ROADMAP.md defines milestone acceptance.
docs/REPOSITORY_PLAN.md defines future layout and branch/worktree policy.
