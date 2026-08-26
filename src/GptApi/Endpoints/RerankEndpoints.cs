using GptApi.Dtos;
using GptApi.Handlers;
using Microsoft.AspNetCore.Mvc;

namespace GptApi.Endpoints;

public static class RerankEndpoints
{
    public static IEndpointConventionBuilder MapRerank(this IEndpointRouteBuilder app) =>
        app.MapPost("/v1/rerank", (
                [FromBody] RerankRequest req,
                RerankHandler h,
                CancellationToken ct) => h.RerankAsync(req, ct))
            .WithTags("Rerank")
            .WithSummary("Rerank documents against a query (Jina/Cohere-shape).")
            .WithDescription(
                """
                Cross-encoder reranking — scores each `documents[]` entry against `query` and
                returns `results` sorted by `relevance_score`. Second stage after an embeddings
                top-K retrieval. Routed to the reranker backend; not streamed.
                """)
            .Produces<RerankResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);
}
