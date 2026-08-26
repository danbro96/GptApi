using System.Diagnostics;
using System.Net;
using System.Text.Json;
using GptApi.Dtos;
using GptApi.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace GptApi.Handlers;

public sealed class EmbeddingsHandler
{
    private static readonly ActivitySource _activitySource = new("GptApi.Chat");

    private readonly LlamaRouter _router;
    private readonly ModelAliasResolver _aliases;
    private readonly ILogger<EmbeddingsHandler> _log;
    private readonly JsonSerializerOptions _json;

    public EmbeddingsHandler(
        LlamaRouter router,
        ModelAliasResolver aliases,
        ILogger<EmbeddingsHandler> log,
        IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions)
    {
        _router = router;
        _aliases = aliases;
        _log = log;
        _json = jsonOptions.Value.SerializerOptions;
    }

    public async Task<Results<Ok<EmbeddingsResponse>, ProblemHttpResult>> EmbedAsync(
        EmbeddingsRequest req, CancellationToken ct)
    {
        req.Model = _aliases.Resolve(req.Model);
        if (string.IsNullOrWhiteSpace(req.Model))
            return TypedResults.Problem(detail: "model is required", statusCode: 400);
        if (req.Input.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return TypedResults.Problem(detail: "input is required", statusCode: 400);

        var pair = _router.Resolve(req.Model);
        if (pair is null)
            return TypedResults.Problem(detail: $"model '{req.Model}' has no configured backend", statusCode: 400);

        using var activity = _activitySource.StartActivity("embeddings");
        activity?.SetTag("gen_ai.request.model", req.Model);

        var requestJson = JsonSerializer.Serialize(req, _json);

        try
        {
            var responseJson = await _router.InvokeAsync(
                pair, c => c.EmbeddingsAsync(requestJson, ct), activity);
            var typed = JsonSerializer.Deserialize<EmbeddingsResponse>(responseJson, _json)
                ?? throw new JsonException("worker returned null");
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
            _log.LogWarning(ex, "Worker call failed for embeddings");
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
            _log.LogError(ex, "Failed to deserialize worker response for embeddings");
            return TypedResults.Problem(detail: "worker returned malformed response", statusCode: 502);
        }
    }
}
