using GptApi.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GptApi.Endpoints;

/// <summary>
/// Liveness (<c>/livez</c>) and readiness (<c>/readyz</c>) probes built on the ASP.NET Core
/// health-check framework. Liveness reports process-up only; readiness pings the llama-server
/// worker. Names follow the k8s "z-pages" convention; <c>/healthz</c> is deliberately avoided
/// as ambiguous.
/// </summary>
public static class HealthChecks
{
    private const string LiveTag = "live";
    private const string ReadyTag = "ready";

    public static IServiceCollection AddAppHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            // Liveness: the process is up and serving. Touches no dependencies, so a failure
            // here means "restart me", never "a downstream is down".
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: [LiveTag])
            // Readiness: the llama-server worker is reachable. Hard timeout so a hung worker
            // fails fast instead of blocking the probe until Kestrel's request timeout.
            .AddCheck<WorkerHealthCheck>(
                "worker",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag],
                timeout: TimeSpan.FromSeconds(3));
        return services;
    }

    public static void MapAppHealthChecks(this IEndpointRouteBuilder app, IHostEnvironment env)
    {
        // Detailed per-dependency JSON only outside Production — the body reveals dependency
        // topology and these probes are anonymous. Production falls back to the framework's
        // minimal plaintext status.
        var detailed = !env.IsProduction();

        app.MapHealthChecks("/livez", Options(LiveTag, detailed))
            .AllowAnonymous()
            .DisableHttpMetrics();

        app.MapHealthChecks("/readyz", Options(ReadyTag, detailed))
            .AllowAnonymous()
            .DisableHttpMetrics();
    }

    private static HealthCheckOptions Options(string tag, bool detailed)
    {
        var options = new HealthCheckOptions { Predicate = check => check.Tags.Contains(tag) };
        // Leaving ResponseWriter unset keeps the framework default (minimal plaintext status).
        if (detailed) options.ResponseWriter = WriteJsonReport;
        return options;
    }

    private static Task WriteJsonReport(HttpContext context, HealthReport report) =>
        context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                durationMs = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                error = e.Value.Exception?.Message,
            }),
        });
}

/// <summary>
/// Readiness check: pings the always-on default backend (the NAS worker). A GPU backend being
/// down (e.g. the PC while gaming) is normal and handled by failover, so readiness tracks only
/// the backend the gateway can never serve without. Reuses <see cref="LlamaClient.IsHealthyAsync"/>,
/// which applies its own 3s budget and swallows transport errors to a bool.
/// </summary>
internal sealed class WorkerHealthCheck(LlamaRouter router) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var client = router.DefaultClient();
        if (client is null)
            return HealthCheckResult.Unhealthy("No worker backend configured.");

        return await client.IsHealthyAsync(cancellationToken)
            ? HealthCheckResult.Healthy("Worker reachable.")
            : HealthCheckResult.Unhealthy("Worker unreachable.");
    }
}
