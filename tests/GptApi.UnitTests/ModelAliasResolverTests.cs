using GptApi.Models;
using GptApi.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace GptApi.UnitTests;

/// <summary>
/// The alias layer is pure name→id substitution applied before routing: a tier alias resolves to
/// its concrete model id, and any unknown id (a raw GGUF id) passes through untouched.
/// </summary>
public class ModelAliasResolverTests
{
    private static ModelAliasResolver Resolver() => new(Options.Create(new LlamaOptions
    {
        Aliases =
        {
            ["assistant-fast"] = "qwen3-1.7b",
            ["embed"] = "qwen3-embedding-0.6b",
        },
    }));

    [Fact]
    public void Resolve_maps_alias_to_concrete_id()
    {
        Assert.Equal("qwen3-1.7b", Resolver().Resolve("assistant-fast"));
        Assert.Equal("qwen3-embedding-0.6b", Resolver().Resolve("embed"));
    }

    [Fact]
    public void Resolve_passes_through_unknown_id_unchanged()
    {
        Assert.Equal("qwen3-14b", Resolver().Resolve("qwen3-14b"));
        Assert.Equal("", Resolver().Resolve(""));
    }

    [Fact]
    public void Resolve_is_case_insensitive()
    {
        Assert.Equal("qwen3-1.7b", Resolver().Resolve("Assistant-Fast"));
    }

    [Fact]
    public void Aliases_property_exposes_the_configured_map()
    {
        Assert.Contains("assistant-fast", Resolver().Aliases.Keys);
        Assert.Contains("embed", Resolver().Aliases.Keys);
    }
}
