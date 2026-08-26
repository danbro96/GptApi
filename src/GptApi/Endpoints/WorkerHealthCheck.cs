using GptApi.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GptApi.Endpoints;

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
