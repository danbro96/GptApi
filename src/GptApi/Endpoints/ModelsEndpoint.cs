using GptApi.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace GptApi.Endpoints;

public static class ModelsEndpoint
{
    public static IEndpointConventionBuilder MapModelsEndpoint(this IEndpointRouteBuilder app) =>
        app.MapGet("/v1/models", (IOptions<LlamaOptions> options) =>
            {
                var id = options.Value.ServedModelId;
                var response = new ModelsResponse
                {
                    Data = new[]
                    {
                        new ModelInfo
                        {
                            Id = id,
                            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            OwnedBy = "local",
                        },
                    },
                };
                return TypedResults.Ok(response);
            })
            .WithTags("Models")
            .WithSummary("List the loaded model.")
            .WithDescription(
                """
                Returns the single model currently loaded by the worker, in OpenAI-compatible
                shape. The id matches what callers should pass in the `model` field of
                completion requests. Configured via the `Llama:ServedModelId` setting.
                """);
}
