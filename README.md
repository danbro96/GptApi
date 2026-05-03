# GptApi

Self-hosted, OpenAI-compatible LLM API powered by [llama.cpp](https://github.com/ggml-org/llama.cpp).
Drop-in replacement for `https://api.openai.com/v1/*` for any client that supports a
configurable base URL.

Two-image deploy: a thin .NET 10 minimal-API frontend (`gpt-api`) handles auth,
rate limiting, OpenAPI docs, and OpenTelemetry. A
[`llama-swap`](https://github.com/mostlygeek/llama-swap) sidecar (`gpt-worker`,
image `ghcr.io/mostlygeek/llama-swap:cpu`) proxies in front of `llama-server`,
loading and unloading models on demand so only one is resident at a time.

Three-tier model ladder (pick by sending the id in the `model` field):

| Model id | Family | Disk | Approx tok/s on Xeon Gold 6248 | When to use |
|---|---|---|---|---|
| `qwen3-8b` (default) | Qwen 3 8B Instruct, dense, UD-Q4_K_XL | ~5 GB | **~25–40 tok/s** | Cline + everyday chat. The new daily driver. |
| `qwen3.6-35b-a3b-q4` (medium) | Qwen 3.6-35B-A3B MoE, UD-Q4_K_XL | ~22 GB | ~5–10 tok/s | When the 8B feels under-powered. |
| `qwen3.6-35b-a3b-q8` (heavy) | Qwen 3.6-35B-A3B MoE, UD-Q8_K_XL | ~38 GB | ~3–6 tok/s | Final-pass / hard reasoning. Lossless quality. |

Pick a model by sending its id in the `model` field of any chat-completion
request. First request after switching pays a ~30–60 s mmap penalty; subsequent
requests on the same model are fast. Edit
[`deploy/llama-swap.yaml`](deploy/llama-swap.yaml) to add or tune models.

## Endpoints

| Method | Path | Auth | Purpose |
|---|---|---|---|
| `GET` | `/healthz` | anonymous | Liveness; cascades to worker `/health`. |
| `GET` | `/v1/models` | api-key | Lists the configured models (proxied from llama-swap, 60 s cache). |
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
Default 32K context, Bearer auth, and `--jinja` (correct tool-call rendering on
Qwen 3.6) make this work out of the box.

Three model ids to pick from in any client's model dropdown:

- `qwen3-8b` — fast (~25–40 tok/s), default for Cline + chat
- `qwen3.6-35b-a3b-q4` — slower, more capable when the 8B isn't enough
- `qwen3.6-35b-a3b-q8` — slowest, peak quality on-demand

### Cline — agentic file edits + tool calling (recommended)

1. Install the **Cline** extension.
2. Settings → API Provider → **OpenAI Compatible**.
3. Base URL: `https://gpt.lupira.com/v1`, API key: `$GPT_API_KEY`.
4. Model id: `qwen3-8b` (default). Switch to `qwen3.6-35b-a3b-q4` or `-q8` when you need more capability and can wait.
5. Enable **Auto-approve** for `read_file`/`list_files` if you want a smoother loop.

### Continue.dev — alt for chat + agent mode

`~/.continue/config.yaml`:

```yaml
models:
  - name: GptApi (8B, fast)
    provider: openai
    apiBase: https://gpt.lupira.com/v1
    apiKey: ${env:GPT_API_KEY}
    model: qwen3-8b
    roles: [chat, edit, apply]
  - name: GptApi (35B Q4, capable)
    provider: openai
    apiBase: https://gpt.lupira.com/v1
    apiKey: ${env:GPT_API_KEY}
    model: qwen3.6-35b-a3b-q4
    roles: [chat, edit, apply]
  - name: GptApi (35B Q8, max quality)
    provider: openai
    apiBase: https://gpt.lupira.com/v1
    apiKey: ${env:GPT_API_KEY}
    model: qwen3.6-35b-a3b-q8
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
