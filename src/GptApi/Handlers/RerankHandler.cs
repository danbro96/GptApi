using System.Diagnostics;
using System.Net;
using System.Text.Json;
using GptApi.Models;
using GptApi.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace GptApi.Handlers;

public sealed class RerankHandler
{
    private static readonly ActivitySource _activitySource = new("GptApi.Chat");

    private readonly LlamaRouter _router;
    private readonly ModelAliasResolver _aliases;
    private readonly ILogger<RerankHandler> _log;
    private readonly JsonSerializerOptions _json;

    public RerankHandler(
        LlamaRouter router,
        ModelAliasResolver aliases,
        ILogger<RerankHandler> log,
        IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions)
    {
        _router = router;
        _aliases = aliases;
        _log = log;
        _json = jsonOptions.Value.SerializerOptions;
    }

    public async Task<Results<Ok<RerankResponse>, ProblemHttpResult>> RerankAsync(
        RerankRequest req, CancellationToken ct)
    {
        req.Model = _aliases.Resolve(req.Model);
        if (string.IsNullOrWhiteSpace(req.Model))
            return TypedResults.Problem(detail: "model is required", statusCode: 400);
        if (string.IsNullOrWhiteSpace(req.Query))
            return TypedResults.Problem(detail: "query is required", statusCode: 400);
        if (req.Documents is null || req.Documents.Count == 0)
            return TypedResults.Problem(detail: "documents must be a non-empty array", statusCode: 400);

        var pair = _router.Resolve(req.Model);
        if (pair is null)
            return TypedResults.Problem(detail: $"model '{req.Model}' has no configured backend", statusCode: 400);

        using var activity = _activitySource.StartActivity("rerank");
        activity?.SetTag("gen_ai.request.model", req.Model);

        var requestJson = JsonSerializer.Serialize(req, _json);

        try
        {
            var responseJson = await _router.InvokeAsync(
                pair, c => c.RerankAsync(requestJson, ct), activity);
            var typed = JsonSerializer.Deserialize<RerankResponse>(responseJson, _json)
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
            _log.LogWarning(ex, "Worker call failed for rerank");
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
            _log.LogError(ex, "Failed to deserialize worker response for rerank");
            return TypedResults.Problem(detail: "worker returned malformed response", statusCode: 502);
        }
    }
}
