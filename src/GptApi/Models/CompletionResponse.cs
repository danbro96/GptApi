using System.Text.Json;
using System.Text.Json.Serialization;

namespace GptApi.Models;

public sealed class CompletionResponse
{
    public required string Id { get; set; }

    [JsonPropertyName("object")]
    public string Object { get; set; } = "text_completion";

    public long Created { get; set; }

    public required string Model { get; set; }

    public required IReadOnlyList<CompletionChoice> Choices { get; set; }

    public ChatCompletionUsage? Usage { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
