using System.Text.Json.Serialization;

namespace GptApi.Models;

public sealed class ModelsResponse
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = "list";

    public required IReadOnlyList<ModelInfo> Data { get; set; }
}
