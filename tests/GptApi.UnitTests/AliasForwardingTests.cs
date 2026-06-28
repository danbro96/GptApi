using System.Text.Json;
using GptApi.Handlers;
using GptApi.Models;
using GptApi.Services;
using GptApi.UnitTests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GptApi.UnitTests;

/// <summary>
/// End-to-end through a handler: an alias is rewritten to its concrete id before forwarding, so the
/// backend (llama-swap) receives the GGUF id and loads the right model; raw ids forward unchanged
/// and an unrouted id still errors as before.
/// </summary>
public class AliasForwardingTests
{
    private static LlamaOptions AliasOptions(string? defaultBackend = "cpu") => new()
    {
        Backends = { new BackendOptions { Name = "cpu", Url = "http://stub:9000" } },
        DefaultBackend = defaultBackend,
        Aliases =
        {
            ["assistant-fast"] = "qwen3-1.7b",
            ["embed"] = "qwen3-embedding-0.6b",
            ["rerank"] = "qwen3-reranker-0.6b",
        },
    };

    private static LlamaOptions TwoBackendsNoDefault() => new()
    {
        Backends =
        {
            new BackendOptions { Name = "a", Url = "http://a:9000" },
            new BackendOptions { Name = "b", Url = "http://b:9000" },
        },
        DefaultBackend = null,
        Aliases = { ["assistant-fast"] = "qwen3-1.7b" },
    };

    private static (T handler, CapturingStubHandler stub) Build<T>(
        LlamaOptions options, string responseJson, Func<LlamaRouter, ModelAliasResolver, T> create)
    {
        var stub = new CapturingStubHandler(responseJson);
        var router = new LlamaRouter(new SingleBackendFactory(stub), Options.Create(options), NullLogger<LlamaRouter>.Instance);
        var resolver = new ModelAliasResolver(Options.Create(options));
        return (create(router, resolver), stub);
    }

    private static JsonElement Str(string s) => JsonSerializer.SerializeToElement(s);
    private static IResult Inner(INestedHttpResult result) => result.Result;

    [Fact]
    public async Task Chat_rewrites_alias_to_concrete_id_before_forwarding()
    {
        var (handler, stub) = Build(
            AliasOptions(),
            """{"id":"x","object":"chat.completion","created":0,"model":"qwen3-1.7b","choices":[]}""",
            (r, a) => new ChatHandler(r, a, NullLogger<ChatHandler>.Instance, TestJson.Options()));

        var req = new ChatCompletionRequest
        {
            Model = "assistant-fast",
            Messages = [new ChatMessage { Role = "user", Content = Str("hi") }],
        };
        var result = await handler.ChatAsync(req, new DefaultHttpContext(), default);

        Assert.IsType<Ok<ChatCompletionResponse>>(Inner(result));
        Assert.Contains("\"model\":\"qwen3-1.7b\"", stub.LastBody);
    }

    [Fact]
    public async Task Chat_forwards_raw_model_id_unchanged()
    {
        var (handler, stub) = Build(
            AliasOptions(),
            """{"id":"x","object":"chat.completion","created":0,"model":"qwen3-14b","choices":[]}""",
            (r, a) => new ChatHandler(r, a, NullLogger<ChatHandler>.Instance, TestJson.Options()));

        var req = new ChatCompletionRequest
        {
            Model = "qwen3-14b",
            Messages = [new ChatMessage { Role = "user", Content = Str("hi") }],
        };
        var result = await handler.ChatAsync(req, new DefaultHttpContext(), default);

        Assert.IsType<Ok<ChatCompletionResponse>>(Inner(result));
        Assert.Contains("\"model\":\"qwen3-14b\"", stub.LastBody);
    }

    [Fact]
    public async Task Chat_unrouted_model_still_returns_400()
    {
        var (handler, _) = Build(
            TwoBackendsNoDefault(),
            "{}",
            (r, a) => new ChatHandler(r, a, NullLogger<ChatHandler>.Instance, TestJson.Options()));

        var req = new ChatCompletionRequest
        {
            Model = "bogus",
            Messages = [new ChatMessage { Role = "user", Content = Str("hi") }],
        };
        var result = await handler.ChatAsync(req, new DefaultHttpContext(), default);

        var problem = Assert.IsType<ProblemHttpResult>(Inner(result));
        Assert.Equal(400, problem.ProblemDetails.Status);
    }

    [Fact]
    public async Task Embeddings_rewrites_alias_to_concrete_id_before_forwarding()
    {
        var (handler, stub) = Build(
            AliasOptions(),
            """{"object":"list","data":[]}""",
            (r, a) => new EmbeddingsHandler(r, a, NullLogger<EmbeddingsHandler>.Instance, TestJson.Options()));

        var req = new EmbeddingsRequest { Model = "embed", Input = Str("hi") };
        var result = await handler.EmbedAsync(req, default);

        Assert.IsType<Ok<EmbeddingsResponse>>(Inner(result));
        Assert.Contains("\"model\":\"qwen3-embedding-0.6b\"", stub.LastBody);
    }

    [Fact]
    public async Task Rerank_rewrites_alias_to_concrete_id_before_forwarding()
    {
        var (handler, stub) = Build(
            AliasOptions(),
            """{"object":"list","results":[]}""",
            (r, a) => new RerankHandler(r, a, NullLogger<RerankHandler>.Instance, TestJson.Options()));

        var req = new RerankRequest { Model = "rerank", Query = "q", Documents = ["a"] };
        var result = await handler.RerankAsync(req, default);

        Assert.IsType<Ok<RerankResponse>>(Inner(result));
        Assert.Contains("\"model\":\"qwen3-reranker-0.6b\"", stub.LastBody);
    }
}
