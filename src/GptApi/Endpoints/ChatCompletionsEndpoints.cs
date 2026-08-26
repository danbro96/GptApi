using GptApi.Dtos;
using GptApi.Handlers;
using Microsoft.AspNetCore.Mvc;

namespace GptApi.Endpoints;

public static class ChatCompletionsEndpoints
{
    public static IEndpointConventionBuilder MapChatCompletions(this IEndpointRouteBuilder app) =>
        app.MapPost("/v1/chat/completions", (
                [FromBody] ChatCompletionRequest req,
                ChatHandler h,
                HttpContext ctx,
                CancellationToken ct) => h.ChatAsync(req, ctx, ct))
            .WithTags("Chat")
            .WithSummary("Generate a chat completion (OpenAI-compatible).")
            .WithDescription(
                """
                OpenAI-compatible chat completion endpoint. Drop-in replacement for
                `https://api.openai.com/v1/chat/completions` for any client that supports
                a configurable base URL.

                Set `stream: true` to receive a `text/event-stream` response with
                `data: {chunk}` lines terminated by `data: [DONE]`. Otherwise the response
                is a single buffered JSON body.

                Unknown fields (e.g. new OpenAI parameters like `response_format`,
                `tool_choice`) are forwarded to the worker untouched.
                """)
            .Produces<ChatCompletionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout)
            .WithName("CreateChatCompletion");
}
