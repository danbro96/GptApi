# GptApi

Self-hosted, OpenAI-compatible LLM API powered by [llama.cpp](https://github.com/ggml-org/llama.cpp).
Drop-in replacement for `https://api.openai.com/v1/*` for any client that supports a
configurable base URL.

Two-image deploy: a thin .NET 10 minimal-API frontend (`gpt-api`) handles auth,
rate limiting, OpenAPI docs, and OpenTelemetry. A `llama-server` sidecar
(`gpt-worker`, upstream `ghcr.io/ggml-org/llama.cpp:server`) does the actual
inference on CPU. Sized for the MedelyNAS Xeon Gold 6248 running a single 30B-class
GGUF model resident.

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

Send your API key in the `X-API-Key` header (or `?api_key=` query for tools that
don't support custom headers). Configure keys via `Auth__ApiKeys__N__Key` /
`Auth__ApiKeys__N__Name` env vars or `appsettings.json`. Keys are matched in
constant time.

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
