# Product vision

## Purpose

Toby should make computer work conversational. A person can state a goal, ask questions while work proceeds, correct an instruction, review a draft, or take control directly. The assistant should retain enough continuity that the user does not need to repeat their environment and preferences every session.

The product combines a desktop companion, a reasoning runtime, computer-operation tools, and connections to coding agents. Its distinctive value is continuity across these components: the same companion can explain a screen error, start tests in Codex, observe progress, and resume a familiar workflow.

## Product identity

“Toby” identifies the project during development. The user's assistant name is a separate setting, used in speech and conversation. A custom display name does not automatically create a reliable wake word. First release should offer push-to-talk for every name and only enable a chosen wake phrase after a recognition check.

Windows is the initial desktop target. WSL2 is an execution environment connected from Windows, not a separate graphical desktop implementation. Mac and Linux support require their own observation and input adapters later.

The companion should be friendly, concise, and honest about uncertainty. It can learn communication preferences without pretending to know a user's internal thoughts or emotions.

## Primary users and jobs

| User context | Job to accomplish |
| --- | --- |
| Developer with coding agents | Direct work and obtain evidence-based updates without repeatedly switching terminals |
| General desktop user | Complete routine application and browser tasks through conversation |
| User working away from keyboard | Ask a question, interrupt a task, or hear a brief update hands-free |
| User with several projects | Route instructions to the right environment while preserving task context |
| User away from desktop | Check status and review a prepared action through an optional phone client |

The first pilot should target a developer on one Windows machine using one coding session. General computer control grows from a measured set of reliable applications.

## Conversation behavior

Each utterance is interpreted in the context of active tasks and the selected session. Toby distinguishes new work, a status question, a correction, cancellation, a remembered preference, and a request to repeat or explain.

“Update me” reads an observation. “Tell Claude to focus on login” sends an instruction. “Stop talking” stops speech; “stop the task” cancels the task; “stop everything” stops speech and pending actions. Ambiguous cancellation should immediately pause input and clarify the remaining task scope.

The interface exposes the target project/session before dispatch. If the target cannot be resolved confidently, Toby asks a short question. It should not guess between two similarly named projects simply to appear fluid.

Spoken answers remain brief by default. Detailed plans, logs, errors, and diffs belong in the panel. The user can request more detail at any time.

## Representative journeys

### Resume a project

The user says, “Let's continue regression.” Toby retrieves the last compact task summary, identifies the mapped session and working directory, checks that the session is available, and describes the next intended step. It sends the instruction once and tracks the returned task identifier. A later request for progress reads the worker's observed state rather than creating another task.

### Operate a desktop application

The user asks Toby to open a browser and prepare an email. Toby identifies the relevant account and recipient, observes the application, fills the draft, and presents an editable preview. It sends only after authorization that applies to that exact draft. If the page changes while waiting, it rechecks the recipient and draft before sending.

### Install into two environments

The user requests installation in Windows and WSL2. Toby records two separate subtasks. It checks existing versions, chooses an official distribution source, communicates required privileges, performs each authorized installation, and verifies version/output separately. Partial success produces a partial-success report; it does not imply both installations succeeded.

### Learn a routine

The user performs or requests a project setup that completes successfully. Toby identifies repeatable steps and proposes a scoped routine. A candidate skill must describe prerequisites and success checks. It becomes trusted only after verification. If a prerequisite changes later, Toby pauses or revises the skill.

### Use the phone

A paired phone displays the same task state and assistant identity. The user asks for a status update or approves a draft. The desktop validates freshness and authority and performs the action. Offline or asleep desktops are shown as unavailable; the phone must not display a queued action as completed.

## Scope by maturity

| First useful release | Later expansion |
| --- | --- |
| Local speech input and output | Optional premium cloud speech |
| Original character with clear task states | Additional original themes and animation packs |
| One supported reasoning backend | Multiple interchangeable providers |
| One managed coding session | Multiple sessions and project routing |
| Known desktop/browser tasks | Broader app coverage and robust recovery |
| Editable preference memory | Verified skill learning and richer retrieval |
| Desktop-only operation | LAN phone pairing, then evaluated remote connectivity |

No first-release promise of universal PC control, arbitrary terminal attachment, perfect voice recognition, background mobile wake words, or autonomous recovery from every failure.

## Product success

Measure completion of representative tasks, mistaken routing, duplicate dispatch, interruption responsiveness, speech accuracy, time to an honest update, and frequency of user corrections. User trust depends on visible evidence and controllability as much as task speed.

The release demonstration should include: a spoken task, a working agent, a status question that does not create a second agent turn, a desktop click with verified outcome, user takeover, a memory correction, and a restarted session that preserves the compact task context.

## Business model

The proposed baseline is a free desktop app with no mandatory Toby subscription. Users supply an eligible provider account, API access, or supported local model. Provider limits and charges still apply. Local speech reduces recurring voice-service costs but does not eliminate compute, hosting, signing, support, or relay costs.

Phone relay service, paid voices, and commercial distribution remain product decisions. Do not advertise unlimited free inference or guaranteed reuse of every AI subscription.
