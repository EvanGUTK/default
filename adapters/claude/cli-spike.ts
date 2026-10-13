// M0 spike: get one reply from the user's own, unmodified, already-signed-in
// `claude` CLI. This is the conservative path in docs/INTEGRATIONS.md: Toby
// never sees the sign-in credentials; it only runs the CLI and reads its output.
//
// Exit codes: 0 verified, 1 failure, 2 CLI not signed in or auth error.
//
// Usage: node cli-spike.ts

import { spawn } from "node:child_process";

const TEST_PROMPT = "Reply with the single word: verified";
const EXPECTED_WORD = "verified";
const CLI_TIMEOUT_MS = 90_000;

const EXIT_OK = 0;
const EXIT_FAILURE = 1;
const EXIT_AUTH = 2;

interface CliResult {
  type?: string;
  subtype?: string;
  is_error?: boolean;
  result?: string;
  session_id?: string;
  duration_ms?: number;
  duration_api_ms?: number;
  num_turns?: number;
  total_cost_usd?: number;
  usage?: unknown;
  modelUsage?: Record<string, unknown>;
}

function runClaude(): Promise<{ code: number | null; stdout: string; stderr: string }> {
  return new Promise((resolve, reject) => {
    // No tools, no slash commands, no persisted session: a plain text exchange.
    const child = spawn(
      "claude",
      ["-p", TEST_PROMPT, "--output-format", "json", "--no-session-persistence", "--tools", "", "--disable-slash-commands"],
      { stdio: ["ignore", "pipe", "pipe"] },
    );
    let stdout = "";
    let stderr = "";
    const timer = setTimeout(() => child.kill("SIGTERM"), CLI_TIMEOUT_MS);
    child.stdout.on("data", (d: Buffer) => (stdout += d.toString()));
    child.stderr.on("data", (d: Buffer) => (stderr += d.toString()));
    child.on("error", (err) => (clearTimeout(timer), reject(err)));
    child.on("close", (code) => (clearTimeout(timer), resolve({ code, stdout, stderr })));
  });
}

async function main(): Promise<number> {
  const started = performance.now();
  let run: Awaited<ReturnType<typeof runClaude>>;
  try {
    run = await runClaude();
  } catch (err) {
    console.error(`Could not start the claude CLI: ${(err as Error).message}. Is it installed and on PATH?`);
    return EXIT_FAILURE;
  }
  const wallMs = Math.round(performance.now() - started);

  let parsed: CliResult;
  try {
    parsed = JSON.parse(run.stdout) as CliResult;
  } catch {
    console.error(`claude exited ${run.code} without JSON output. stderr: ${run.stderr.slice(0, 300)}`);
    return EXIT_FAILURE;
  }

  console.log(JSON.stringify({ exit_code: run.code, wall_ms: wallMs, duration_ms: parsed.duration_ms, duration_api_ms: parsed.duration_api_ms, num_turns: parsed.num_turns, is_error: parsed.is_error, models: Object.keys(parsed.modelUsage ?? {}) }));
  console.log(`reply: ${parsed.result ?? ""}`);

  if (parsed.is_error) {
    const looksLikeAuth = /log ?in|auth|credential|401|api key/i.test(parsed.result ?? "");
    console.error(`claude reported an error: ${parsed.result ?? "no detail"}`);
    return looksLikeAuth ? EXIT_AUTH : EXIT_FAILURE;
  }
  if (!(parsed.result ?? "").toLowerCase().includes(EXPECTED_WORD)) {
    console.error(`Unexpected reply; wanted "${EXPECTED_WORD}".`);
    return EXIT_FAILURE;
  }
  return EXIT_OK;
}

main().then((code) => process.exit(code));
