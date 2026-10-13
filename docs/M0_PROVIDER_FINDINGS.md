# M0 provider and session spike findings

Branch `spike/m0-provider-session`. Run 2026-10-13 on WSL2 Ubuntu, Node 24.21, `claude` 2.1.296, `codex-cli` 0.160.0. Everything below was observed in this run unless marked **not tested**.

## Summary

| Question | Result |
| --- | --- |
| Authenticate to Anthropic with an API key and get a reply | **Not demonstrated.** No `ANTHROPIC_API_KEY` or `OPENAI_API_KEY` was set. The client exists and its failure paths were exercised; the success path was not. |
| Get a reply from the user's unmodified signed-in `claude` CLI | Demonstrated: reply `verified`. |
| Capture a normalized lifecycle event from a running Codex process | Demonstrated over stdio and loopback websocket. |
| Observer reconnect | Demonstrated; behavior depends on whether the thread is persisted (see below). |

## Part A: provider spike

Code: `adapters/claude/provider-spike.ts` (API key) and `adapters/claude/cli-spike.ts` (signed-in CLI). Run with `node <file>`; Node 24 strips the types, so there is no build step. No type checker was available (`tsc` is not installed), so the code is verified only by running it.

### Provider tested

Anthropic only. Neither key was in the environment, so no OpenAI path was written.

### API-key path (`provider-spike.ts`)

- Auth: `x-api-key: <key>` read from `ANTHROPIC_API_KEY`. Required headers per the request it sends: `x-api-key`, `anthropic-version: 2023-06-01`, `content-type: application/json`. The endpoint is `POST https://api.anthropic.com/v1/messages`.
- Model: defaults to `claude-haiku-5-5`, overridable with `TOBY_CLAUDE_MODEL`. Whether a given key can use that model is **not tested**.
- Missing key: prints a clear message, exit 2.
- Rejected key: a deliberately invalid dummy key sent to the real endpoint returned HTTP 401 `invalid x-api-key` in 285 ms, with a `request-id` header, exit 2. This confirms the endpoint, headers and error shape, and that failures are reported clearly.
- Rate limits: the 401 response carried no `anthropic-ratelimit-*` headers, and no 429 was observed. The client prints any such headers and `retry-after` when a successful or limited response arrives, so the first real run will record them. Actual limits are **unknown**.
- Success latency: **not measured**; the only timing is the 401 round trip above.

### Signed-in CLI path (`cli-spike.ts`)

`claude -p "Reply with the single word: verified" --output-format json --no-session-persistence --tools "" --disable-slash-commands`.

- Result: reply `verified`, `is_error: false`, one turn, exit 0.
- Latency: 3.1 s API time, 8.3 s reported duration, 21.8 s wall clock. The gap is Claude Code start-up including the user's own hooks, so it is not a figure for Toby's reasoning latency.
- Model used: `claude-sonnet-5-5`, chosen by the CLI's own configuration.
- Auth: the CLI reported a claude.ai subscription sign-in (`authMethod: claude.ai`). The spike only runs the CLI; it never reads its token cache.

### Eligibility and scope constraints

- The CLI reply was drawn from the user's subscription limits. `docs/INTEGRATIONS.md` already flags that third-party use of Claude.ai login is restricted; this spike used the user's own unmodified CLI and is not evidence that Toby may offer subscription-backed inference itself. That must be re-validated before release.
- Pure API-key access is the billed alternative and remains unverified until a key is supplied.
- Codex, by comparison, authenticated through its own ChatGPT sign-in: the app server reported `authMode: chatgpt` without any key from us, and a model turn completed (Part B). That also demonstrates a working Codex reasoning reply through a supported sign-in.

### To finish Part A

Run `ANTHROPIC_API_KEY=... node adapters/claude/provider-spike.ts`, and append the status, latency, model and rate-limit headers it prints to this file.

## Part B: Session Event Spike

Code: `adapters/codex/session-spike.ts`. Flags: `--transport stdio|ws`, `--reconnect` (ws only, drops the observer mid-turn), `--persist` (persisted instead of ephemeral thread; the thread is deleted again at the end).

### Protocol endpoint used

`codex app-server` JSON-RPC, one JSON object per line, **without** a `"jsonrpc"` field.

- stdio: `codex app-server --listen stdio://` (default transport). Used for the main capture.
- websocket: `codex app-server --listen ws://127.0.0.1:47821`. Used for reconnect tests.
- `--listen` also accepts `unix://`, `unix://PATH` and `off`. Not tested.
- Handshake: `initialize` request with `clientInfo {name, version}`, then an `initialized` notification. Then `thread/start`, then `turn/start` with `input: [{type: "text", text, text_elements: []}]`. Related methods seen in the generated schema (`codex app-server generate-json-schema`): `turn/steer`, `turn/interrupt`, `thread/resume`, `thread/read`, `thread/loaded/list`, `thread/delete`. Only the ones named above were exercised.
- The spike started with `approvalPolicy: "never"` and `sandbox: "read-only"` in a temp working directory. The task `echo hello world` ran inside that sandbox and returned `hello world`.
- A user-managed Codex app-server daemon (`--listen unix://`, version 0.162.1) was already running. The spike did not attach to it and did not touch it; it spawned and terminated its own server. Attaching to a user's live sessions is a separate capability question.

### Quirk: early requests are silently dropped on stdio

Requests written within the first few hundred ms after spawn got no reply and no error. With a delay of 0 ms there was no response; at 500 ms and 3000 ms the response came. `session-spike.ts` therefore retries `initialize` (1.5 s per attempt, 6 attempts). A real adapter needs this or a readiness signal.

### Event types observed

Provider notifications seen across the stdio and ws runs: `thread/started`, `thread/status/changed`, `turn/started`, `turn/completed`, `item/started`, `item/completed`, `item/agentMessage/delta`, `item/commandExecution/outputDelta`, `thread/tokenUsage/updated`, `account/rateLimits/updated`, `account/updated`, `hook/started`, `hook/completed`, `mcpServer/startupStatus/updated`, `remoteControl/status/changed`, `thread/goal/cleared`, `thread/closed`, `warning`, `deprecationNotice`.

Items seen: `userMessage`, `agentMessage`, `commandExecution`.

Mapping in the spike (anything unmapped is kept as `provider.<method>` rather than guessed):

| Provider method | Normalized `event_type` |
| --- | --- |
| `thread/started` | `session.started` |
| `turn/started` | `task.accepted` |
| `item/agentMessage/delta`, `item/commandExecution/outputDelta` | `task.output` |
| `item/completed` for an `agentMessage` | `task.output.final` |
| `turn/completed` (status `completed` / `failed` / `interrupted`) | `task.completed` / `task.failed` / `task.interrupted` |
| server request needing an answer (e.g. approval) | `task.input_requested` (the spike answers with an error and never approves) |

Not observed: an approval request, a failed turn, an interrupted turn. Those mappings are written but **not tested**, as are `turn/interrupt` and `turn/steer`.

### The normalized event captured

`timestamp` is the observer's receive time; the provider's own time is in `raw_payload.emittedAtMs`.

```json
{
  "session_id": "01a1367b-d08a-7e50-b926-dbb5d2386cd9",
  "task_id": "01a1367b-d263-7361-b90c-4d9cb86f7bab",
  "event_type": "task.accepted",
  "timestamp": "2026-10-13T18:17:26.901Z",
  "raw_payload": {
    "method": "turn/started",
    "params": {
      "threadId": "01a1367b-d08a-7e50-b926-dbb5d2386cd9",
      "turn": {
        "id": "01a1367b-d263-7361-b90c-4d9cb86f7bab",
        "items": [],
        "itemsView": "notLoaded",
        "status": "inProgress",
        "error": null,
        "startedAt": 1791915446,
        "completedAt": null,
        "durationMs": null
      }
    },
    "emittedAtMs": 1791915446900
  }
}
```

`session_id` is the Codex `threadId` and `task_id` is the turn id. The closing event was `task.completed` with `status: "completed"`. A full run produced about 55 events, and the echo task took 5.2 to 7.4 s end to end.

### Reconnect behavior

Over stdio the connection is the process, so there is nothing to reconnect to. Over websocket:

- **Ephemeral thread** (`ephemeral: true`): when the only observer disconnected mid-turn, the server closed the thread (`thread/closed`). The turn was lost. The second connection saw `thread/loaded/list` containing the id, but `thread/resume` failed with `no rollout found for thread id`, and `thread/read` failed with `thread not loaded`.
- **Persisted thread** (`--persist`): the turn kept running after the observer dropped. A new connection did `initialize`, `thread/resume` (ok), then received the rest of the stream live up to `turn/completed`. Earlier events were **not** replayed: the second observer never saw `task.accepted`. `thread/read` with `includeTurns` returned the stored thread afterwards.
- Consequence for Toby: use persisted threads and rebuild history with `thread/read`, rather than relying on event replay. Resuming after a restart of the app server itself is **not tested**.

### Session limits and auth scope

- Auth: the app server used the user's own Codex sign-in (`authMode: chatgpt`). No credential was supplied, read or logged by the spike.
- The loopback websocket accepted a connection with no token. Any local process can therefore attach to a ws app server on that port while it runs. The spike used a fixed port for a few seconds on 127.0.0.1; a real adapter should prefer stdio or a socket with filesystem permissions and flag this to Chief/security.
- Max concurrent tasks, idle timeout, token lifetime and refresh behavior: **not tested**.

### Cleanup

The spike spawns the server in its own process group and terminates that group (SIGTERM, then SIGKILL after 5 s) before exiting, and removes its temp directory. After the final stdio and ws runs there was no `codex app-server` process of the spike left and no `/tmp/toby-codex-spike-*` directory. The persisted test thread was removed with `thread/delete`, and a search of `~/.codex/sessions` for its prompt text found nothing (other Codex storage locations were not searched). One earlier ws run left its server process alive for a short time after the script returned; the process-group cleanup was added after that.

## Open items for Chief

1. Part A success path needs a real `ANTHROPIC_API_KEY` (or a decision to rely on the signed-in CLI path for M0).
2. Decide whether to attach to the user's existing managed Codex daemon or always spawn a private app server.
3. Loopback websocket has no authentication; choose stdio or a permissioned socket for the real adapter.
4. Interrupt, approval and failure events are mapped but unproven.
