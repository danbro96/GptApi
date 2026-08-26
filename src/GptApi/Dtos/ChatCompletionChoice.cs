using System.Text.Json;
using System.Text.Json.Serialization;

namespace GptApi.Dtos;

public sealed class ChatCompletionChoice
{
    public int Index { get; set; }

    public required ChatMessage Message { get; set; }

    public string? FinishReason { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
