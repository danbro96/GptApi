using GptApi.Models;
using GptApi.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace GptApi.Handlers;

public sealed class ChatHandler
{
    private static readonly ActivitySource _activitySource = new("GptApi.Chat");

    private readonly LlamaClient _client;
    private readonly ILogger<ChatHandler> _log;
    private readonly LlamaOptions _options;
    private readonly JsonSerializerOptions _json;

    public ChatHandler(
        LlamaClient client,
        ILogger<ChatHandler> log,
        IOptions<LlamaOptions> options,
        IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions)
    {
        _client = client;
        _log = log;
        _options = options.Value;
        _json = jsonOptions.Value.SerializerOptions;
    }

    public Task<Results<Ok<ChatCompletionResponse>, ProblemHttpResult, EmptyHttpResult>>
        ChatAsync(ChatCompletionRequest req, HttpContext ctx, CancellationToken ct) =>
        DispatchAsync<ChatCompletionRequest, ChatCompletionResponse>(
            req, "chat.completion", ctx,
            buffered: _client.ChatCompletionAsync,
            streaming: _client.StreamChatCompletionAsync,
            ct);

    public Task<Results<Ok<CompletionResponse>, ProblemHttpResult, EmptyHttpResult>>
        CompletionAsync(CompletionRequest req, HttpContext ctx, CancellationToken ct) =>
        DispatchAsync<CompletionRequest, CompletionResponse>(
            req, "text.completion", ctx,
            buffered: _client.CompletionAsync,
            streaming: _client.StreamCompletionAsync,
            ct);

    private async Task<Results<Ok<TResponse>, ProblemHttpResult, EmptyHttpResult>>
        DispatchAsync<TRequest, TResponse>(
            TRequest req,
            string activityName,
            HttpContext ctx,
            Func<string, CancellationToken, Task<string>> buffered,
            Func<string, CancellationToken, Task<HttpResponseMessage>> streaming,
            CancellationToken ct)
        where TRequest : class
        where TResponse : class
    {
        var validation = Validate(req);
        if (validation is not null) return validation;

        using var activity = _activitySource.StartActivity(activityName);
        ApplyRequestTags(activity, req);

        var requestJson = JsonSerializer.Serialize(req, _json);
        var sw = Stopwatch.StartNew();

        try
        {
            if (IsStreaming(req))
            {
                using var workerResponse = await streaming(requestJson, ct);
                ctx.Response.ContentType = "text/event-stream";
                ctx.Response.Headers.CacheControl = "no-cache";
                ctx.Response.Headers["X-Accel-Buffering"] = "no";

                await using var workerStream = await workerResponse.Content.ReadAsStreamAsync(ct);
                await PipeAndFlushAsync(workerStream, ctx.Response.Body, ct);

                activity?.SetTag("elapsed_ms", sw.ElapsedMilliseconds);
                return TypedResults.Empty;
            }

            var responseJson = await buffered(requestJson, ct);
            var typed = JsonSerializer.Deserialize<TResponse>(responseJson, _json)
                ?? throw new JsonException("worker returned null");

            ApplyResponseTags(activity, typed);
            activity?.SetTag("elapsed_ms", sw.ElapsedMilliseconds);
            return TypedResults.Ok(typed);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return TypedResults.Problem(detail: ex.Message, statusCode: 400);
        }
        catch (HttpRequestException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _log.LogWarning(ex, "Worker call failed for {Activity}", activityName);
            return TypedResults.Problem(detail: $"worker error: {ex.Message}", statusCode: 502);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "timeout");
            return TypedResults.Problem(detail: "inference timeout", statusCode: 504);
        }
        catch (JsonException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _log.LogError(ex, "Failed to deserialize worker response for {Activity}", activityName);
            return TypedResults.Problem(detail: "worker returned malformed response", statusCode: 502);
        }
    }

    private static ProblemHttpResult? Validate<TRequest>(TRequest req)
        where TRequest : class
        => req switch
        {
            ChatCompletionRequest c when string.IsNullOrWhiteSpace(c.Model)
                => TypedResults.Problem(detail: "model is required", statusCode: 400),
            ChatCompletionRequest c when c.Messages is null || c.Messages.Count == 0
                => TypedResults.Problem(detail: "messages must be a non-empty array", statusCode: 400),
            CompletionRequest p when string.IsNullOrWhiteSpace(p.Model)
                => TypedResults.Problem(detail: "model is required", statusCode: 400),
            CompletionRequest p when p.Prompt.ValueKind == JsonValueKind.Undefined || p.Prompt.ValueKind == JsonValueKind.Null
                => TypedResults.Problem(detail: "prompt is required", statusCode: 400),
            _ => null,
        };

    private static bool IsStreaming<TRequest>(TRequest req)
        where TRequest : class
        => req switch
        {
            ChatCompletionRequest c => c.Stream,
            CompletionRequest p => p.Stream,
            _ => false,
        };

    private static void ApplyRequestTags<TRequest>(Activity? activity, TRequest req)
        where TRequest : class
    {
        if (activity is null) return;
        switch (req)
        {
            case ChatCompletionRequest c:
                activity.SetTag("gen_ai.request.model", c.Model);
                activity.SetTag("gen_ai.request.streaming", c.Stream);
                activity.SetTag("gen_ai.request.message_count", c.Messages.Count);
                if (c.MaxTokens is { } chatMax) activity.SetTag("gen_ai.request.max_tokens", chatMax);
                if (c.Temperature is { } chatTemp) activity.SetTag("gen_ai.request.temperature", chatTemp);
                break;
            case CompletionRequest p:
                activity.SetTag("gen_ai.request.model", p.Model);
                activity.SetTag("gen_ai.request.streaming", p.Stream);
                if (p.MaxTokens is { } promptMax) activity.SetTag("gen_ai.request.max_tokens", promptMax);
                break;
        }
    }

    private static void ApplyResponseTags<TResponse>(Activity? activity, TResponse typed)
        where TResponse : class
    {
        if (activity is null) return;
        var usage = typed switch
        {
            ChatCompletionResponse c => c.Usage,
            CompletionResponse p => p.Usage,
            _ => null,
        };
        if (usage is null) return;
        activity.SetTag("gen_ai.usage.prompt_tokens", usage.PromptTokens);
        activity.SetTag("gen_ai.usage.completion_tokens", usage.CompletionTokens);
        activity.SetTag("gen_ai.usage.total_tokens", usage.TotalTokens);
    }

    private static async Task PipeAndFlushAsync(Stream source, Stream destination, CancellationToken ct)
    {
        // Small buffer + explicit flush per read so SSE tokens reach the client as fast as
        // llama-server emits them. Default Stream.CopyToAsync uses an 80 KiB buffer that
        // would coalesce token-sized chunks and spoil the streaming UX.
        var buffer = new byte[4096];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            await destination.FlushAsync(ct);
        }
    }
}
