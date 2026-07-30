using GptApi.Handlers;
using GptApi.Models;
using GptApi.Services;
using GptApi.UnitTests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GptApi.UnitTests;

/// <summary><c>/v1/models</c> advertises the configured tier aliases alongside the backend-reported models.</summary>
public class ModelsAliasListingTests
{
    [Fact]
    public async Task ListAsync_includes_aliases_alongside_backend_models()
    {
        var options = new LlamaOptions
        {
            Backends = { new BackendOptions { Name = "cpu", Url = "http://stub:9000" } },
            DefaultBackend = "cpu",
            Aliases =
            {
                ["assistant-fast"] = "qwen3-1.7b",
                ["embed"] = "qwen3-embedding-0.6b",
            },
        };
        var stub = new CapturingStubHandler("""{"object":"list","data":[{"id":"qwen3-1.7b","object":"model","owned_by":"local"}]}""");
        var router = new LlamaRouter(new SingleBackendFactory(stub), Options.Create(options), NullLogger<LlamaRouter>.Instance);
        var resolver = new ModelAliasResolver(Options.Create(options));
        var handler = new ModelsHandler(router, resolver, new MemoryCache(new MemoryCacheOptions()), TestJson.Options());

        var result = await handler.ListAsync(default);

        var ok = Assert.IsType<Ok<ModelsResponse>>(((INestedHttpResult) result).Result);
        var ids = ok.Value!.Data.Select(m => m.Id).ToHashSet();
        Assert.Contains("qwen3-1.7b", ids);          // backend-reported
        Assert.Contains("assistant-fast", ids);      // alias
        Assert.Contains("embed", ids);               // alias
    }
}
