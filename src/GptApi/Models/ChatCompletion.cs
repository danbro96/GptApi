using System.Text.Json;
using System.Text.Json.Serialization;

namespace GptApi.Models;

public sealed class ChatMessage
{
    public required string Role { get; set; }

    /// <summary>
    /// Either a plain string or an array of content parts (multimodal). Kept as a raw
    /// JsonElement so both forms round-trip without lossy conversion.
    /// </summary>
    public required JsonElement Content { get; set; }

    public string? Name { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

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

public sealed class ChatCompletionChoice
{
    public int Index { get; set; }

    public required ChatMessage Message { get; set; }

    public string? FinishReason { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class ChatCompletionUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}

public sealed class ChatCompletionResponse
{
    public required string Id { get; set; }

    [JsonPropertyName("object")]
    public string Object { get; set; } = "chat.completion";

    public long Created { get; set; }

    public required string Model { get; set; }

    public required IReadOnlyList<ChatCompletionChoice> Choices { get; set; }

    public ChatCompletionUsage? Usage { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
