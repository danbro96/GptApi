using System.Diagnostics;
using GptApi.Models;
using Microsoft.Extensions.Options;

namespace GptApi.Services;

/// <summary>
/// Resolves a request's <c>model</c> to a worker backend (plus an optional fallback) and runs
/// the call with transport-level failover. The gateway is one OpenAI-compatible edge in front
/// of several llama-server workers split across hosts (PC GPU, NAS A380, NAS CPU); this is the
/// only place that knows which model lives where.
/// </summary>
public sealed class LlamaRouter
{
    private readonly IHttpClientFactory _factory;
    private readonly LlamaOptions _options;
    private readonly ILogger<LlamaRouter> _log;
    private readonly Dictionary<string, ModelRoute> _routes;
    private readonly HashSet<string> _backendNames;
    private readonly string? _defaultBackend;

    public LlamaRouter(IHttpClientFactory factory, IOptions<LlamaOptions> options, ILogger<LlamaRouter> log)
    {
        _factory = factory;
        _options = options.Value;
        _log = log;
        _backendNames = _options.EffectiveBackends().Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _routes = _options.Routes.ToDictionary(r => r.Model, r => r, StringComparer.OrdinalIgnoreCase);
        _defaultBackend = _options.EffectiveDefaultBackend();
    }

    public static string ClientName(string backend) => $"llama:{backend}";

    /// <summary>
    /// Maps <paramref name="model"/> to its primary backend (+ fallback). Returns <c>null</c>
    /// when the model has no route and no default backend is configured.
    /// </summary>
    public LlamaBackendPair? Resolve(string model)
    {
        string primary;
        string? fallback = null;

        if (_routes.TryGetValue(model, out var route) && !string.IsNullOrWhiteSpace(route.Backend))
        {
            primary = route.Backend;
            fallback = route.Fallback;
        }
        else if (_defaultBackend is not null)
        {
            primary = _defaultBackend;
        }
        else
        {
            return null;
        }

        if (!_backendNames.Contains(primary)) return null;
        if (fallback is not null && !_backendNames.Contains(fallback)) fallback = null;

        return new LlamaBackendPair
        {
            PrimaryName = primary,
            Primary = ClientFor(primary),
            FallbackName = fallback,
            Fallback = fallback is null ? null : ClientFor(fallback),
        };
    }

    /// <summary>The always-on backend, used by the readiness probe. <c>null</c> if none configured.</summary>
    public LlamaClient? DefaultClient() =>
        _defaultBackend is not null && _backendNames.Contains(_defaultBackend) ? ClientFor(_defaultBackend) : null;

    public IReadOnlyList<(string Name, LlamaClient Client)> AllBackends() =>
        _options.EffectiveBackends().Select(b => (b.Name, ClientFor(b.Name))).ToList();

    /// <summary>
    /// Runs <paramref name="op"/> against the primary backend; on a transport failure
    /// (worker process unreachable — an <see cref="HttpRequestException"/> with no
    /// <see cref="HttpRequestException.StatusCode"/>) retries the fallback. A live worker's HTTP
    /// error (status code set) or a generation timeout is NOT failed over — those propagate.
    /// </summary>
    public async Task<T> InvokeAsync<T>(LlamaBackendPair pair, Func<LlamaClient, Task<T>> op, Activity? activity)
    {
        try
        {
            var result = await op(pair.Primary);
            activity?.SetTag("llm.backend", pair.PrimaryName);
            return result;
        }
        catch (HttpRequestException ex) when (pair.Fallback is not null && ex.StatusCode is null)
        {
            _log.LogWarning(
                ex,
                "Backend {Primary} unreachable; failing over to {Fallback}",
                pair.PrimaryName,
                pair.FallbackName);
            var result = await op(pair.Fallback!);
            activity?.SetTag("llm.backend", pair.FallbackName);
            activity?.SetTag("llm.failover", true);
            return result;
        }
    }

    private LlamaClient ClientFor(string backend) => new(_factory.CreateClient(ClientName(backend)));
}

public sealed class LlamaBackendPair
{
    public required string PrimaryName { get; init; }

    public required LlamaClient Primary { get; init; }

    public string? FallbackName { get; init; }

    public LlamaClient? Fallback { get; init; }
}
