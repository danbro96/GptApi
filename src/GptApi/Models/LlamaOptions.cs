namespace GptApi.Models;

public sealed class LlamaOptions
{
    public const string DefaultBackendName = "default";

    public int RequestTimeoutSeconds { get; set; } = 300;

    /// <summary>When true, a chat response carrying <c>response_format.json_schema</c> is validated against
    /// that schema before returning; non-conforming worker output becomes a 502. Defense-in-depth behind the
    /// worker's own grammar-constrained decoding.</summary>
    public bool EnforceResponseSchema { get; set; } = true;

    /// <summary>
    /// Named worker backends (one llama-swap / llama-server each). The gateway routes a
    /// request's <c>model</c> to one of these via <see cref="Routes"/>.
    /// </summary>
    public List<BackendOptions> Backends { get; set; } = new();

    /// <summary>
    /// Per-model routing: which backend serves a model id, with an optional fallback used only
    /// when the primary backend is unreachable (process down) — e.g. the GPU host is gaming.
    /// </summary>
    public List<ModelRoute> Routes { get; set; } = new();

    /// <summary>
    /// Semantic tier aliases: a caller-facing name (e.g. <c>assistant-fast</c>) → a concrete model
    /// id, resolved before routing so a tier can be re-pointed without touching consumers. A model
    /// id that matches no alias is used as-is.
    /// </summary>
    public Dictionary<string, string> Aliases { get; set; } = new();

    /// <summary>
    /// Backend for model ids with no explicit route. Must be an always-on backend.
    /// </summary>
    public string? DefaultBackend { get; set; }

    /// <summary>
    /// Legacy single-worker URL. When <see cref="Backends"/> is empty this synthesizes one
    /// backend so pre-routing deploys keep working with no config change.
    /// </summary>
    public string? WorkerUrl { get; set; }

    public IReadOnlyList<BackendOptions> EffectiveBackends() =>
        Backends.Count > 0
            ? Backends
            : !string.IsNullOrWhiteSpace(WorkerUrl)
                ? new[] { new BackendOptions { Name = DefaultBackendName, Url = WorkerUrl } }
                : Array.Empty<BackendOptions>();

    public string? EffectiveDefaultBackend()
    {
        if (!string.IsNullOrWhiteSpace(DefaultBackend)) return DefaultBackend;
        var backends = EffectiveBackends();
        return backends.Count == 1 ? backends[0].Name : null;
    }
}

public sealed class BackendOptions
{
    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;
}

public sealed class ModelRoute
{
    public string Model { get; set; } = string.Empty;

    public string Backend { get; set; } = string.Empty;

    public string? Fallback { get; set; }
}
