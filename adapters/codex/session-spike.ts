// M0 spike: capture normalized lifecycle events from a Codex app-server process.
//
// Spawns `codex app-server` (stdio or loopback websocket), runs one tiny task
// in a throwaway read-only thread, normalizes the notification stream into
// { session_id, task_id, event_type, timestamp, raw_payload }, and always
// terminates the process it started. It never touches credentials: the app
// server uses the user's own Codex sign-in.
//
// Usage: node session-spike.ts [--transport stdio|ws] [--reconnect] [--persist] [--prompt "..."]
//   --reconnect  (ws only) drop the observer mid-turn, reconnect, try to resume
//   --persist    use a persisted thread instead of an ephemeral one; the thread
//                this run created is deleted again through thread/delete

import { spawn, type ChildProcess } from "node:child_process";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createInterface } from "node:readline";

const DEFAULT_PROMPT = "Run the shell command `echo hello world` and tell me its output.";
const SLOW_PROMPT = "Run the shell command `sleep 6; echo hello world` and tell me its output.";
const RECONNECT_GAP_MS = 1_000;
const REQUEST_TIMEOUT_MS = 30_000;
const HANDSHAKE_ATTEMPT_TIMEOUT_MS = 1_500;
const HANDSHAKE_MAX_ATTEMPTS = 6;
const TURN_TIMEOUT_MS = 120_000;
const WS_READY_TIMEOUT_MS = 15_000;
const WS_PORT = 47821;
const CHILD_EXIT_TIMEOUT_MS = 5_000;
const CLIENT_INFO = { name: "toby-m0-spike", title: null, version: "0.0.0" };

type Json = null | boolean | number | string | Json[] | { [key: string]: Json };
type JsonObject = { [key: string]: Json };

interface RpcMessage {
  id?: number | string;
  method?: string;
  params?: Json;
  result?: Json;
  error?: { code: number; message: string; data?: Json };
}

export interface NormalizedEvent {
  session_id: string | null;
  task_id: string | null;
  event_type: string;
  timestamp: string;
  raw_payload: Json;
}

interface Transport {
  send(message: JsonObject): void;
  onMessage(handler: (message: RpcMessage) => void): void;
  onClose(handler: () => void): void;
  close(): Promise<void>;
}

const EVENT_TYPE_BY_METHOD: Record<string, string> = {
  "thread/started": "session.started",
  "turn/started": "task.accepted",
  "item/agentMessage/delta": "task.output",
  "item/commandExecution/outputDelta": "task.output",
  "item/commandExecution/terminalInteraction": "task.input_requested",
  "turn/completed": "task.completed",
  error: "task.failed",
};

function asObject(value: Json | undefined): JsonObject {
  return value !== null && typeof value === "object" && !Array.isArray(value) ? value : {};
}

function asString(value: Json | undefined): string | null {
  return typeof value === "string" ? value : null;
}

// Unknown methods keep their provider name under "provider.<method>" rather than
// being guessed into a lifecycle state.
export function normalize(message: RpcMessage, receivedAt: Date = new Date()): NormalizedEvent | null {
  if (!message.method) return null;
  const params = asObject(message.params);
  const turn = asObject(params.turn);
  const item = asObject(params.item);
  let eventType = EVENT_TYPE_BY_METHOD[message.method] ?? `provider.${message.method}`;
  if (message.method === "turn/completed") {
    const status = asString(turn.status);
    eventType = status === "failed" ? "task.failed" : status === "interrupted" ? "task.interrupted" : "task.completed";
  }
  if (message.method === "item/completed" && asString(item.type) === "agentMessage") {
    eventType = "task.output.final";
  }
  return {
    session_id: asString(params.threadId) ?? asString(asObject(params.thread).id),
    task_id: asString(params.turnId) ?? asString(turn.id),
    event_type: eventType,
    timestamp: receivedAt.toISOString(),
    raw_payload: message as unknown as Json,
  };
}

function stdioTransport(child: ChildProcess): Transport {
  const handlers: Array<(m: RpcMessage) => void> = [];
  const closers: Array<() => void> = [];
  if (!child.stdout || !child.stdin) throw new Error("app-server stdio pipes unavailable");
  const stdin = child.stdin;
  createInterface({ input: child.stdout }).on("line", (line) => {
    if (!line.trim()) return;
    const parsed = JSON.parse(line) as RpcMessage;
    handlers.forEach((h) => h(parsed));
  });
  child.on("close", () => closers.forEach((c) => c()));
  return {
    send: (m) => void stdin.write(`${JSON.stringify(m)}\n`),
    onMessage: (h) => void handlers.push(h),
    onClose: (h) => void closers.push(h),
    close: async () => void stdin.end(),
  };
}

async function websocketTransport(url: string): Promise<Transport> {
  const deadline = Date.now() + WS_READY_TIMEOUT_MS;
  for (;;) {
    try {
      const socket = await new Promise<WebSocket>((resolve, reject) => {
        const ws = new WebSocket(url);
        ws.addEventListener("open", () => resolve(ws), { once: true });
        ws.addEventListener("error", () => reject(new Error("websocket connect failed")), { once: true });
      });
      const handlers: Array<(m: RpcMessage) => void> = [];
      const closers: Array<() => void> = [];
      socket.addEventListener("message", (ev) => {
        const parsed = JSON.parse(String(ev.data)) as RpcMessage;
        handlers.forEach((h) => h(parsed));
      });
      socket.addEventListener("close", () => closers.forEach((c) => c()));
      return {
        send: (m) => socket.send(JSON.stringify(m)),
        onMessage: (h) => void handlers.push(h),
        onClose: (h) => void closers.push(h),
        close: async () => socket.close(),
      };
    } catch (err) {
      if (Date.now() > deadline) throw err;
      await new Promise((r) => setTimeout(r, 250));
    }
  }
}

class RpcClient {
  private nextId = 1;
  private pending = new Map<number | string, { resolve: (v: Json) => void; reject: (e: Error) => void }>();
  readonly events: NormalizedEvent[] = [];
  private listeners: Array<(e: NormalizedEvent) => void> = [];

  private transport: Transport;

  constructor(transport: Transport) {
    this.transport = transport;
    transport.onMessage((m) => this.dispatch(m));
    transport.onClose(() => {
      for (const p of this.pending.values()) p.reject(new Error("transport closed"));
      this.pending.clear();
    });
  }

  subscribe(listener: (e: NormalizedEvent) => void): void {
    this.listeners.push(listener);
  }

  private dispatch(message: RpcMessage): void {
    if (message.id !== undefined && !message.method) {
      const waiter = this.pending.get(message.id);
      if (!waiter) return;
      this.pending.delete(message.id);
      if (message.error) waiter.reject(new Error(`${message.error.code}: ${message.error.message}`));
      else waiter.resolve(message.result ?? null);
      return;
    }
    if (message.id !== undefined && message.method) {
      // Server-initiated request (e.g. an approval). The spike never grants one.
      const event = normalize(message);
      if (event) this.record({ ...event, event_type: "task.input_requested" });
      this.transport.send({ id: message.id, error: { code: -32601, message: "spike does not handle server requests" } });
      return;
    }
    const event = normalize(message);
    if (event) this.record(event);
  }

  private record(event: NormalizedEvent): void {
    this.events.push(event);
    this.listeners.forEach((l) => l(event));
  }

  request(method: string, params: JsonObject, timeoutMs: number = REQUEST_TIMEOUT_MS): Promise<Json> {
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`request ${method} timed out after ${timeoutMs}ms`));
      }, timeoutMs);
      this.pending.set(id, {
        resolve: (v) => (clearTimeout(timer), resolve(v)),
        reject: (e) => (clearTimeout(timer), reject(e)),
      });
      this.transport.send({ id, method, params });
    });
  }

  notify(method: string): void {
    this.transport.send({ method });
  }

  // Observed: a stdio app-server silently drops requests written in the first
  // few hundred ms after spawn, so initialize is retried until it answers.
  async handshake(): Promise<Json> {
    let lastError = new Error("handshake not attempted");
    for (let attempt = 0; attempt < HANDSHAKE_MAX_ATTEMPTS; attempt++) {
      try {
        const result = await this.request("initialize", { clientInfo: CLIENT_INFO }, HANDSHAKE_ATTEMPT_TIMEOUT_MS);
        this.notify("initialized");
        return result;
      } catch (err) {
        lastError = err as Error;
      }
    }
    throw new Error(`initialize failed after ${HANDSHAKE_MAX_ATTEMPTS} attempts: ${lastError.message}`);
  }

  disconnect(): Promise<void> {
    return this.transport.close();
  }
}

function isTurnCompleted(e: NormalizedEvent): boolean {
  return asObject(e.raw_payload).method === "turn/completed";
}

function waitFor(client: RpcClient, predicate: (e: NormalizedEvent) => boolean, timeoutMs: number): Promise<NormalizedEvent> {
  const existing = client.events.find(predicate);
  if (existing) return Promise.resolve(existing);
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`timed out after ${timeoutMs}ms waiting for event`)), timeoutMs);
    client.subscribe((e) => {
      if (predicate(e)) (clearTimeout(timer), resolve(e));
    });
  });
}

function parseArgs(argv: string[]): { transport: "stdio" | "ws"; reconnect: boolean; persist: boolean; prompt: string } {
  const args = { transport: "stdio" as "stdio" | "ws", reconnect: false, persist: false, prompt: DEFAULT_PROMPT };
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === "--transport") {
      const v = argv[++i];
      if (v !== "stdio" && v !== "ws") throw new Error(`unknown transport: ${v}`);
      args.transport = v;
    } else if (argv[i] === "--reconnect") args.reconnect = true;
    else if (argv[i] === "--persist") args.persist = true;
    else if (argv[i] === "--prompt") args.prompt = argv[++i] ?? DEFAULT_PROMPT;
    else throw new Error(`unknown argument: ${argv[i]}`);
  }
  if (args.persist && !(args.reconnect && args.transport === "ws")) {
    throw new Error("--persist needs --transport ws --reconnect so the persisted thread can be deleted afterwards");
  }
  return args;
}

async function main(): Promise<number> {
  const args = parseArgs(process.argv.slice(2));
  const workdir = mkdtempSync(join(tmpdir(), "toby-codex-spike-"));
  const serverArgs = args.transport === "ws" ? ["app-server", "--listen", `ws://127.0.0.1:${WS_PORT}`] : ["app-server", "--listen", "stdio://"];
  const child = spawn("codex", serverArgs, { cwd: workdir, stdio: ["pipe", "pipe", "pipe"], detached: true });
  const stderrChunks: string[] = [];
  child.stderr?.on("data", (d: Buffer) => stderrChunks.push(d.toString()));

  // The server runs in its own process group so the wrapper and any native
  // child both receive the signal; only the group this run created is signalled.
  // ESRCH means the group already exited, which is the outcome we want.
  const signalGroup = (pid: number, signal: NodeJS.Signals): boolean => {
    try {
      process.kill(-pid, signal);
      return true;
    } catch (err) {
      if ((err as NodeJS.ErrnoException).code === "ESRCH") return false;
      throw err;
    }
  };

  const cleanup = async (): Promise<void> => {
    try {
      const pid = child.pid;
      if (pid !== undefined && child.exitCode === null) {
        const exited = new Promise<void>((resolve) => child.once("close", () => resolve()));
        if (signalGroup(pid, "SIGTERM")) {
          const timedOut = await Promise.race([exited.then(() => false), new Promise<boolean>((r) => setTimeout(() => r(true), CHILD_EXIT_TIMEOUT_MS))]);
          if (timedOut) signalGroup(pid, "SIGKILL");
        }
      }
    } finally {
      rmSync(workdir, { recursive: true, force: true });
    }
  };

  let persistedThreadId: string | null = null;

  // A persisted thread lives in the user's Codex history, so it is always deleted
  // here, over a fresh connection, even when the run failed part-way.
  const deletePersistedThread = async (): Promise<string> => {
    if (!persistedThreadId) return "none created";
    try {
      const fresh = new RpcClient(await websocketTransport(`ws://127.0.0.1:${WS_PORT}`));
      await fresh.handshake();
      await fresh.request("thread/delete", { threadId: persistedThreadId });
      await fresh.disconnect();
      return "ok";
    } catch (err) {
      return `error: ${(err as Error).message} (thread ${persistedThreadId} may remain in Codex history)`;
    }
  };

  // process.exit skips the finally block below, so the persisted thread is
  // deleted here first, while the server it lives in is still running.
  process.on("SIGINT", () =>
    void (async () => {
      if (args.persist) console.error(`# persisted thread cleanup: thread/delete -> ${await deletePersistedThread()}`);
      await cleanup();
      process.exit(130);
    })(),
  );

  try {
    const transport = args.transport === "ws" ? await websocketTransport(`ws://127.0.0.1:${WS_PORT}`) : stdioTransport(child);
    const client = new RpcClient(transport);
    const init = await client.handshake();
    console.error(`# initialized: ${JSON.stringify(init)}`);

    const started = asObject(await client.request("thread/start", { cwd: workdir, approvalPolicy: "never", sandbox: "read-only", ephemeral: !args.persist }));
    const threadId = asString(asObject(started.thread).id);
    if (!threadId) throw new Error(`thread/start returned no thread id: ${JSON.stringify(started)}`);
    if (args.persist) persistedThreadId = threadId;

    const midTurn = args.reconnect && args.transport === "ws";
    const prompt = midTurn ? SLOW_PROMPT : args.prompt;
    const t0 = Date.now();
    await client.request("turn/start", { threadId, input: [{ type: "text", text: prompt, text_elements: [] }] });

    if (!midTurn) {
      const done = await waitFor(client, isTurnCompleted, TURN_TIMEOUT_MS);
      console.error(`# turn finished in ${Date.now() - t0}ms: ${done.event_type}`);
      for (const e of client.events) console.log(JSON.stringify(e));
      await client.disconnect();
      return done.event_type === "task.completed" ? 0 : 1;
    }

    await waitFor(client, (e) => e.event_type === "task.accepted", REQUEST_TIMEOUT_MS);
    const beforeDrop = client.events.length;
    await client.disconnect();
    console.error(`# observer A dropped mid-turn after ${beforeDrop} events (+${Date.now() - t0}ms)`);
    await new Promise((r) => setTimeout(r, RECONNECT_GAP_MS));

    const second = new RpcClient(await websocketTransport(`ws://127.0.0.1:${WS_PORT}`));
    await second.handshake();
    const loaded = await second.request("thread/loaded/list", {}).catch((e: Error) => `error: ${e.message}`);
    console.error(`# observer B: thread/loaded/list -> ${JSON.stringify(loaded)}`);
    const resumed = await second.request("thread/resume", { threadId }).then(() => "ok").catch((e: Error) => `error: ${e.message}`);
    console.error(`# observer B: thread/resume -> ${resumed}`);
    const done = await waitFor(second, isTurnCompleted, TURN_TIMEOUT_MS).catch((e: Error) => e);
    const types = [...new Set(second.events.map((e) => e.event_type))].join(", ");
    console.error(`# observer B: ${second.events.length} events after reconnect (${types}); turn/completed seen: ${!(done instanceof Error)}`);
    const read = await second.request("thread/read", { threadId, includeTurns: true }).catch((e: Error) => `error: ${e.message}`);
    console.error(`# observer B: thread/read -> ${JSON.stringify(read).slice(0, 500)}`);
    for (const e of [...client.events, ...second.events]) console.log(JSON.stringify(e));
    await second.disconnect();
    return done instanceof Error ? 1 : 0;
  } catch (err) {
    console.error(`session spike failed: ${(err as Error).message}`);
    if (stderrChunks.length) console.error(`codex stderr: ${stderrChunks.join("").slice(0, 800)}`);
    return 1;
  } finally {
    if (args.persist) console.error(`# persisted thread cleanup: thread/delete -> ${await deletePersistedThread()}`);
    await cleanup();
  }
}

if (import.meta.url === `file://${process.argv[1]}`) {
  main().then((code) => process.exit(code));
}
