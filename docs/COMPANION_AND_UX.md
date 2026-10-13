# Companion, cursor, and interface

## Visual direction

Create an original character with a recognizable silhouette, readable eyes, and restrained motion. References inform the broad experience only. Do not trace another character, reuse sprites/GIFs/fonts, or imitate distinctive visual assets.

The repository presentation follows the user's GoodyOS reference in structure: a centered identity, short promise, clear principles, and readable tables. Toby's palette and character remain original. Proposed palette: midnight background, violet identity accent, cyan observation accent, warm amber for waiting, and mint for verified completion. Status must also have text/icons; color alone is insufficient.

The character is the compact daily surface. Full settings and detailed work live in an application window. Reduced-motion mode and a nonanimated tray-only mode are first-class options.

## Attention model

Maintain an attention target separate from task and speech state. The attention target can be the user, an observed screen region, Toby's intended click, an active worker session, or no target.

The eyes reflect this state. They do not imply a screenshot is being analyzed when capture is unavailable. When Toby points toward an off-screen monitor, show a monitor label or directional cue rather than pretending the character is looking at a visible object.

Smooth gaze interpolation runs locally at UI animation cadence. Capture and model inference happen at their own rates. Eyes following a cursor is presentation behavior; understanding the screen requires actual observations.

## State mapping

| State | Animation direction | Visible explanation |
| --- | --- | --- |
| Idle | Gentle optional movement | Ready |
| Listening | Face toward user, subtle audio response | Listening; live text optional |
| Thinking | Small deliberate motion | Interpreting request |
| Inspecting | Gaze toward observed target | Looking at named window |
| Acting | Gaze tracks intended cursor/action | Current action and target |
| Speaking | Speech animation | Response text available |
| Worker active | Distinct working animation | Named agent/session and latest status |
| Waiting for user | Face user, restrained cue | Exact question or approval |
| Paused | Stable pose | Paused; user has control |
| Failure/unknown | Neutral concerned cue | Actual error or unavailable observation |

Speech and work can overlap. Animation priority should avoid rapidly switching contradictory states; the panel can show both “speaking” and “Claude working” while the main pose emphasizes the current interaction.

## Cursor behavior

Show an optional assistant cursor overlay, target ring, and short action label. A visible overlay is not a second independent Windows input device. The native executor must still manage actual input and focus.

Before a click, glance toward the target and highlight it. During movement, gaze follows cursor position. After a click, react only after checking the outcome. Motion should communicate intent, not introduce gratuitous delays or fake thinking.

Physical user input pauses Toby's foreground action lease. Keep an immediate stop shortcut and clickable pause control. Do not require voice recognition or a cloud request to stop input. Restarting requires clear task context; a stale click is not resumed blindly.

## Main surfaces

| Surface | Contents |
| --- | --- |
| Companion | Current state, attention, quick controls |
| Conversation panel | Spoken exchange, editable text, target session, attachments |
| Activity | Task/subtask status, evidence, latest observation time |
| Review panel | Drafts, proposed changes, exact action to approve |
| Settings | Voice, connections, computer access, memory, skills, appearance, phone |
| Session selector | Named project, provider, environment, capabilities, connectivity |

Do not fill the main interface with protocol IDs, tokens, or infrastructure details. Advanced diagnostics can expose technical evidence when useful.

## Settings detail

Voice settings include microphone, output device, push-to-talk, wake phrase, recognition language, voice, playback speed, and interruption behavior. Test the microphone and selected phrase during setup.

Connections distinguish the conversational brain from working agents. Each agent has a display name, project, environment, and capability level. A terminal is not labeled fully connected merely because it appears on screen.

Computer settings show observation targets and granted actions. Memory displays editable facts by scope. Skills show verification evidence and age. Appearance offers character, size, position, gaze, motion, and reduced motion. Phone shows paired devices, connectivity, and revocation.

## Naming and personality

Store project brand and user-selected assistant name separately. Display-name changes take effect immediately; wake-phrase changes require supported enrollment/testing. A fallback push-to-talk route is always available.

Personality settings describe tone, verbosity, and initiative. They should not obscure uncertainty or change tool authority. The companion can be warm and familiar while reporting “I cannot verify that yet.”

## Optional phone experience

The phone shows the same identity and task truth, with voice input, activity, review, and stop controls. LAN pairing starts from the desktop with an expiring code/QR challenge. Background microphone and continuous wake behavior require platform evaluation and should not be promised.

If both devices are present, avoid simultaneous spoken playback by allowing a selected speaking device. A suspended phone reconnects with sequence-aware task state. Expired approvals require a new preview.

## Accessibility and acceptance

Support keyboard controls, readable status text, captions, scalable text, high contrast, reduced motion, and clear capture/listening indicators. Test eyes/cursor geometry on mixed-DPI monitors and verify the overlay does not intercept unrelated clicks.

A usability review must confirm users can distinguish listening from recording, speaking from working, acting from observing, and paused from completed.
