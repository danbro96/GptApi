using System.Text.Json;
using System.Text.Json.Serialization;

namespace GptApi.Models;

public sealed class CompletionChoice
{
    public int Index { get; set; }

    public required string Text { get; set; }

    public string? FinishReason { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
