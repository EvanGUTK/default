# Decisions, assumptions, and open questions

## Confirmed direction from the owner

| Decision | Consequence |
| --- | --- |
| Voice-first desktop companion | Speech drives conversation/actions without manual transcript transfer |
| Phone optional | Desktop remains useful and complete without mobile |
| Users name their assistant | Project brand and wake phrase/name configuration are distinct |
| Computer observation and mouse control | Native capture/input adapter required |
| Animated eyes follow attention/cursor | Gaze driven by actual observation/action state |
| Claude/Codex connections | Session registry and provider-specific adapters required |
| Status questions do not prompt workers | Observation must be separate from task dispatch |
| Selective memory and learned skills | Scoped records and verified procedure lifecycle |
| Independently authored code and visuals | No inherited implementation/assets in the blueprint tree |
| Detailed blueprint before code | Planning branch contains documentation only |

## Proposed decisions awaiting milestone acceptance

Windows first; Tauri/React/TypeScript/Rust; SQLite and full-text retrieval; local speech; one managed agent before broad attachment; LAN phone before remote relay; one foreground action lease; declarative skills before executable skill plugins.

Toby-desktop is the proposed repository name. The user-facing assistant name remains configurable.

## Open product questions

| Question | Working default | When it must be resolved |
| --- | --- | --- |
| Final public brand | Toby as temporary project brand | Before public launch and domain/trademark work |
| Software license | Unselected for new Toby work | Before accepting external code contributions/distribution |
| First reasoning backend | Supported user-supplied access | Foundation provider proof |
| First coding adapter | Most reliable supported protocol | Foundation session proof |
| Wake-word engine | Push-to-talk fallback | Before custom wake claims |
| Voice engine/distribution | Evaluate local candidates | Before bundled release |
| Remote-phone funding | LAN-only first | Before operating a relay |
| Existing terminal support | Explicit capability levels | Before advertising arbitrary attachment |
| Memory retention durations | Bounded, configurable | Before pilot release |
| Cross-platform expansion | Separate future adapters | After Windows pilot evidence |

## Risk register

| Risk | Impact | Mitigation/evidence |
| --- | --- | --- |
| Provider access changes | Sign-in or inference unavailable | Adapter boundary and pre-release eligibility checks |
| Unsupported live terminal attachment | Wrong input or incomplete state | Managed sessions first; label capabilities |
| Stale screenshots | Wrong click | Observation age/geometry checks and reobserve |
| Mixed-DPI geometry | Misplaced cursor | Recorded monitor test matrix |
| Speech errors | Wrong intent or target | Visible target, correction, push-to-talk/text fallback |
| Physical-input conflict | User fights pointer | Foreground lease and local stop |
| Prompt injection in observed content | Unwanted action | Treat page/terminal content as evidence, not user authority |
| Memory overreach | Wrong preference or authorization | Scope, provenance, correction, no permission promotion |
| Skill drift | Routine fails after change | Prerequisites, versions, verification |
| Dependency license mismatch | Distribution blocked | Engine/model/voice/license review before bundle |
| Mobile reconnection | Duplicate or stale action | IDs, freshness, no blind replay |
| Scope growth | Large fragile codebase | Vertical slices and milestone gates |

## Historical corrections

The inspirational repository actually declares The Unlicense. An earlier inability to load it was not evidence of no license. The fork's README and LICENSE were inspected through the GitHub connection.

The blueprint is original work, but it remains in a fork with inherited history. Neither a new name nor a clean branch proves independent repository ancestry.

No fully implemented capability, model benchmark, live speech measurement, desktop test pass, or provider entitlement has been claimed by this plan.
