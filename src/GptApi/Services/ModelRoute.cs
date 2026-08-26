namespace GptApi.Services;

public sealed class ModelRoute
{
    public string Model { get; set; } = string.Empty;

    public string Backend { get; set; } = string.Empty;

    public string? Fallback { get; set; }
}
