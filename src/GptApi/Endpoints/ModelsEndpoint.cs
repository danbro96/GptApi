using System.Text.Json;
using GptApi.Models;
using GptApi.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace GptApi.Endpoints;

public static class ModelsEndpoint
{
    private const string CacheKey = "worker-models";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public static IEndpointConventionBuilder MapModelsEndpoint(this IEndpointRouteBuilder app) =>
        app.MapGet("/v1/models", async Task<Results<Ok<ModelsResponse>, ProblemHttpResult>> (
                LlamaClient client,
                IMemoryCache cache,
                IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions,
                CancellationToken ct) =>
            {
                string? raw;
                try
                {
                    raw = await cache.GetOrCreateAsync(CacheKey, async entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = CacheTtl;
                        return await client.GetModelsAsync(ct);
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
                    var typed = JsonSerializer.Deserialize<ModelsResponse>(raw, jsonOptions.Value.SerializerOptions);
                    return typed is null || typed.Data is null
                        ? TypedResults.Problem(detail: "worker returned malformed model list", statusCode: 502)
                        : TypedResults.Ok(typed);
                }
                catch (JsonException)
                {
                    return TypedResults.Problem(detail: "worker returned malformed model list", statusCode: 502);
                }
            })
            .WithTags("Models")
            .WithSummary("List the models the worker has configured.")
            .WithDescription(
                """
                Returns the model list reported by the worker, in OpenAI-compatible
                shape. With llama-swap this is the full set of declared models; pick
                one by sending its `id` in the `model` field of completion requests.

                Cached for 60 s to avoid hammering the worker on every IDE poll.
                """);
}
