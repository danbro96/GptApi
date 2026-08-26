using System.Text.Json;
using System.Text.Json.Serialization;

namespace GptApi.Dtos;

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
