using GptApi.Dependencies;
using GptApi.Services;
using Xunit;

namespace GptApi.UnitTests;

/// <summary>
/// The /depz roster mirrors the configured llama backends: one anonymous health edge per backend,
/// named for the dependency graph, with the legacy single-worker URL still covered.
/// </summary>
public class DependencyTargetsTests
{
    [Fact]
    public void From_emits_one_health_edge_per_backend()
    {
        var targets = DependencyTargets.From(new LlamaOptions
        {
            Backends =
            {
                new BackendOptions { Name = "storm", Url = "http://storm:9000" },
                new BackendOptions { Name = "nasGpu", Url = "http://gpt-worker-a380:9000" },
                new BackendOptions { Name = "nasCpu", Url = "http://gpt-worker:9000" },
            },
        });

        Assert.Collection(
            targets,
            t => AssertTarget(t, "llama-swap-storm", "http://storm:9000"),
            t => AssertTarget(t, "llama-swap-nas-gpu", "http://gpt-worker-a380:9000"),
            t => AssertTarget(t, "llama-swap-nas-cpu", "http://gpt-worker:9000"));
    }

    [Fact]
    public void From_legacy_worker_url_yields_default_edge()
    {
        var targets = DependencyTargets.From(new LlamaOptions { WorkerUrl = "http://localhost:9000" });

        AssertTarget(Assert.Single(targets), "llama-swap-default", "http://localhost:9000");
    }

    [Fact]
    public void From_no_backends_yields_empty_roster()
    {
        Assert.Empty(DependencyTargets.From(new LlamaOptions()));
    }

    private static void AssertTarget(DependencyTarget target, string name, string baseUrl)
    {
        Assert.Equal(name, target.Name);
        Assert.Equal(baseUrl, target.BaseUrl);
        Assert.Equal("health", target.ProbePath);
    }
}
