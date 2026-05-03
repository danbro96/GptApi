using System.Text.Json;
using System.Text.Json.Serialization;

namespace GptApi.Models;

public sealed class CompletionRequest
{
    public required string Model { get; set; }

    /// <summary>
    /// String or array of strings. Kept raw to preserve either form.
    /// </summary>
    public required JsonElement Prompt { get; set; }

    public bool Stream { get; set; }

    public int? MaxTokens { get; set; }

    public float? Temperature { get; set; }

    public float? TopP { get; set; }

    public int? Seed { get; set; }

    public JsonElement? Stop { get; set; }

    public string? User { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class CompletionChoice
{
    public int Index { get; set; }

    public required string Text { get; set; }

    public string? FinishReason { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

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
