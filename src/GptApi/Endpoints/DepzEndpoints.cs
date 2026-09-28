using System.Text.Json;
using System.Text.Json.Serialization;
using GptApi.Dependencies;

namespace GptApi.Endpoints;

/// <summary>Non-gating dependency report: this service's outward edges, served from the
/// poller's cache. Deliberately not part of /readyz.</summary>
public static class DepzEndpoints
{
    // The app-wide snake_case contract would break devops-api's camelCase /depz parser.
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static void MapDepz(this IEndpointRouteBuilder app) =>
        app.MapGet("/depz", (DependencyReportCache cache) => TypedResults.Json(cache.Current(), WireJson))
            .AllowAnonymous()
            .AddEndpointFilter<ProbeKeyFilter>()
            .ExcludeFromDescription()
            .DisableHttpMetrics()
            .WithName("GetDependencies");
}
