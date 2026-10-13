# Proposed technology stack

## Status

These are recommendations for the first implementation, not installed dependencies or completed integrations. Pin supported versions after proof-of-concept work. A component's existence does not prove the required behavior, packaging, performance, or provider entitlement.

## Core choices

| Area | Proposed choice | Why | Validation gate |
| --- | --- | --- | --- |
| Desktop shell | Tauri 2 | Native host with web-based settings and companion surfaces | Tray, transparent overlay, click-through, scaling, global shortcut, packaging |
| Interface | React and TypeScript | Explicit state and reusable conversation/settings components | Keyboard navigation, reduced motion, screen-reader labels |
| Local authority | Rust | Fits Tauri host and native Windows adapters | Team proficiency, native API ergonomics, cancellation reliability |
| UI styling | CSS design tokens; optional Tailwind | Consistent colors, spacing, and responsive panels | Avoid styling dependencies before needed |
| App state | Small typed store, candidate Zustand | Separate conversation, task, and animation state | No second source of truth for task completion |
| Contracts | Versioned typed schemas; generated bindings where useful | Stable UI/native/adapter boundary | Compatibility fixtures for protocol changes |
| Speech recognition | Whisper.cpp sidecar | Local recognition with reusable model files | Windows packaging, CPU baseline, names and technical vocabulary |
| Speech activity | Tested local VAD candidate | End-of-utterance detection and interruption | Speech overlap, noise, echo rejection |
| Wake phrase | Pluggable detector, selected by evaluation | Custom name support without locking core to one engine | Model/license availability and false-wake rate |
| Speech output | Engine adapter; Piper candidate | Local playback path | Engine and voice licenses, latency, quality, distribution |
| Memory | SQLite plus FTS5 | Bounded local persistence and exact/full-text retrieval | Migrations, backup, corruption recovery, deletion behavior |
| Credentials | Windows credential protection | Secrets outside UI and logs | Auth refresh, logout, export redaction |
| Logs | Structured local events | Evidence for progress and recovery | Retention budget and redaction |
| Mobile | React Native candidate | Separate iOS/Android client with shared protocol concepts | LAN pairing, mic, secure storage, suspend/reconnect |
| Verification | Rust tests, Vitest, targeted desktop integration fixtures | Match checks to component responsibility | Representative failures and interrupted operations |

## Native Windows tools

Use Windows Graphics Capture for approved screen/window observation, UI Automation for semantic controls, and Win32 input APIs for actions needing keyboard/mouse input. The Rust windows bindings are a proposed access route, subject to a native spike.

A narrow native adapter owns target resolution, geometry, capture, focus, input, and user takeover. Do not give every frontend window direct access to these operations. Elevated windows, UAC secure desktop, password controls, lock screens, and protected applications are compatibility boundaries, not routine bypass targets.

A browser adapter may use Playwright in a managed browser context. Connecting to an arbitrary existing personal browser is a separate capability requiring a supported attachment mechanism. Do not assume all profiles are attachable.

PowerShell processes and WSL2 commands need explicit environment records. Store distro, working directory, shell, process ID, and output provenance. Prefer structured process invocation over constructing layered shell strings.

## Reasoning and agent integrations

The conversational brain uses a provider interface supporting text, structured tool proposals, streaming, image inputs where needed, and cancellation. Provider identity and model identity are configurable independently of the assistant's display name.

Codex app-server is the leading candidate for managed Codex threads and streamed state. Claude integration should use documented mechanisms with an unmodified user-authenticated CLI where supported, or appropriately billed API/SDK access. Eligibility and authentication paths must be tested and rechecked at release.

Do not embed provider OAuth token scraping or assume a ChatGPT plan authorizes every endpoint, model, computer-use capability, or voice service.

## Why this stack

The proposal prioritizes a small distributable desktop shell, local ownership of state, typed boundaries, and a path to original animated UI. It avoids a mandatory Python server, Docker, Kubernetes, cloud database, vector database, and hosted orchestrator for the first pilot.

Speech engines can run as supervised local sidecars without becoming unrestricted automation services. Crash, timeout, restart, model download, and version compatibility must be designed explicitly.

## Alternatives worth preserving

| Alternative | When it becomes attractive |
| --- | --- |
| Electron with TypeScript host | Native bridge or Tauri development overhead blocks the first proof |
| .NET desktop host | Windows-only native APIs are substantially easier for the maintainer |
| Cloud speech | Local voice latency or pronunciation fails the target experience |
| Local multimodal reasoning | A evaluated model meets tool and visual reliability requirements on target hardware |
| Native mobile apps | Background audio or platform-specific interaction needs exceed the shared client approach |

One prototype should select the desktop host. Do not build Tauri, Electron, and .NET applications in parallel.

## Dependency and distribution policy

Review application license, engine license, model license, font license, and voice license separately. Piper's current repository identifies GPL licensing; its candidacy is not approval to bundle it under any desired application license. Decide compatible packaging and obligations before distribution.

Download model files only with user knowledge, verified origin, checksums, clear size requirements, and a removal control. CPU functionality should exist before requiring NVIDIA hardware. Performance claims need a measured reference machine and workload.

The blueprint does not set a final software license. Preserve the upstream provenance separately from future Toby licensing. Signing and installers remain release work, not proof provided by a README badge.
