using System.Text.Json.Serialization;

namespace GptApi.Models;

public sealed class ModelInfo
{
    public required string Id { get; set; }

    [JsonPropertyName("object")]
    public string Object { get; set; } = "model";

    public long Created { get; set; }

    public string OwnedBy { get; set; } = "local";
}
