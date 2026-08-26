using System.Text.Json.Serialization;

namespace GptApi.Dtos;

public sealed class EmbeddingData
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = "embedding";

    public required IReadOnlyList<float> Embedding { get; set; }

    public int Index { get; set; }
}
