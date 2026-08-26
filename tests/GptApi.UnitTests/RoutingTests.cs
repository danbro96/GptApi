using System.Net;
using GptApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GptApi.UnitTests;

/// <summary>
/// The gateway's per-model routing + transport-level failover: a model maps to its primary
/// backend (+ optional fallback), the fallback fires only when the primary worker is
/// unreachable, and a live worker's HTTP error is never failed over.
/// </summary>
public class RoutingTests
{
    private static LlamaOptions TwoBackendOptions() => new()
    {
        Backends =
        {
            new BackendOptions { Name = "pcGpu", Url = "http://pc:9000" },
            new BackendOptions { Name = "nasCpu", Url = "http://nas:9000" },
        },
        Routes =
        {
            new ModelRoute { Model = "qwen3-14b", Backend = "pcGpu", Fallback = "nasCpu" },
            new ModelRoute { Model = "qwen3-1.7b", Backend = "nasCpu" },
        },
        DefaultBackend = "nasCpu",
    };

    private static LlamaRouter Router(LlamaOptions options, Dictionary<string, StubHandler> handlers) =>
        new(new StubFactory(handlers), Options.Create(options), NullLogger<LlamaRouter>.Instance);

    [Fact]
    public void Resolve_maps_model_to_its_primary_and_fallback()
    {
        var router = Router(TwoBackendOptions(), Unused());

        var pair = router.Resolve("qwen3-14b");

        Assert.NotNull(pair);
        Assert.Equal("pcGpu", pair!.PrimaryName);
        Assert.Equal("nasCpu", pair.FallbackName);
    }

    [Fact]
    public void Resolve_unrouted_model_uses_default_backend_with_no_fallback()
    {
        var router = Router(TwoBackendOptions(), Unused());

        var pair = router.Resolve("some-unknown-model");

        Assert.NotNull(pair);
        Assert.Equal("nasCpu", pair!.PrimaryName);
        Assert.Null(pair.FallbackName);
    }

    [Fact]
    public void Resolve_returns_null_when_unrouted_and_no_default()
    {
        var options = TwoBackendOptions();
        options.DefaultBackend = null;   // two backends, no default → unroutable

        var router = Router(options, new());

        Assert.Null(router.Resolve("some-unknown-model"));
    }

    [Fact]
    public async Task InvokeAsync_fails_over_to_fallback_when_primary_is_unreachable()
    {
        var pcGpu = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var nasCpu = new StubHandler(_ => Ok("""{"served_by":"cpu"}"""));
        var router = Router(TwoBackendOptions(), Handlers(pcGpu, nasCpu));
        var pair = router.Resolve("qwen3-14b")!;

        var body = await router.InvokeAsync(pair, c => c.ChatCompletionAsync("{}", default), activity: null);

        Assert.Contains("cpu", body);
        Assert.Equal(1, pcGpu.Calls);
        Assert.Equal(1, nasCpu.Calls);
    }

    [Fact]
    public async Task InvokeAsync_does_not_fail_over_on_a_live_worker_http_error()
    {
        var pcGpu = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("model crashed"),
        });
        var nasCpu = new StubHandler(_ => Ok("{}"));
        var router = Router(TwoBackendOptions(), Handlers(pcGpu, nasCpu));
        var pair = router.Resolve("qwen3-14b")!;

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => router.InvokeAsync(pair, c => c.ChatCompletionAsync("{}", default), activity: null));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Equal(1, pcGpu.Calls);
        Assert.Equal(0, nasCpu.Calls);   // a live worker's error is the answer, not a failover trigger
    }

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private static Dictionary<string, StubHandler> Handlers(StubHandler pcGpu, StubHandler nasCpu) => new()
    {
        [LlamaRouter.ClientName("pcGpu")] = pcGpu,
        [LlamaRouter.ClientName("nasCpu")] = nasCpu,
    };

    // Resolve builds the LlamaClient pair eagerly; these handlers exist only so client
    // construction succeeds — their responders are never invoked by a Resolve-only test.
    private static Dictionary<string, StubHandler> Unused() => Handlers(
        new StubHandler(_ => throw new InvalidOperationException("should not be called")),
        new StubHandler(_ => throw new InvalidOperationException("should not be called")));

    private sealed class StubFactory(Dictionary<string, StubHandler> handlers) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handlers[name], disposeHandler: false) { BaseAddress = new Uri("http://stub.local") };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            try
            {
                return Task.FromResult(responder(request));
            }
            catch (Exception ex)
            {
                return Task.FromException<HttpResponseMessage>(ex);
            }
        }
    }
}
