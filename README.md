# GptApi

Self-hosted, OpenAI-compatible LLM API powered by [llama.cpp](https://github.com/ggml-org/llama.cpp).
Drop-in replacement for `https://api.openai.com/v1/*` for any client that supports a
configurable base URL.

Two-image deploy: a thin .NET 10 minimal-API frontend (`gpt-api`) handles auth,
rate limiting, OpenAPI docs, and OpenTelemetry. A `llama-server` sidecar
(`gpt-worker`, upstream `ghcr.io/ggml-org/llama.cpp:server`) does the actual
inference on CPU. Default model is Qwen 3.6-35B-A3B (MoE, 3B active per token,
Unsloth Dynamic Q8) — sized for the MedelyNAS Xeon Gold 6248 / 125 GiB RAM.

## Endpoints

| Method | Path | Auth | Purpose |
|---|---|---|---|
| `GET` | `/healthz` | anonymous | Liveness; cascades to worker `/health`. |
| `GET` | `/v1/models` | api-key | Lists the loaded model in OpenAI shape. |
| `POST` | `/v1/chat/completions` | api-key | Chat completion. Set `stream: true` for SSE. |
| `POST` | `/v1/completions` | api-key | Legacy text completion. Same `stream` semantics. |
| `GET` | `/openapi/v1.json` | anonymous | OpenAPI 3 schema. |
| `GET` | `/scalar/v1` | anonymous | Interactive [Scalar](https://scalar.com/) docs. |

## Auth

Three accepted forms, matched in constant time:

- `Authorization: Bearer <key>` — what every OpenAI-compat client sends.
- `X-API-Key: <key>` — used by KokoroApi/FlorenceApi-style internal callers.
- `?api_key=<key>` — query param fallback for tools that can't set headers.

Configure keys via `Auth__ApiKeys__N__Key` / `Auth__ApiKeys__N__Name` env vars
or `appsettings.json`.

## Local quickstart

```powershell
# 1. Get a small model for smoke testing (~600 MB)
mkdir -p .\cache
curl.exe -L -o .\cache\qwen2.5-3b-instruct-q4_k_m.gguf `
  https://huggingface.co/bartowski/Qwen2.5-3B-Instruct-GGUF/resolve/main/Qwen2.5-3B-Instruct-Q4_K_M.gguf

# 2. Run llama-server alone
docker run --rm -p 9000:9000 -v ${PWD}\cache:/cache `
  ghcr.io/ggml-org/llama.cpp:server `
  -m /cache/qwen2.5-3b-instruct-q4_k_m.gguf -c 4096 --host 0.0.0.0 --port 9000

# 3. In another terminal, run the .NET frontend pointed at the worker
cd src\GptApi
$env:Llama__WorkerUrl = "http://localhost:9000"
$env:Auth__ApiKeys__0__Key = "dev-key"
$env:Auth__ApiKeys__0__Name = "dev"
dotnet run
```

Then open `http://localhost:8080/scalar/v1` and try `/v1/chat/completions` with
header `X-API-Key: dev-key`.

## VS Code as a coding agent

GptApi works as a drop-in OpenAI-compatible backend for VS Code coding agents.
Default `CONTEXT_SIZE=32768` and `--jinja` (correct tool-call rendering for
Qwen3.6) make this work out of the box.

### GitHub Copilot Chat — Bring Your Own Key (chat panel)

Native, GA April 2026. Settings UI:

1. Command Palette → **GitHub Copilot: Manage models**.
2. Add model → provider **OpenAI Compatible**.
3. Base URL: `https://gpt.lupira.com/v1`
4. API key: `$GPT_API_KEY`
5. Model id: `qwen3.6-35b-a3b`

### Cline — agentic file edits + tool calling (recommended for repo work)

1. Install the **Cline** extension.
2. Settings → API Provider → **OpenAI Compatible**.
3. Base URL: `https://gpt.lupira.com/v1`, API key: `$GPT_API_KEY`,
   Model id: `qwen3.6-35b-a3b`.
4. Enable **Auto-approve** for `read_file`/`list_files` if you want a smoother loop.

### Continue.dev — alt for chat + agent mode

`~/.continue/config.yaml`:

```yaml
models:
  - name: GptApi (qwen3.6-35b-a3b)
    provider: openai
    apiBase: https://gpt.lupira.com/v1
    apiKey: ${env:GPT_API_KEY}
    model: qwen3.6-35b-a3b
    roles: [chat, edit, apply]
```

### Tool calling (Cline / Copilot agent mode)

Qwen 3.6 supports OpenAI-shape tool calls natively, and GptApi passes them
through unchanged (request fields like `tools` / `tool_choice` and the
returned `tool_calls` round-trip via `JsonExtensionData`). The `--jinja` flag
on the worker is what makes tool-call rendering correct — without it, agentic
clients would see broken responses.

### What's not included

- **Inline autocomplete (ghost-text)** — needs llama.cpp's `/infill` endpoint,
  which GptApi doesn't proxy. Use Copilot's stock autocomplete or wire
  Continue.dev's `provider: llama.cpp` directly at the worker if you really
  want this later.

## Production deploy

Mirrors the `FlorenceApi` / `KokoroApi` shape — TrueNAS Custom App, Cloudflare
Tunnel, OpenObserve telemetry. See
[`DevOps/Websites/gpt-api/deployment.md`](../../Nextcloud/Familj/DevOps/Websites/gpt-api/deployment.md)
in the DevOps repo for the full first-time stand-up.

The compose template lives in [`deploy/compose.yaml`](deploy/compose.yaml); copy
[`deploy/.env.example`](deploy/.env.example) to `deploy/.env` and edit.

## Conventions

- `net10.0`
- `sealed class` DTOs with `public required T Name { get; set; }` (not positional records)
- `TypedResults` everywhere it's possible — endpoints return `Results<Ok<T>, ProblemHttpResult, …>` unions
- OpenAPI + Scalar UI baked in
- REST routing where applicable; OpenAI-compat endpoints match the `/v1/*` spec verbatim
