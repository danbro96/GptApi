using GptApi.Handlers;
using GptApi.Dtos;

namespace GptApi.Endpoints;

public static class ModelsEndpoints
{
    public static IEndpointConventionBuilder MapModelsEndpoint(this IEndpointRouteBuilder app) =>
        app.MapGet("/v1/models", (ModelsHandler h, CancellationToken ct) => h.ListAsync(ct))
            .WithTags("Models")
            .WithSummary("List the models the worker has configured.")
            .WithDescription(
                """
                Returns the model list reported by the worker, in OpenAI-compatible
                shape. With llama-swap this is the full set of declared models; pick
                one by sending its `id` in the `model` field of completion requests.

                Cached for 60 s to avoid hammering the worker on every IDE poll.
                """)
            .Produces<ModelsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
}
