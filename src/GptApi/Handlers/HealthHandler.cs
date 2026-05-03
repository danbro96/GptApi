using GptApi.Models;
using GptApi.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace GptApi.Handlers;

public sealed class HealthHandler
{
    private readonly LlamaClient _client;

    public HealthHandler(LlamaClient client) => _client = client;

    public async Task<Results<Ok<HealthResponse>, ProblemHttpResult>> CheckAsync(CancellationToken ct)
    {
        var ok = await _client.IsHealthyAsync(ct);
        return ok
            ? TypedResults.Ok(new HealthResponse { Status = "ok" })
            : TypedResults.Problem(detail: "worker unhealthy", statusCode: 503);
    }
}
