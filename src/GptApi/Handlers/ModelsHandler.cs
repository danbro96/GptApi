using System.Text.Json;
using GptApi.Models;
using GptApi.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace GptApi.Handlers;

public sealed class ModelsHandler
{
    private const string CacheKey = "worker-models";
    private static readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(60);

    private readonly LlamaClient _client;
    private readonly IMemoryCache _cache;
    private readonly JsonSerializerOptions _json;

    public ModelsHandler(
        LlamaClient client,
        IMemoryCache cache,
        IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions)
    {
        _client = client;
        _cache = cache;
        _json = jsonOptions.Value.SerializerOptions;
    }

    public async Task<Results<Ok<ModelsResponse>, ProblemHttpResult>> ListAsync(CancellationToken ct)
    {
        string? raw;
        try
        {
            raw = await _cache.GetOrCreateAsync(CacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = _cacheTtl;
                return await _client.GetModelsAsync(ct);
            });
        }
        catch (HttpRequestException)
        {
            return TypedResults.Problem(detail: "worker unavailable", statusCode: 503);
        }

        if (string.IsNullOrEmpty(raw))
            return TypedResults.Problem(detail: "worker returned empty model list", statusCode: 502);

        try
        {
            var typed = JsonSerializer.Deserialize<ModelsResponse>(raw, _json);
            return typed is null || typed.Data is null
                ? TypedResults.Problem(detail: "worker returned malformed model list", statusCode: 502)
                : TypedResults.Ok(typed);
        }
        catch (JsonException)
        {
            return TypedResults.Problem(detail: "worker returned malformed model list", statusCode: 502);
        }
    }
}
