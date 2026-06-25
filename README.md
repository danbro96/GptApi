# GptApi

Self-hosted, OpenAI-compatible LLM API powered by [llama.cpp](https://github.com/ggml-org/llama.cpp).
Drop-in replacement for `https://api.openai.com/v1/*` for any client that supports a
configurable base URL.

A thin .NET 10 minimal-API **gateway** (`gpt-api`) handles auth, rate limiting, OpenAPI docs,
OpenTelemetry, and **per-model routing**: each request's `model` is dispatched to one of several
[`llama-swap`](https://github.com/mostlygeek/llama-swap) workers, with an optional fallback used
only when the primary worker is unreachable. Backends and routes are config (see
[`deploy/compose.yaml`](deploy/compose.yaml)), so where a model runs is an ops decision, not a
code change.

Example roster (pick by sending the id in the `model` field). Place models by availability +
workload shape, not size — heavy generation on a GPU host, always-on small models locally:

| Model id | Role | Backend (primary → fallback) | Notes |
|---|---|---|---|
| `qwen3-14b` | Workhorse — generation / orchestration | GPU host → CPU | Heavy generation; kept warm on the GPU |
| `qwen3-1.7b` | Triage — cheap actionable-or-not gate | CPU | Always-on, runs on every inbound |
| `qwen3-embedding-0.6b` | Embeddings — retrieval / dedup (`/v1/embeddings`) | local GPU → CPU | Single-pass encoder, resident |
| `qwen3-reranker-0.6b` | Reranker (`/v1/rerank`) | local GPU → CPU | Cross-encoder; re-scores the embedding top-K |

A `model` with no configured route goes to the default backend (an always-on one). Edit
[`deploy/llama-swap.yaml`](deploy/llama-swap.yaml) (CPU), `llama-swap.gpu.yaml` (GPU host), or
`llama-swap.a380.yaml` (local GPU encoders) to add or tune models on a given host.

## Endpoints

| Method | Path | Auth | Purpose |
|---|---|---|---|
| `GET` | `/livez` | anonymous | Liveness; process up (no dependency check). |
| `GET` | `/readyz` | anonymous | Readiness; pings the always-on default (NAS) backend. Used by the container healthcheck. |
| `GET` | `/v1/models` | api-key | Lists the configured models — union across all backends, 60 s cache. |
| `POST` | `/v1/chat/completions` | api-key | Chat completion. Set `stream: true` for SSE. |
| `POST` | `/v1/completions` | api-key | Legacy text completion. Same `stream` semantics. |
| `POST` | `/v1/embeddings` | api-key | Embeddings (routed to the embedding backend). Not streamed. |
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
Default 32K context, Bearer auth, and `--jinja` (correct tool-call rendering)
make this work out of the box.

The agent-facing model in any client's dropdown:

- `qwen3-14b` — the workhorse, on the GPU host (falls back to CPU if that host is offline)

### Cline — agentic file edits + tool calling (recommended)

1. Install the **Cline** extension.
2. Settings → API Provider → **OpenAI Compatible**.
3. Base URL: `https://your-gateway/v1`, API key: `$GPT_API_KEY`.
4. Model id: `qwen3-14b` (the workhorse).
5. Enable **Auto-approve** for `read_file`/`list_files` if you want a smoother loop.

### Continue.dev — alt for chat + agent mode

`~/.continue/config.yaml`:

```yaml
models:
  - name: GptApi (workhorse)
    provider: openai
    apiBase: https://your-gateway/v1
    apiKey: ${env:GPT_API_KEY}
    model: qwen3-14b
    roles: [chat, edit, apply]
```

### GitHub Copilot Chat BYOK — currently broken

VS Code's BYOK for "OpenAI Compatible" providers is unreliable as of early 2026:
[microsoft/vscode#289003](https://github.com/microsoft/vscode/issues/289003).
Custom models added via `chatLanguageModels.json` don't reliably appear in the
model picker. Use Cline or Continue.dev until that's fixed.

### Tool calling (Cline / agent mode)

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

## Deploy

Run the gateway + workers with [`deploy/compose.yaml`](deploy/compose.yaml): copy
[`deploy/.env.example`](deploy/.env.example) to `deploy/.env`, set your backend URLs + API key,
and point each worker at its `deploy/llama-swap*.yaml` model roster.

## Conventions

- `net10.0`
- `sealed class` DTOs with `public required T Name { get; set; }` (not positional records)
- `TypedResults` everywhere it's possible — endpoints return `Results<Ok<T>, ProblemHttpResult, …>` unions
- OpenAPI + Scalar UI baked in
- REST routing where applicable; OpenAI-compat endpoints match the `/v1/*` spec verbatim
