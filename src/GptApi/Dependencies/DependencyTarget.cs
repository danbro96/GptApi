namespace GptApi.Dependencies;

/// <summary>One outward edge: where to probe it. llama-swap's health endpoint is anonymous.</summary>
public sealed class DependencyTarget
{
    public required string Name { get; set; }

    public required string BaseUrl { get; set; }

    public required string ProbePath { get; set; }
}
