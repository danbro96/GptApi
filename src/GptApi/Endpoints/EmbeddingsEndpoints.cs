using GptApi.Dtos;
using GptApi.Handlers;
using Microsoft.AspNetCore.Mvc;

namespace GptApi.Endpoints;

public static class EmbeddingsEndpoints
{
    public static IEndpointConventionBuilder MapEmbeddings(this IEndpointRouteBuilder app) =>
        app.MapPost("/v1/embeddings", (
                [FromBody] EmbeddingsRequest req,
                EmbeddingsHandler h,
                CancellationToken ct) => h.EmbedAsync(req, ct))
            .WithTags("Embeddings")
            .WithSummary("Create embeddings (OpenAI-compatible).")
            .WithDescription(
                """
                OpenAI-compatible embeddings endpoint. Drop-in replacement for
                `https://api.openai.com/v1/embeddings`. `input` accepts a string or an array
                of strings. Routed to the embedding-model backend; not streamed.
                """)
            .Produces<EmbeddingsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);
}
