using System.Text.Json;
using GptApi.Dtos;
using GptApi.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace GptApi.Handlers;

public sealed class ModelsHandler
{
    private const string CacheKey = "worker-models";
    private static readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(60);

    private readonly LlamaRouter _router;
    private readonly ModelAliasResolver _aliases;
    private readonly IMemoryCache _cache;
    private readonly JsonSerializerOptions _json;

    public ModelsHandler(
        LlamaRouter router,
        ModelAliasResolver aliases,
        IMemoryCache cache,
        IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions)
    {
        _router = router;
        _aliases = aliases;
        _cache = cache;
        _json = jsonOptions.Value.SerializerOptions;
    }

    public async Task<Results<Ok<ModelsResponse>, ProblemHttpResult>> ListAsync(CancellationToken ct)
    {
        try
        {
            var union = await _cache.GetOrCreateAsync(CacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = _cacheTtl;
                return await FetchUnionAsync(ct);
            });
            // GetOrCreateAsync only returns null if the factory did; FetchUnionAsync never does.
            return TypedResults.Ok(union!);
        }
        catch (HttpRequestException)
        {
            // No backend answered (e.g. all workers down). Don't cache the failure.
            return TypedResults.Problem(detail: "no backend available", statusCode: 503);
        }
    }

    /// <summary>
    /// Merges <c>/v1/models</c> across every configured backend, deduped by id, so the full
    /// roster shows even though it's split across the PC GPU, A380, and CPU workers. A backend
    /// that's down (e.g. the PC while gaming) or malformed is skipped, not fatal. The configured
    /// tier aliases are advertised alongside the backend-reported models.
    /// </summary>
    private async Task<ModelsResponse> FetchUnionAsync(CancellationToken ct)
    {
        var byId = new Dictionary<string, ModelInfo>(StringComparer.Ordinal);
        var anyOk = false;

        foreach (var (_, client) in _router.AllBackends())
        {
            try
            {
                var raw = await client.GetModelsAsync(ct);
                var parsed = JsonSerializer.Deserialize<ModelsResponse>(raw, _json);
                if (parsed?.Data is null) continue;
                anyOk = true;
                foreach (var model in parsed.Data) byId.TryAdd(model.Id, model);
            }
            catch (HttpRequestException)
            {
                // Backend unreachable (e.g. the PC while gaming) — skip it.
            }
            catch (JsonException)
            {
                // Backend returned junk — skip it.
            }
        }

        if (!anyOk) throw new HttpRequestException("no backend returned a model list");

        foreach (var alias in _aliases.Aliases.Keys)
            byId.TryAdd(alias, new ModelInfo { Id = alias, OwnedBy = "alias" });

        return new ModelsResponse { Data = byId.Values.ToList() };
    }
}
