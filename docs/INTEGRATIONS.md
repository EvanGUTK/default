# AI providers and coding sessions

## Two different connections

Toby's conversational reasoning backend and the coding agent it directs are separate integrations. A user may use one supported model to converse with Toby and another provider's coding agent to perform development work. Both need their own supported authorization and usage accounting.

OAuth identifies and authorizes a user according to granted scopes. It does not universally convert a chat subscription into API credits. Screen understanding, voice services, model inference, and coding-session usage may have different availability and limits.

## Proposed provider matrix

| Connection | Intended role | Current planning position |
| --- | --- | --- |
| OpenAI supported plan sign-in | Eligible reasoning requests | Official documentation describes open-source plan usage; register and test required scopes and eligible requests |
| OpenAI API key | Direct reasoning/tool requests | User-billed route; verify selected model capabilities and budget |
| Codex app-server | Managed coding conversations/events | Prototype documented protocol, auth, approvals, cancellation, reconnect |
| Unmodified Claude Code | User's coding worker | User authenticates in Anthropic's own flow; test supported integration without extracting tokens |
| Claude API/Agent SDK | App-controlled reasoning or coding workflows | Use documented authentication; resolve eligibility before offering subscription-backed app behavior |
| Local model | Optional conversational/visual reasoning | Evaluate output quality, tool reliability, memory use, and responsiveness |
| Other providers | Future adapters | Require a documented capability and authentication contract |

Anthropic's technical/legal documentation restricts third-party Claude.ai login and credential intermediation. Its support article also describes existing SDK/CLI usage drawing from subscription limits. This is an integration eligibility question, not a blanket permission for Toby to offer Claude subscription inference. The conservative launch path is the user's unmodified authenticated CLI or appropriately billed API access; revalidate the exact proposed SDK mode before shipping.

OpenAI plan sign-in also requires a valid supported flow. A successful identity login alone does not authorize AI requests or grant access to the user's prior ChatGPT conversations.

## Managed versus existing sessions

| Mode | What Toby can reasonably promise after testing | Limitation |
| --- | --- | --- |
| Managed structured session | Start task, correlate events, receive completion, handle supported approval requests | Depends on provider protocol and version |
| Managed terminal session | Observe captured output and send input to the terminal Toby owns | Terminal text is less structured and may contain UI control sequences |
| Existing terminal observation | Read available visible or exported information | Does not imply control over hidden history or session lifecycle |
| Existing terminal input | Send an instruction after explicit target/focus validation | Can hit the wrong process; idle/readiness needs reliable evidence |
| Provider-supported resume | Resume an eligible saved conversation | Resuming may create a new process; it is not necessarily attaching to the original live terminal |

Start with one managed session. Later offer user-selected existing sessions with a clearly displayed capability level. A session connection should expose whether Toby can observe, instruct, interrupt, resume, or handle approvals.

## Adapter contract

An adapter normalizes provider-specific behavior into a registry entry and event stream. Registry fields include display name, provider, native session ID, project, working directory, host environment, connection mode, capabilities, auth state, worker state, and last observation time.

Events should represent accepted task, output, requested user input, completion, failure, disconnection, and reconnect. Preserve provider-native IDs to investigate discrepancies. Unsupported states remain unknown rather than being guessed.

A status question reads the event store and may request a fresh observation. It must not submit a worker turn. An instruction produces a dispatch operation with its target and operation ID. Interrupting a worker requires that adapter's supported cancellation semantics.

## Routing and corrections

Resolve an explicitly named session first, then the selected project session, then conversational references when unambiguous. If two candidates remain, ask the user to choose. The chosen target is shown before meaningful dispatch.

A correction can update a queued task, interrupt a supported active task, or become a follow-up instruction. Toby should explain which occurred. “Tell it to stop” is not complete until the worker's stop state is observed or the limitation reported.

## Credentials

Prefer provider-owned authentication where available. Store Toby-managed credentials using OS protection, and reference them from the settings model. Never read another application's token cache to impersonate it. Never include credentials in task transcripts, phone payloads, screenshots for diagnostics, or Git commits.

Phone pairing establishes Toby device access, not provider login. The desktop owns provider credentials and enforces request authority. Revoking a phone must invalidate its ability to issue future actions.

## Budgets and failure states

Distinguish rate limiting, expired authentication, insufficient entitlement, network loss, provider failure, worker failure, and observation failure. Keep the user's task checkpoint and explain the actual blocking condition.

Show which service incurs usage and allow configurable limits where measurable. Cache read-only recent status locally. Avoid recurring model calls just to animate a pet or poll an unchanged session.

## Integration acceptance

The prototype must demonstrate one task dispatched once, an observed completion, a status question with no new worker turn, a correction, an interruption, a disconnected observer, and an auth refresh without exposing tokens. The adapter must survive a supported client restart or clearly mark recovery as unavailable.
