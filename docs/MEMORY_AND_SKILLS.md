# Selective memory and learned skills

## Goal

Toby should remember enough to be useful without building an ever-growing archive of everything the user says. Long-term memory stores useful knowledge and reusable procedures. It does not need continuous model retraining.

Separate an optional activity history from the material sent into reasoning context. A user may retain a diagnostic log without requiring every future response to ingest it.

## Memory layers

| Layer | Example | Default treatment |
| --- | --- | --- |
| Ephemeral conversation | A question and current speech recognition | Session-bounded; no permanent raw audio by default |
| Active task state | Current regression failure and next step | Persist until resolved, then compact |
| Explicit preference | “Use brief spoken updates.” | Durable until corrected or removed |
| Inferred preference | User often chooses one editor | Candidate with confidence; not an unconditional rule |
| Project knowledge | Repo path, chosen session, environment | Scoped and checked when stale |
| Computer knowledge | WSL distro and tool locations | Machine-scoped; revalidate before dependent actions |
| Skill | Verified project setup procedure | Versioned; prerequisites checked before use |

## Memory lifecycle

At a task boundary or meaningful correction, extract candidate facts. Evaluate usefulness, scope, evidence, sensitivity, duplication, and expected lifespan. Merge compatible records and mark superseded records. Retrieve only relevant facts for the current request.

Every durable record carries category, subject, value, scope, source reference, confidence, created/updated time, last-confirmed time, and expiry or review rule. A user correction is a new authoritative revision, not a competing equally weighted guess.

A compact preference budget and task-context budget prevent accumulation from becoming prompt bloat. Start with exact and full-text retrieval. Add embeddings only if measured retrieval failures justify them; embeddings are an index, not a solution to stale or contradictory knowledge.

## Authority and scope

Explicit user instructions outrank inferred behavior. Current task instructions can override a standing preference for that task. Project preferences do not automatically become global ones. Machine facts do not transfer to a second computer without validation.

“Always ask before sending email” is a preference that affects a workflow. “This draft is approved” is an authorization for a concrete draft, not a durable permission to send any future message. Memory must not silently broaden authorization.

A single choice can create a candidate observation. Repeated consistent choices can justify a proposed preference. Toby should offer a compact correction path rather than announcing every minor update.

## Retention proposal

| Data | Proposed first-release policy |
| --- | --- |
| Raw microphone audio | Transient processing buffer; recording only with explicit opt-in |
| Full recognized utterances | Session-bounded by default; optional user-selected history |
| Screenshots | Transient observations; diagnostic retention opt-in |
| Completed task summaries | Bounded retention with configurable review/removal |
| Preferences and environment facts | Keep until revoked, superseded, or stale |
| Skill documents | Keep active versions and a bounded revision history |
| Logs | Rotated, redacted, size-limited |

Exact durations are a settings/release decision. Do not claim secure forensic erasure from SQLite backups or storage hardware. Normal forget behavior should remove active retrieval records and derived indexes, and explain any retained user-managed backups.

## Inspection and correction

Provide “What do you remember?”, “Forget this”, “That only applies to this project”, “Replace that preference”, and “Export my memory.” The UI presents source, scope, and age, not just a mysterious generated paragraph.

Export preferences and skills separately from credentials and optional activity logs. Imports require conflict resolution and should not silently override newer explicit facts.

## Skill structure

| Field | Meaning |
| --- | --- |
| Name and purpose | Human-readable routine and intended result |
| Trigger | Requests and contexts where it applies |
| Scope | Project, application, machine, or general |
| Prerequisites | Environment and authority needed |
| Steps | Parameterized actions with expected observations |
| Success checks | Observable conditions proving completion |
| Stop conditions | Missing target, changed environment, unsafe ambiguity |
| Recovery | Permitted retry or rollback behavior |
| Evidence | Verification run and source task |
| Version and state | Candidate, verified, deprecated, or blocked |

## Learning a skill

Observe a completed routine and draft a candidate. Remove incidental values and secrets; parameterize reusable inputs. Have the user review important assumptions. Verify in a suitable controlled scenario before granting trusted status.

Promoting a skill does not automatically authorize its future actions. Runtime checks still apply. If an app layout, tool version, or working directory changes, check compatibility. A failed run can demote the skill or create a revised candidate; it must not overwrite the last known-good version without evidence.

A skill is not arbitrary model-authored executable code. Prefer declarative steps using registered tools in the first release. Scripts, plugins, and downloaded skills need a separate distribution and trust model later.

## Acceptance tests

Verify duplicate merging, project/global scope separation, explicit correction precedence, stale paths, deletion from retrieval, bounded context, safe exports, and a skill invalidated by an environment change. Include a negative case where a failed routine never becomes verified.
