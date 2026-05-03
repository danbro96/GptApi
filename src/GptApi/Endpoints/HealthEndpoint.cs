using GptApi.Models;
using GptApi.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace GptApi.Endpoints;

public static class HealthEndpoint
{
    public static IEndpointConventionBuilder MapHealthEndpoint(this IEndpointRouteBuilder app) =>
        app.MapGet("/healthz", async Task<Results<Ok<HealthResponse>, ProblemHttpResult>> (
                LlamaClient client,
                CancellationToken ct) =>
            {
                var ok = await client.IsHealthyAsync(ct);
                return ok
                    ? TypedResults.Ok(new HealthResponse { Status = "ok" })
                    : TypedResults.Problem(detail: "worker unhealthy", statusCode: 503);
            })
            .AllowAnonymous()
            .WithTags("Meta")
            .WithSummary("Health probe (cascades to the worker).")
            .WithDescription(
                """
                Returns 200 with `{ "status": "ok" }` when both the .NET service and the
                llama-server worker are reachable. Returns 503 when the worker is
                unreachable or unhealthy. Anonymous — no API key required, suitable for
                the container healthcheck.
                """);
}
