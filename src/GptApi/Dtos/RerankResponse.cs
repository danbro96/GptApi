using System.Text.Json;
using System.Text.Json.Serialization;

namespace GptApi.Dtos;

public sealed class RerankResponse
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = "list";

    public required IReadOnlyList<RerankResult> Results { get; set; }

    public string? Model { get; set; }

    public EmbeddingsUsage? Usage { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
