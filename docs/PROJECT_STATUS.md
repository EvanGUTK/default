# Toby — Project Status

**Date:** 2026-10-13
**Branch:** docs/toby-product-blueprint
**Stage:** M0 Foundation Proofs complete (pending Validator sign-off)

---

## What has been done

### Repository and planning

- Original product blueprint authored across 11 documents (README, PRODUCT, ARCHITECTURE, STACK, INTEGRATIONS, MEMORY_AND_SKILLS, COMPANION_AND_UX, HIERARCHY, ROADMAP, REPOSITORY_PLAN, DECISIONS, PROVENANCE, SOURCES)
- AGENTS.md established with atomic commit rule, worktree policy, and engineering constraints
- CLAUDE.md created so Claude Code agents automatically load project rules
- Worktree policy updated: one worktree per agent per active branch whenever more than one agent works the same branch
- Chief (Hermes) / Validator / Specialist hierarchy operational via Herdr multi-agent setup
- Done-signal protocol established: agents notify Chief via `herdr agent prompt hermes "DONE: ..."` on completion

### M0 Foundation Proofs — spike branches pushed

Four spikes ran in parallel, each in an isolated worktree on its own branch.

#### spike/m0-windows-capture
**Status: complete**
- Listed all visible top-level windows with HWND, geometry, and DPI scale factor
- Captured a window frame via Windows Graphics Capture API (saved as PNG)
- Read accessible name via UI Automation (IUIAutomation)
- Synthesized mouse move + click and keyboard type into Notepad via SendInput
- All observed windows at 96 DPI (1.00x scale)
- Findings: docs/M0_WINDOWS_CAPTURE_FINDINGS.md
- Open: mixed-DPI monitor configuration untested; repository security scanning disabled

#### spike/m0-speech-deps
**Status: complete**
- whisper.cpp integrated via whisper-rs (Unlicense)
- base.en model: ~0.3–0.6 s per 2–4 s utterance (encoder window shrunk); 1.9 s unshrunk
- small.en model: ~1.0 s (shrunk); 4.8 s unshrunk — required for technical vocabulary
- Technical vocabulary (e.g. "cargo build --release") requires small.en plus a hint prompt
- Kokoro-82M recommended as TTS candidate (latency unmeasured)
- Dependency license table completed (TypeScript is Apache-2.0, not MIT as assumed)
- Findings: docs/M0_SPEECH_FINDINGS.md, docs/M0_DEPENDENCY_REVIEW.md
- Open: all numbers from loaded WSL2 machine with synthetic speech; idle-machine baseline, Windows-native run, and live-mic transcription not yet measured

#### spike/m0-provider-session
**Status: complete**
- Codex app-server IPC protocol documented and one normalized session event captured
- Claude CLI (`claude` binary) authenticated and replied "verified" to test prompt
- Reconnect behavior documented
- Integration plan: Codex app-server + Claude CLI (no raw API key required)
- Findings: docs/M0_PROVIDER_FINDINGS.md
- Open: none against the agreed integration plan

#### spike/m0-desktop-shell
**Status: complete with one partial**
- Tauri 2 application builds and runs on Windows
- Transparent always-on-top overlay window verified (top-right, 300×80 px)
- Settings window open/close and position restore verified
- Global keyboard shortcut Ctrl+Shift+S verified (stops/hides overlay)
- System tray icon present
- Findings: docs/M0_DESKTOP_SHELL_FINDINGS.md
- **Partial:** tray right-click → Quit → clean exit (code 0) not verified — Windows UI Automation cannot invoke the tray context menu programmatically; manual verification or Win32 PostMessage approach needed

---

## M0 gate result

Gate requirement: at least one viable route for speech, reasoning, observation, execution, and agent state — with unresolved limits documented rather than hidden.

| Proof area   | Result  | Evidence |
| --- | --- | --- |
| Speech       | Pass    | whisper.cpp base.en sub-600 ms on WSL2; limits documented |
| Reasoning    | Pass    | Codex app-server + Claude CLI confirmed |
| Observation  | Pass    | WGC frame capture + UIA accessible name confirmed |
| Execution    | Pass    | SendInput mouse + keyboard confirmed in Notepad |
| Agent state  | Pass    | Codex session event normalized and reconnect documented |
| Shell        | Partial | Tray/overlay/settings/shortcut work; tray Quit unverified |

**M0 is passable.** Validator sign-off is pending. One bounded fix task remains before full closure: verify tray Quit / clean-exit via Win32 PostMessage or manual test documentation.

---

## What is left — core application milestones

### M1 — Desktop conversation and original companion
Build push-to-talk, local recognition (whisper.cpp, proven in M0), a supported reasoning adapter (Codex/Claude CLI, proven in M0), local TTS playback (Kokoro-82M candidate), conversation text panel, settings, and original placeholder companion animation.

Key work: apps/desktop UI shell, speech pipeline crate integration, Tauri command boundaries, companion animation state machine, listening/thinking/speaking states.

Acceptance: conversational loop on a CPU-capable machine; no copy-paste step; stop speech works locally; typed fallback when audio fails.

### M2 — One managed coding session
Session registry, one adapter (Codex app-server, proven in M0), normalized events, task state machine, project mapping, named target display.

Key work: crates/runtime task states, adapters/codex session adapter, status reads that create zero additional worker turns.

Acceptance: start a task; ask status; ask again; correct; observe blocked; cancel; reconnect after observer loss.

### M3 — Verified desktop and terminal actions
Observation snapshots, target resolution, execution broker, foreground lease, coordinate freshness, stop controls, verified outcomes. Narrow task set: open application, locate control, type text, scroll, simple terminal operation.

Key work: crates/platform-windows action layer (builds on M0 WGC/UIA/SendInput spike), execution broker in crates/runtime, foreground lease with physical-input revocation.

Acceptance: representative actions succeed on recorded configurations; geometry changes trigger reobservation; human takeover stops pending input; no duplicate submission on retry.

### M4 — Selective memory and verified skills
Scoped facts, provenance, confidence, correction, duplicate merging, retrieval budgets, export/forget controls, completed-task summaries, declarative skill documents.

Key work: crates/memory SQLite schema, crates/skills lifecycle, retrieval budget enforcement.

Acceptance: explicit corrections dominate inferred behavior; unrelated project context excluded; failed routines never become trusted; deletion removes active retrieval entries.

### M5 — Multiple sessions and environments
Additional proven adapters, capability levels for existing terminals, per-project defaults, independent worker tasks, structured environment records. Desktop action lease stays singular.

Key work: adapters/claude second adapter, session routing logic, Windows/WSL environment records.

### M6 — Optional phone companion
LAN-only pairing, encrypted authenticated transport, revocation, request IDs, state resynchronisation. Reuse desktop-owned provider connections and memory.

Key work: apps/mobile React Native client, phone gateway crate, pairing and challenge flow.

### M7 — Windows pilot and release readiness
Signed release, installer, dependency and asset provenance review, resource measurement, restart/update/uninstall testing, pilot feedback collection.

---

## Partials and open items

| Item | Spike | Impact | Suggested resolution |
| --- | --- | --- | --- |
| Tray Quit / clean-exit unverified | desktop-shell | M0 partial | Win32 PostMessage to tray HWND or manual test doc — bounded fix task |
| Mixed-DPI untested | windows-capture | M3 risk | Add second monitor to test matrix before M3 desktop actions |
| Speech latency on idle machine | speech-deps | M1 planning | Re-run whisper.cpp on idle Windows-native before committing to latency targets |
| Speech latency Windows-native | speech-deps | M1 planning | WSL2 numbers are conservative; native may be faster or expose different constraints |
| Live-mic transcription untested | speech-deps | M1 gate | Required before M1 push-to-talk acceptance |
| Kokoro-82M TTS latency unmeasured | speech-deps | M1 planning | Measure before committing to TTS choice in M1 |
| TypeScript license Apache-2.0 | speech-deps | M7 | Update license table in dependency review; no blocker for development |

---

## Repository layout so far

```
companion/
  AGENTS.md               — universal agent rules (Codex reads this)
  CLAUDE.md               — universal agent rules (Claude Code reads this)
  README.md               — project overview and blueprint index
  docs/
    ARCHITECTURE.md
    COMPANION_AND_UX.md
    DECISIONS.md
    HIERARCHY.md
    INTEGRATIONS.md
    MEMORY_AND_SKILLS.md
    M0_DEPENDENCY_REVIEW.md     (spike/m0-speech-deps)
    M0_DESKTOP_SHELL_FINDINGS.md (spike/m0-desktop-shell)
    M0_PROVIDER_FINDINGS.md     (spike/m0-provider-session)
    M0_SPEECH_FINDINGS.md       (spike/m0-speech-deps)
    M0_WINDOWS_CAPTURE_FINDINGS.md (spike/m0-windows-capture)
    PRODUCT.md
    PROJECT_STATUS.md           (this file)
    PROVENANCE.md
    REPOSITORY_PLAN.md
    ROADMAP.md
    SOURCES.md
    STACK.md
```

Spike branches (not yet merged — pending Validator sign-off):

- spike/m0-desktop-shell    (last commit: desktop-shell agent)
- spike/m0-windows-capture  (last commit: cd confirmed at 5b5dd4a area)
- spike/m0-speech-deps      (last commit: speech-deps agent)
- spike/m0-provider-session (last commit: 5b5dd4a)

---

## Next steps

1. **Validator review** — independent check of all four spike branches against M0 acceptance scenarios
2. **Fix task** — verify tray Quit/clean-exit (desktop-shell partial)
3. **Owner M0 sign-off** — confirm gate passes or request additional evidence
4. **M1 planning** — Chief presents M1 goal, behavior, constraints, dependencies, acceptance scenarios, and risks for owner approval before any M1 implementation begins
