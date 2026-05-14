using GptApi.Handlers;
using GptApi.Models;

namespace GptApi.Endpoints;

public static class HealthEndpoint
{
    public static IEndpointConventionBuilder MapHealthEndpoint(this IEndpointRouteBuilder app) =>
        app.MapGet("/healthz", (HealthHandler h, CancellationToken ct) => h.CheckAsync(ct))
            .AllowAnonymous()
            .DisableHttpMetrics()
            .WithTags("Meta")
            .WithSummary("Health probe (cascades to the worker).")
            .WithDescription(
                """
                Returns 200 with `{ "status": "ok" }` when both the .NET service and the
                llama-server worker are reachable. Returns 503 when the worker is
                unreachable or unhealthy. Anonymous — no API key required, suitable for
                the container healthcheck.
                """)
            .Produces<HealthResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
}
