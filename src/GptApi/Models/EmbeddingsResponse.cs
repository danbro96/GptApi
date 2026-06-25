using System.Text.Json;
using System.Text.Json.Serialization;

namespace GptApi.Models;

public sealed class EmbeddingsResponse
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = "list";

    public required IReadOnlyList<EmbeddingData> Data { get; set; }

    public string? Model { get; set; }

    public EmbeddingsUsage? Usage { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class EmbeddingData
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = "embedding";

    public required IReadOnlyList<float> Embedding { get; set; }

    public int Index { get; set; }
}

public sealed class EmbeddingsUsage
{
    public int PromptTokens { get; set; }

    public int TotalTokens { get; set; }
}
