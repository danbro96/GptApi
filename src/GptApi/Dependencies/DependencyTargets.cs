using System.Text.Json;
using GptApi.Services;

namespace GptApi.Dependencies;

/// <summary>Roster derived from the same options the real clients bind — edges cannot drift.</summary>
public static class DependencyTargets
{
    public static IReadOnlyList<DependencyTarget> From(LlamaOptions llama) =>
        llama.EffectiveBackends()
            .Select(b => new DependencyTarget
            {
                Name = $"llama-swap-{JsonNamingPolicy.KebabCaseLower.ConvertName(b.Name)}",
                BaseUrl = b.Url,
                ProbePath = "health",
            })
            .ToList();
}
