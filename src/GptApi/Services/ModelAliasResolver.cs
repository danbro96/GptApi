using GptApi.Dtos;
using Microsoft.Extensions.Options;

namespace GptApi.Services;

/// <summary>
/// Rewrites a caller-facing tier alias (e.g. <c>assistant-fast</c>) to its concrete model id before
/// routing, so consumers pick a semantic tier and a model can be re-pointed in config. Pure
/// substitution — a model id that matches no alias passes through unchanged.
/// </summary>
public sealed class ModelAliasResolver
{
    private readonly Dictionary<string, string> _aliases;

    public ModelAliasResolver(IOptions<LlamaOptions> options) =>
        _aliases = options.Value.Aliases.ToDictionary(
            kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps an alias to its concrete model id; an unknown id is returned unchanged.</summary>
    public string Resolve(string model) =>
        _aliases.TryGetValue(model, out var concrete) ? concrete : model;

    /// <summary>Configured alias → concrete-id map, advertised by <c>/v1/models</c>.</summary>
    public IReadOnlyDictionary<string, string> Aliases => _aliases;
}
