# Repository and worktree plan

## Current branch

The original fork baseline is commit 6d84d91456f3bf7ad9b31caf31f19710b0ba8328. The blueprint lives on docs/toby-product-blueprint with a documentation-only current tree. Main and upstream history retain inherited files.

The intended repository name is toby-desktop. Renaming a repository does not change its branch contents or remove its fork ancestry. A truly standalone repository/history is a separate owner decision.

## Planned implementation layout

Directories below are a future layout, not existing implemented modules.

| Planned path | Responsibility | Primary owner |
| --- | --- | --- |
| apps/desktop/ | React settings, conversation, review, activity, companion | Experience |
| apps/desktop/src-tauri/ | Desktop host and scoped command entry points | Runtime |
| apps/mobile/ | Optional phone client after desktop proof | Mobile |
| crates/runtime/ | Tasks, cancellation, execution broker, event normalization | Runtime |
| crates/platform-windows/ | Capture, UIA, input, geometry, takeover | Windows |
| crates/memory/ | SQLite records, retrieval, correction, retention | Memory |
| crates/skills/ | Declarative skill validation and lifecycle | Memory |
| crates/speech/ | Engine adapters and supervised speech processes | Voice |
| adapters/codex/ | Supported Codex session protocol | Integrations |
| adapters/claude/ | Supported Claude session protocol | Integrations |
| adapters/terminal/ | Owned terminals and PowerShell/WSL execution | Integrations |
| packages/contracts/ | Versioned cross-process schemas | Chief-designated owner |
| assets/companion/ | New character states and original assets | Motion |
| tests/fixtures/ | Synthetic sessions and desktop targets | Relevant specialist |
| tests/acceptance/ | Representative end-to-end scenarios | Validator |
| docs/ | Product, decisions, evidence, compatibility | Chief and specialists |

Do not precreate empty implementation scaffolds for every future module. Add paths as the approved milestone needs them. Contract changes should precede dependent implementations when possible.

## Branch strategy

| Branch type | Purpose |
| --- | --- |
| main | Existing fork baseline until owner accepts migration |
| docs/toby-product-blueprint | Original specification and review |
| feature/<bounded-task> | Focused implementation |
| integration/<milestone> | Combined milestone behavior |
| fix/<specific-failure> | Bounded repair from validation |
| release/<version> | Packaging and release checks if warranted |

No branch merge or default-branch change is implied by creating this blueprint.

## Worktree policy

Create one isolated worktree per concurrent bounded implementation task. Each worktree checks out its own feature branch. Keep shared local runtime state outside the repository and use different test ports/process identities when needed.

A worktree does not provide application-data isolation automatically. Workers must avoid the user's real mailbox, live provider credentials, active personal browser profile, and shared production SQLite state during automated tests.

Chief owns integration ordering. Shared contract files have one active owner. Specialists synchronize on reviewed contract commits rather than independently changing incompatible schemas.

## Atomic change policy

One coherent behavior change and its directly relevant validation belong in a commit. Avoid “everything changed” checkpoints spanning voice, memory, mobile, and Windows control. Keep generated binaries, model weights, microphone recordings, credentials, and screenshots with personal data out of Git.

The current documentation snapshot is one coherent planning commit because its linked specification, hierarchy, and roadmap form a single reviewable deliverable.

## Pull request evidence

A PR explains the trigger, new behavior, changed boundary, verification performed, and material limitations. Show the actual commit tested. Chief checks integration; Validator checks independent acceptance. Push a feature branch for review without implying merge readiness.

## Repository presentation

README uses centered identity, truthful status badges, example requests, principle tables, compact diagrams, and linked detailed documents. Use static status badges for a planning branch; do not add a passing-build badge until a workflow exists and passes.

Future screenshots and animations must represent the actual original application. Do not label concept art as a working interface. A current-tree cleanup does not erase inherited history.
