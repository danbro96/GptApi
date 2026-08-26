namespace GptApi.Services;

public sealed class LlamaBackendPair
{
    public required string PrimaryName { get; init; }

    public required LlamaClient Primary { get; init; }

    public string? FallbackName { get; init; }

    public LlamaClient? Fallback { get; init; }
}
