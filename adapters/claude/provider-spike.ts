// M0 spike: prove Toby can authenticate to the Anthropic Messages API with a
// user-supplied API key and get a valid response.
//
// The key is read from ANTHROPIC_API_KEY only; it is never logged, echoed, or
// written to disk. Exit codes: 0 verified, 1 unexpected failure, 2 missing or
// rejected credentials, 3 rate limited.
//
// Usage: ANTHROPIC_API_KEY=... node provider-spike.ts

const API_URL = "https://api.anthropic.com/v1/messages";
const API_VERSION = "2023-06-01";
const DEFAULT_MODEL = "claude-haiku-5-5";
const TEST_PROMPT = "Reply with the single word: verified";
const MAX_TOKENS = 16;
const REQUEST_TIMEOUT_MS = 30_000;
const EXPECTED_WORD = "verified";

const EXIT_OK = 0;
const EXIT_FAILURE = 1;
const EXIT_AUTH = 2;
const EXIT_RATE_LIMITED = 3;

interface ContentBlock {
  type: string;
  text?: string;
}

interface MessagesResponse {
  id?: string;
  model?: string;
  content?: ContentBlock[];
  usage?: { input_tokens: number; output_tokens: number };
  error?: { type: string; message: string };
}

// Headers worth recording for the findings doc; none of them carry secrets.
function interestingHeaders(headers: Headers): Record<string, string> {
  const picked: Record<string, string> = {};
  for (const [name, value] of headers) {
    if (name.startsWith("anthropic-ratelimit-") || name === "request-id" || name === "retry-after") {
      picked[name] = value;
    }
  }
  return picked;
}

async function main(): Promise<number> {
  const apiKey = process.env.ANTHROPIC_API_KEY;
  if (!apiKey) {
    console.error("ANTHROPIC_API_KEY is not set. Export a key from console.anthropic.com and retry.");
    return EXIT_AUTH;
  }
  const model = process.env.TOBY_CLAUDE_MODEL ?? DEFAULT_MODEL;

  const started = performance.now();
  let response: Response;
  try {
    response = await fetch(API_URL, {
      method: "POST",
      headers: {
        "x-api-key": apiKey,
        "anthropic-version": API_VERSION,
        "content-type": "application/json",
      },
      body: JSON.stringify({ model, max_tokens: MAX_TOKENS, messages: [{ role: "user", content: TEST_PROMPT }] }),
      signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
    });
  } catch (err) {
    console.error(`Network failure reaching ${API_URL}: ${(err as Error).message}`);
    return EXIT_FAILURE;
  }
  const latencyMs = Math.round(performance.now() - started);

  const text = await response.text();
  let body: MessagesResponse = {};
  try {
    body = JSON.parse(text) as MessagesResponse;
  } catch {
    console.error(`HTTP ${response.status}: response was not JSON: ${text.slice(0, 200)}`);
    return EXIT_FAILURE;
  }

  console.log(JSON.stringify({ status: response.status, latency_ms: latencyMs, model: body.model ?? model, headers: interestingHeaders(response.headers) }));

  if (response.status === 401 || response.status === 403) {
    console.error(`Authentication rejected (HTTP ${response.status}): ${body.error?.message ?? "no detail"}. Check the key and its workspace permissions.`);
    return EXIT_AUTH;
  }
  if (response.status === 429) {
    console.error(`Rate limited (HTTP 429): ${body.error?.message ?? "no detail"}. retry-after=${response.headers.get("retry-after") ?? "n/a"}`);
    return EXIT_RATE_LIMITED;
  }
  if (!response.ok) {
    console.error(`Provider error (HTTP ${response.status}): ${body.error?.type ?? "unknown"}: ${body.error?.message ?? text.slice(0, 200)}`);
    return EXIT_FAILURE;
  }

  const reply = (body.content ?? []).filter((b) => b.type === "text").map((b) => b.text ?? "").join("").trim();
  console.log(`reply: ${reply}`);
  console.log(`usage: ${JSON.stringify(body.usage ?? null)}`);
  if (!reply.toLowerCase().includes(EXPECTED_WORD)) {
    console.error(`Unexpected reply; wanted "${EXPECTED_WORD}".`);
    return EXIT_FAILURE;
  }
  return EXIT_OK;
}

main().then((code) => process.exit(code));
