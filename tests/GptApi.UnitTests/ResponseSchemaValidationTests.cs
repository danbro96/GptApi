using System.Text.Json;
using GptApi.Dtos;
using GptApi.Handlers;
using GptApi.Services;
using GptApi.UnitTests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GptApi.UnitTests;

/// <summary>
/// Layer-2 enforcement: a chat request that pins <c>response_format.json_schema</c> gets its worker output
/// validated before returning; a non-conforming (or non-JSON) body becomes a 502. Requests without a schema,
/// streaming requests, and the completions path are untouched; the flag can disable it entirely.
/// </summary>
public class ResponseSchemaValidationTests
{
    private static LlamaOptions Opts(bool enforce = true) => new()
    {
        Backends = { new BackendOptions { Name = "cpu", Url = "http://stub:9000" } },
        DefaultBackend = "cpu",
        EnforceResponseSchema = enforce,
    };

    private static ChatHandler Build(LlamaOptions options, string responseJson, out CapturingStubHandler stub)
    {
        stub = new CapturingStubHandler(responseJson);
        var router = new LlamaRouter(new SingleBackendFactory(stub), Options.Create(options), NullLogger<LlamaRouter>.Instance);
        var resolver = new ModelAliasResolver(Options.Create(options));
        return new ChatHandler(router, resolver, Options.Create(options), NullLogger<ChatHandler>.Instance, TestJson.Options());
    }

    // A minimal object schema requiring an "actions" array.
    private const string Schema = """{"type":"object","required":["actions"],"properties":{"actions":{"type":"array"}}}""";

    private static ChatCompletionRequest Request(string? schema, bool stream = false)
    {
        var req = new ChatCompletionRequest
        {
            Model = "qwen3-14b",
            Stream = stream,
            Messages = [new ChatMessage { Role = "user", Content = JsonSerializer.SerializeToElement("hi") }],
        };
        if (schema is not null)
        {
            var rf = JsonSerializer.SerializeToElement(new
            {
                type = "json_schema",
                json_schema = new { name = "t", strict = true, schema = JsonDocument.Parse(schema).RootElement },
            });
            req.AdditionalProperties = new() { ["response_format"] = rf };
        }

        return req;
    }

    private static string WorkerResponse(string content) => JsonSerializer.Serialize(new
    {
        id = "x",
        @object = "chat.completion",
        created = 0,
        model = "qwen3-14b",
        choices = new[] { new { index = 0, message = new { role = "assistant", content } } },
    });

    private static IResult Inner(INestedHttpResult result) => result.Result;

    [Fact]
    public async Task Schema_pass_returns_ok()
    {
        var handler = Build(Opts(), WorkerResponse("""{"actions":[]}"""), out _);
        var result = await handler.ChatAsync(Request(Schema), new DefaultHttpContext(), default);
        Assert.IsType<Ok<ChatCompletionResponse>>(Inner(result));
    }

    [Fact]
    public async Task Schema_violation_returns_problem_502()
    {
        var handler = Build(Opts(), WorkerResponse("""{"foo":1}"""), out _);
        var result = await handler.ChatAsync(Request(Schema), new DefaultHttpContext(), default);
        var problem = Assert.IsType<ProblemHttpResult>(Inner(result));
        Assert.Equal(502, problem.ProblemDetails.Status);
    }

    [Fact]
    public async Task Non_json_content_returns_problem_502()
    {
        var handler = Build(Opts(), WorkerResponse("hello there"), out _);
        var result = await handler.ChatAsync(Request(Schema), new DefaultHttpContext(), default);
        var problem = Assert.IsType<ProblemHttpResult>(Inner(result));
        Assert.Equal(502, problem.ProblemDetails.Status);
    }

    [Fact]
    public async Task No_response_format_passes_through()
    {
        var handler = Build(Opts(), WorkerResponse("""{"foo":1}"""), out _);
        var result = await handler.ChatAsync(Request(schema: null), new DefaultHttpContext(), default);
        Assert.IsType<Ok<ChatCompletionResponse>>(Inner(result));
    }

    [Fact]
    public async Task Streaming_request_skips_validation()
    {
        // Streaming byte-pipes without deserializing; validation can't (and shouldn't) run.
        var handler = Build(Opts(), WorkerResponse("""{"foo":1}"""), out _);
        var result = await handler.ChatAsync(Request(Schema, stream: true), new DefaultHttpContext(), default);
        Assert.IsType<EmptyHttpResult>(Inner(result));
    }

    [Fact]
    public async Task Enforcement_disabled_passes_through()
    {
        var handler = Build(Opts(enforce: false), WorkerResponse("""{"foo":1}"""), out _);
        var result = await handler.ChatAsync(Request(Schema), new DefaultHttpContext(), default);
        Assert.IsType<Ok<ChatCompletionResponse>>(Inner(result));
    }
}
