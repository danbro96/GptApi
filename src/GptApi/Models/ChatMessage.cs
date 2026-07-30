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
