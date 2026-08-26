using GptApi.Dtos;
using GptApi.Handlers;
using Microsoft.AspNetCore.Mvc;

namespace GptApi.Endpoints;

public static class CompletionsEndpoints
{
    public static IEndpointConventionBuilder MapCompletions(this IEndpointRouteBuilder app) =>
        app.MapPost("/v1/completions", (
                [FromBody] CompletionRequest req,
                ChatHandler h,
                HttpContext ctx,
                CancellationToken ct) => h.CompletionAsync(req, ctx, ct))
            .WithTags("Completions")
            .WithSummary("Generate a text completion (legacy OpenAI shape).")
            .WithDescription(
                """
                Legacy OpenAI-compatible text completion endpoint. Most clients should
                prefer `/v1/chat/completions` — this exists for tooling that still speaks
                the older format.

                Set `stream: true` for SSE streaming. The `prompt` field accepts either a
                string or an array of strings.
                """)
            .Produces<CompletionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);
}
