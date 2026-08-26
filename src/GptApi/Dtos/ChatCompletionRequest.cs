using System.Text.Json;
using System.Text.Json.Serialization;

namespace GptApi.Dtos;

public sealed class ChatCompletionRequest
{
    public required string Model { get; set; }

    public required IReadOnlyList<ChatMessage> Messages { get; set; }

    public bool Stream { get; set; }

    public int? MaxTokens { get; set; }

    public float? Temperature { get; set; }

    public float? TopP { get; set; }

    public float? FrequencyPenalty { get; set; }

    public float? PresencePenalty { get; set; }

    public int? Seed { get; set; }

    /// <summary>
    /// Stop strings. OpenAI accepts either a single string or an array; raw element
    /// preserves whichever form the client sent.
    /// </summary>
    public JsonElement? Stop { get; set; }

    public string? User { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
